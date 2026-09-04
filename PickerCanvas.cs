using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
// WIC (Windows Imaging Component) managed wrappers, referenced only for the
// WebP fallback decoder. Aliased so System.Windows.Media.Color cannot clash
// with System.Drawing.Color elsewhere in this file.
using WicBitmapDecoder = System.Windows.Media.Imaging.BitmapDecoder;
using WicBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using WicFormatConvertedBitmap = System.Windows.Media.Imaging.FormatConvertedBitmap;
using WicInt32Rect = System.Windows.Int32Rect;
using WicPixelFormats = System.Windows.Media.PixelFormats;

namespace WallpaperChanger
{
    // Virtualized thumbnail grid for the manual wallpaper picker.
    //
    // Design (industry standard: ListView virtual mode / Explorer large-icon
    // view / Lightroom grid). It NEVER renders the whole library and NEVER
    // bakes one giant bitmap, because both of those froze the UI:
    //
    //   1. First attempt: ~200 WallpaperTile Controls inside a FlowLayoutPanel
    //      -- every scroll relocated+painted all children (single-digit FPS,
    //      tearing on fast drags).
    //   2. Second attempt: one PickerCanvas that pre-baked every tile into a
    //      single large Bitmap on Configure(). Any window-size change changed
    //      the column layout, forcing a full synchronous re-bake on the UI
    //      thread (~37 MB alloc + 200 Graphics passes), so opening the dialog
    //      froze ~6 s and maximize dead-locked the form.
    //
    // What this control does instead:
    //   - Data (paths + picked indices) and drawing are fully decoupled.
    //   - AutoScroll only moves the scrollbar offset; there are no children.
    //   - OnPaint draws ONLY the tiles inside the visible viewport (a screen
    //     holds ~35 tiles, whatever the library size is). Each tile is a 1:1
    //     blit from the thumbnail cache when ready, or a placeholder when the
    //     decode is still running.
    //   - Thumbnails decode in the background (SemaphoreSlim=4) keyed by the
    //     file path, so scrolling, filtering and re-layout reuse them.
    //   - Scrolling / resizing only Invalidate() and repaint the viewport.
    //     Layout recomputation on resize is debounced so dragging the window
    //     never triggers work in a tight loop.
    internal class PickerCanvas : ScrollableControl
    {
        private static readonly Color Accent = Color.FromArgb(24, 95, 165);
        private static readonly Color BorderIdle = Color.FromArgb(176, 176, 176);
        private static readonly Color PlaceholderBg = Color.FromArgb(240, 240, 240);
        private const int DesignCols = 7;

        private readonly List<string> paths = new List<string>();
        private readonly HashSet<int> picked = new HashSet<int>();
        private readonly Dictionary<string, Bitmap> thumbCache =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> decodeQueue = new Queue<string>();
        private readonly HashSet<string> decoding = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim decodeGate = new SemaphoreSlim(4);
        private readonly System.Windows.Forms.Timer relayoutTimer;
        private Font labelFont;
        private Font placeholderFont;

        private int spacing = 8;          // gap between tiles (physical px)
        private int cellW = 258;
        private int cellImgH = 145;
        private int cellLabelH = 22;
        private int cols = DesignCols;
        private int rows;
        private bool dirtyThumbsForCell;  // cell size changed -> cache invalid

        public event Action<int, bool> TileToggled;

        public int ItemCount { get { return paths.Count; } }
        public string ItemAt(int idx) { return paths[idx]; }
        public bool IsPicked(int idx) { return picked.Contains(idx); }

        public PickerCanvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.Opaque, true);
            AutoScroll = true;
            HScroll = false;
            VScroll = true;
            BackColor = SystemColors.Control;
            TabStop = false;
            labelFont = new Font("Microsoft YaHei UI", 9F);
            placeholderFont = new Font(labelFont, FontStyle.Regular);
            relayoutTimer = new System.Windows.Forms.Timer();
            relayoutTimer.Interval = 200;
            relayoutTimer.Tick += delegate { relayoutTimer.Stop(); Relayout(false); };
        }

        // Replace the dataset (the display subset after filtering) and reset
        // the layout. prePicked holds indices into paths that are checked.
        // Thumbnail cache survives (keyed by file path), except when the cell
        // geometry changed, in which case thumbnails are discarded and
        // re-decoded at the new size.
        public void SetWallpapers(List<string> displayPaths, IEnumerable<int> prePicked)
        {
            paths.Clear();
            picked.Clear();
            if (displayPaths != null) paths.AddRange(displayPaths);
            if (prePicked != null) foreach (int i in prePicked) picked.Add(i);

            if (paths.Count == 0)
            {
                AutoScrollMinSize = Size.Empty;
                Invalidate();
                return;
            }
            Relayout(true);
            PrimeDecodeQueue();
        }

        // Bulk replacement of the picked set (全选/全不选/反选 or toggle from
        // the data layer). Redraws only the tiles whose state flipped.
        public void ApplyPicked(HashSet<int> newPicked)
        {
            if (paths.Count == 0) return;
            List<int> flipped = new List<int>();
            for (int i = 0; i < paths.Count; i++)
            {
                bool want = newPicked != null && newPicked.Contains(i);
                bool have = picked.Contains(i);
                if (want != have)
                {
                    flipped.Add(i);
                    if (want) picked.Add(i); else picked.Remove(i);
                }
            }
            Invalidate(flipped);
        }

        // Push one freshly decoded thumbnail. Ownership transfers to the
        // cache. The affected tile is repainted if it is on screen.
        public void SetThumb(string path, Bitmap bmp)
        {
            if (bmp == null) return;
            if (IsDisposed || Disposing)
            {
                bmp.Dispose();
                return;
            }
            Bitmap old;
            if (thumbCache.TryGetValue(path, out old))
            {
                if (ReferenceEquals(old, bmp)) { bmp.Dispose(); return; }
                if (old != null) old.Dispose();
            }
            thumbCache[path] = bmp;
            Invalidate(TileRectByPath(path));
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (paths.Count == 0 || Disposing) return;
            // Debounce: dragging the window fires many resizes; only relayout
            // once the user pauses.
            relayoutTimer.Stop();
            relayoutTimer.Start();
        }

        private void Relayout(bool forceResetScroll)
        {
            relayoutTimer.Stop();
            if (paths.Count == 0 || ClientSize.Width < 40) return;

            int oldImgW = cellW;
            ComputeMetrics();

            if (cellW != oldImgW)
            {
                // Cell geometry changed (window width crossed a threshold):
                // cached thumbnails were decoded for the old size.
                dirtyThumbsForCell = true;
            }
            if (dirtyThumbsForCell && cellW != oldImgW)
            {
                // Cache may hold null placeholders for files that failed to
                // decode; those must not be disposed.
                foreach (Bitmap b in thumbCache.Values)
                {
                    if (b != null) b.Dispose();
                }
                thumbCache.Clear();
                decoding.Clear();
                dirtyThumbsForCell = false;
                PrimeDecodeQueue();
            }

            rows = (paths.Count + cols - 1) / cols;
            int contentW = spacing + cols * (cellW + spacing);
            int rowH = cellImgH + cellLabelH;
            int contentH = spacing + rows * (rowH + spacing);
            AutoScrollMinSize = new Size(contentW, contentH);
            if (forceResetScroll) AutoScrollPosition = new Point(0, 0);
            Invalidate();
        }

        private void ComputeMetrics()
        {
            spacing = Math.Max(6, ClientSize.Width / 220);
            int avail = ClientSize.Width - spacing * 2
                - SystemInformation.VerticalScrollBarWidth;
            if (avail < 200) avail = 200;
            cellW = (avail - (DesignCols - 1) * spacing) / DesignCols;
            if (cellW < 120) cellW = 120;
            if (cellW > 330) cellW = 330;
            int availCols = Math.Max(1, (avail + spacing) / (cellW + spacing));
            // The bigger the window, the more columns fit; the grid simply
            // recomputes the row/col mapping on relayout.
            int calcCols = Math.Max(1, avail / Math.Max(1, cellW + spacing));
            cols = Math.Max(1, calcCols);
            cellImgH = (int)(cellW * 9f / 16f);
            cellLabelH = Math.Max(20,
                TextRenderer.MeasureText("Ag", labelFont).Height + 6);
            // Discard layout vars that are not needed.
            GC.KeepAlive(availCols);
        }

        private void PrimeDecodeQueue()
        {
            if (paths.Count == 0) return;
            decodeQueue.Clear();
            // Visible rows first so the first screen fills fast.
            int firstVisible = VisibleRowStart();
            List<string> ordered = new List<string>();
            int total = paths.Count;
            int from = Math.Max(0, firstVisible - 1);
            for (int step = 0; step < total; step++)
            {
                int idx = (from + step) % total;
                ordered.Add(paths[idx]);
            }
            foreach (string p in ordered)
            {
                if (!thumbCache.ContainsKey(p) && !decoding.Contains(p))
                    decodeQueue.Enqueue(p);
            }
            PumpDecoder();
        }

        private void PumpDecoder()
        {
            int startCount = decoding.Count;
            while (decoding.Count < 4 && decodeQueue.Count > 0)
            {
                string path = decodeQueue.Dequeue();
                decoding.Add(path);
                int w = cellW;
                int h = cellImgH;
                Task.Run(delegate
                {
                    Bitmap bmp = MakeThumb(path, w, h);
                    decoding.Remove(path);
                    if (bmp != null)
                    {
                        SafeUi(delegate { SetThumb(path, bmp); });
                    }
                    else
                    {
                        // Decode failed: remember a null so we do not queue
                        // this path forever.
                        SafeUi(delegate
                        {
                            thumbCache[path] = null;
                        });
                    }
                    // Continue draining the queue from a thread-pool thread.
                    SafeUi(PumpDecoder);
                });
            }
            if (startCount == 0 && decodeQueue.Count == 0)
            {
                // all queued items are decoding or cached; nothing to do
            }
        }

        private int VisibleRowStart()
        {
            if (cols <= 0) return 0;
            int oy = -AutoScrollPosition.Y;
            int rowH = cellImgH + cellLabelH + spacing;
            int r = oy / Math.Max(1, rowH);
            return Math.Max(0, r * cols);
        }

        private void Invalidate(List<int> tileIndices)
        {
            foreach (int i in tileIndices)
            {
                Rectangle r = TileRectOnScreen(i);
                if (r.IntersectsWith(ClientRectangle)) Invalidate(r);
            }
        }

        private Rectangle TileRectByPath(string path)
        {
            int idx = paths.IndexOf(path);
            if (idx < 0) return Rectangle.Empty;
            return TileRectOnScreen(idx);
        }

        private Rectangle TileRectOnScreen(int idx)
        {
            if (cols <= 0) return Rectangle.Empty;
            int col = idx % cols;
            int row = idx / cols;
            int rowH = cellImgH + cellLabelH;
            int x = spacing + col * (cellW + spacing) + AutoScrollPosition.X;
            int y = spacing + row * (rowH + spacing) + AutoScrollPosition.Y;
            return new Rectangle(x, y, cellW, rowH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            if (paths.Count == 0)
            {
                g.Clear(BackColor);
                return;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.Low;

            // The Graphics of a ScrollableControl is NOT translated by the
            // scroll offset, so map content coordinates to viewport
            // coordinates ourselves. AutoScrollPosition is <= 0 once the
            // viewport is scrolled into the content, and ADDING it to a
            // content coordinate yields the viewport coordinate.
            Rectangle clip = e.ClipRectangle;
            int ox = AutoScrollPosition.X;
            int oy = AutoScrollPosition.Y;

            // The Opaque control style skips OnPaintBackground, so the area
            // of the viewport not covered by tiles must be filled here,
            // otherwise it keeps stale/black pixels.
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, clip);
            }
            int rowH = cellImgH + cellLabelH;
            int pitchX = cellW + spacing;
            int pitchY = rowH + spacing;

            int firstCol = Math.Max(0, (clip.Left - ox - spacing) / pitchX);
            int lastCol = Math.Min(cols - 1,
                (clip.Right - ox - spacing) / pitchX);
            int firstRow = Math.Max(0, (clip.Top - oy - spacing) / pitchY);
            int lastRow = Math.Min(rows - 1,
                (clip.Bottom - oy - spacing) / pitchY);

            for (int r = firstRow; r <= lastRow; r++)
            {
                for (int c = firstCol; c <= lastCol; c++)
                {
                    int idx = r * cols + c;
                    if (idx >= paths.Count) continue;
                    int x = ox + spacing + c * pitchX;
                    int y = oy + spacing + r * pitchY;
                    DrawTile(g, idx, x, y);
                }
            }
        }

        private void DrawTile(Graphics g, int idx, int x, int y)
        {
            bool sel = picked.Contains(idx);
            string path = paths[idx];
            Bitmap thumb;
            thumbCache.TryGetValue(path, out thumb);

            // image area
            Rectangle imgRect = new Rectangle(x, y, cellW, cellImgH);
            if (thumb != null)
            {
                g.DrawImage(thumb, imgRect, 0, 0, thumb.Width, thumb.Height,
                    GraphicsUnit.Pixel);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(PlaceholderBg))
                {
                    g.FillRectangle(b, imgRect);
                }
                TextRenderer.DrawText(g, "...", placeholderFont, imgRect, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (sel)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(26, Accent)))
                {
                    g.FillRectangle(b, imgRect);
                }
            }
            DrawCheckBox(g, x + 6, y + 6, sel);

            // file-name strip
            if (cellLabelH > 0)
            {
                Rectangle labelRect = new Rectangle(x, y + cellImgH, cellW, cellLabelH);
                using (SolidBrush b = new SolidBrush(SystemColors.Control))
                {
                    g.FillRectangle(b, labelRect);
                }
                TextRenderer.DrawText(g, Path.GetFileName(path), labelFont,
                    new Rectangle(x + 3, y + cellImgH, cellW - 6, cellLabelH - 2),
                    Color.FromArgb(70, 70, 70),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix);
            }

            using (Pen pen = new Pen(sel ? Accent : BorderIdle, sel ? 2f : 1f))
            {
                g.DrawRectangle(pen, x, y, cellW - 1,
                    cellImgH + cellLabelH - 1);
            }
        }

        private static void DrawCheckBox(Graphics g, int x, int y, bool on)
        {
            const int box = 18;
            using (SolidBrush fill = new SolidBrush(on ? Accent : Color.White))
            {
                g.FillRectangle(fill, x, y, box, box);
            }
            using (Pen border = new Pen(on ? Accent : Color.FromArgb(120, 120, 120), 1.5f))
            {
                g.DrawRectangle(border, x + 1, y + 1, box - 3, box - 3);
            }
            if (on)
            {
                using (Pen tick = new Pen(Color.White, 2f))
                {
                    g.DrawLine(tick, x + 4.5f, y + 9.5f, x + 7.5f, y + 12.5f);
                    g.DrawLine(tick, x + 7.5f, y + 12.5f, x + 13.5f, y + 5f);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = HitTest(e.Location);
            if (idx < 0) return;
            bool nowPicked;
            if (picked.Contains(idx)) { picked.Remove(idx); nowPicked = false; }
            else { picked.Add(idx); nowPicked = true; }
            Invalidate(TileRectOnScreen(idx));
            Action<int, bool> h = TileToggled;
            if (h != null) h(idx, nowPicked);
        }

        private int HitTest(Point pt)
        {
            if (paths.Count == 0 || cols <= 0) return -1;
            int x = pt.X - AutoScrollPosition.X - spacing;
            int y = pt.Y - AutoScrollPosition.Y - spacing;
            int rowH = cellImgH + cellLabelH;
            int col = x / Math.Max(1, cellW + spacing);
            int row = y / Math.Max(1, rowH + spacing);
            if (col < 0 || col >= cols || row < 0) return -1;
            int idx = row * cols + col;
            if (idx >= paths.Count) return -1;
            return idx;
        }

        // ---- thumbnail decoding (shared by every layout) ----

        private static Bitmap MakeThumb(string path, int w, int h)
        {
            if (w < 8 || h < 8) return null;
            try
            {
                using (Image src = DecodeAny(path))
                {
                    if (src == null || src.Width < 8 || src.Height < 8) return null;
                    float scale = Math.Max((float)w / src.Width, (float)h / src.Height);
                    float cropW = Math.Min(src.Width, w / scale);
                    float cropH = Math.Min(src.Height, h / scale);
                    float sx = (src.Width - cropW) / 2f;
                    float sy = (src.Height - cropH) / 2f;
                    Bitmap bmp = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.DrawImage(src, new RectangleF(0, 0, w, h),
                            new RectangleF(sx, sy, cropW, cropH), GraphicsUnit.Pixel);
                    }
                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }

        private static Image DecodeAny(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (Image tmp = Image.FromStream(fs))
                {
                    if (tmp.Width < 8 || tmp.Height < 8) return null;
                    return CopyToArgb(tmp);
                }
            }
            catch
            {
            }
            try
            {
                WicBitmapDecoder dec = WicBitmapDecoder.Create(new Uri(path),
                    System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                if (dec == null || dec.Frames.Count == 0) return null;
                WicBitmapSource src = dec.Frames[0];
                if (src == null || src.PixelWidth < 8 || src.PixelHeight < 8) return null;
                WicBitmapSource bgra = new WicFormatConvertedBitmap(src,
                    WicPixelFormats.Bgra32, null, 0);
                Bitmap bmp = new Bitmap(bgra.PixelWidth, bgra.PixelHeight,
                    PixelFormat.Format32bppArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    bgra.CopyPixels(new WicInt32Rect(0, 0, bgra.PixelWidth, bgra.PixelHeight),
                        data.Scan0, data.Stride * bgra.PixelHeight, data.Stride);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap CopyToArgb(Image img)
        {
            Bitmap copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(copy))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(img, 0, 0, img.Width, img.Height);
            }
            return copy;
        }

        private void SafeUi(Action a)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                relayoutTimer.Dispose();
                foreach (Bitmap b in thumbCache.Values)
                {
                    if (b != null) b.Dispose();
                }
                thumbCache.Clear();
                if (labelFont != null) { labelFont.Dispose(); labelFont = null; }
                if (placeholderFont != null) { placeholderFont.Dispose(); placeholderFont = null; }
            }
            base.Dispose(disposing);
        }
    }
}