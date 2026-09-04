using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Single-canvas grid control for the manual wallpaper picker.
    //
    // Why a single canvas instead of 200 WallpaperTile Controls inside a
    // FlowLayoutPanel (the previous design)? Each scroll step used to
    // relocate every child and trigger ~200 paints. Fast scrolls tore --
    // partial paints stacked on top of each other in unpredictable ways,
    // and even at rest the main thread was pinned to single-digit FPS
    // by GDI+ per-tile DrawImage + DrawText calls.
    //
    // This control owns one large Bitmap (every tile's thumbnail, label,
    // border, checkbox glyph pre-baked). OnPaint is a single
    // Graphics.DrawImage for the visible region. Tile toggles and
    // asynchronous thumbnail decodes repaint one small rectangle inside
    // the canvas and Invalidate(Rectangle) only that region on screen.
    internal class PickerCanvas : ScrollableControl
    {
        private static readonly Color Accent = Color.FromArgb(24, 95, 165);
        private static readonly Color BorderIdle = Color.FromArgb(176, 176, 176);
        private static readonly Color PlaceholderBg = Color.FromArgb(240, 240, 240);
        private const int TileRadius = 5;

        private readonly List<string> items = new List<string>();
        private readonly HashSet<int> picked = new HashSet<int>();
        private Bitmap canvas;
        private Bitmap[] thumbs;
        private int cellW;
        private int cellImgH;
        private int cellLabelH;
        private int cols;
        private int margin;
        private Font labelFont;
        private Font placeholderFont;

        public event Action<int, bool> TileToggled;
        public event Action<int> TileHover;

        public int ItemCount { get { return items.Count; } }
        public int PickedCount { get { return picked.Count; } }
        public IEnumerable<string> AllItems { get { return items; } }
        public string ItemAt(int idx) { return items[idx]; }

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
        }

        // Replace the entire dataset and rebuild the canvas. prePicked holds
        // indices into items that are checked on first paint.
        public void Configure(IList<string> paths, IEnumerable<int> prePicked,
            int cellW, int cellImgH, int cellLabelH, int cols, int margin, Font font)
        {
            DisposeCanvas();
            items.Clear();
            picked.Clear();
            if (paths != null) items.AddRange(paths);
            if (prePicked != null) foreach (int i in prePicked) picked.Add(i);

            this.cellW = Math.Max(80, cellW);
            this.cellImgH = Math.Max(45, cellImgH);
            this.cellLabelH = Math.Max(0, cellLabelH);
            this.cols = Math.Max(1, cols);
            this.margin = Math.Max(3, margin);
            if (font != null)
            {
                if (labelFont != null) labelFont.Dispose();
                labelFont = new Font(font, font.Style);
            }
            if (placeholderFont == null)
                placeholderFont = new Font(labelFont ?? new Font("Microsoft YaHei UI", 9F), FontStyle.Regular);

            if (items.Count == 0)
            {
                AutoScrollMinSize = Size.Empty;
                Invalidate();
                return;
            }

            thumbs = new Bitmap[items.Count];
            BakeCanvas();
        }

        // Replace the picked set in one shot (used by bulk All/None/Invert).
        // Only repaints tiles whose state actually flipped.
        public void ApplyPicked(HashSet<int> newPicked)
        {
            if (items.Count == 0) return;
            if (newPicked == null) newPicked = new HashSet<int>();
            HashSet<int> flipped = new HashSet<int>();
            for (int i = 0; i < items.Count; i++)
            {
                bool want = newPicked.Contains(i);
                bool have = picked.Contains(i);
                if (want != have) flipped.Add(i);
            }
            picked.Clear();
            foreach (int i in newPicked) picked.Add(i);
            foreach (int i in flipped) PaintTile(i);
            Invalidate();
        }

        // Caller hands ownership of bmp over; previous thumbnail at this slot
        // is disposed. The tile is repainted into the canvas.
        public void SetThumb(int idx, Bitmap bmp)
        {
            if (idx < 0 || idx >= items.Count || thumbs == null)
            {
                if (bmp != null) bmp.Dispose();
                return;
            }
            Bitmap old = thumbs[idx];
            thumbs[idx] = bmp;
            if (old != null) old.Dispose();
            PaintTile(idx);
            Invalidate(TileBoundsOnScreen(idx));
        }

        // True if the tile at idx matches the value the form's data layer holds.
        public bool IsPicked(int idx) { return picked.Contains(idx); }

        // Visible part of the canvas, in screen coords. Used by callers that
        // need to know where a tile currently is for tooltips etc.
        public Rectangle TileBoundsOnScreen(int idx)
        {
            RectangleF r = TileBoundsInCanvas(idx);
            int sx = (int)r.X + AutoScrollPosition.X;
            int sy = (int)r.Y + AutoScrollPosition.Y;
            return new Rectangle(sx, sy, (int)r.Width, (int)r.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (canvas == null)
            {
                e.Graphics.Clear(BackColor);
                return;
            }
            Rectangle clip = e.ClipRectangle;
            int dx = AutoScrollPosition.X;
            int dy = AutoScrollPosition.Y;
            int srcX = clip.Left - dx;
            int srcY = clip.Top - dy;
            if (srcX < 0) { clip.Width += srcX; srcX = 0; }
            if (srcY < 0) { clip.Height += srcY; srcY = 0; }
            if (clip.Width <= 0 || clip.Height <= 0) return;
            int srcW = Math.Min(clip.Width, canvas.Width - srcX);
            int srcH = Math.Min(clip.Height, canvas.Height - srcY);
            if (srcW <= 0 || srcH <= 0) return;
            Rectangle src = new Rectangle(srcX, srcY, srcW, srcH);
            Rectangle dst = new Rectangle(srcX + dx, srcY + dy, srcW, srcH);
            e.Graphics.DrawImage(canvas, dst, src, GraphicsUnit.Pixel);
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
            PaintTile(idx);
            Invalidate(TileBoundsOnScreen(idx));
            Action<int, bool> h = TileToggled;
            if (h != null) h(idx, nowPicked);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = HitTest(e.Location);
            Action<int> h = TileHover;
            if (h != null) h(idx);
        }

        // Convert screen point to tile index, or -1 if outside any tile.
        private int HitTest(Point pt)
        {
            if (items.Count == 0 || cols <= 0) return -1;
            int x = pt.X - AutoScrollPosition.X;
            int y = pt.Y - AutoScrollPosition.Y;
            int rowH = cellImgH + cellLabelH;
            int stepX = cellW + margin;
            int stepY = rowH + margin;
            int col = (x - margin) / stepX;
            int row = (y - margin) / stepY;
            if (col < 0 || col >= cols || row < 0) return -1;
            int idx = row * cols + col;
            if (idx >= items.Count) return -1;
            int insideX = x - margin - col * stepX;
            int insideY = y - margin - row * stepY;
            if (insideX < 0 || insideX >= cellW) return -1;
            if (insideY < 0 || insideY >= rowH) return -1;
            return idx;
        }

        private RectangleF TileBoundsInCanvas(int idx)
        {
            int col = idx % cols;
            int row = idx / cols;
            int rowH = cellImgH + cellLabelH;
            int x = margin + col * (cellW + margin);
            int y = margin + row * (rowH + margin);
            return new RectangleF(x, y, cellW, rowH);
        }

        private void BakeCanvas()
        {
            int rowCount = (items.Count + cols - 1) / cols;
            int rowH = cellImgH + cellLabelH;
            int totalW = margin + cols * cellW + cols * margin;
            int totalH = margin + rowCount * rowH + rowCount * margin;
            canvas = new Bitmap(totalW, totalH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(BackColor);
            }
            AutoScrollMinSize = new Size(totalW, totalH);
            for (int i = 0; i < items.Count; i++) PaintTile(i);
            Invalidate();
        }

        private void PaintTile(int idx)
        {
            if (canvas == null || idx < 0 || idx >= items.Count) return;
            RectangleF bounds = TileBoundsInCanvas(idx);
            int x = (int)bounds.X;
            int y = (int)bounds.Y;
            int w = (int)bounds.Width;
            int h = (int)bounds.Height;
            int imgH = cellImgH;

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // Wipe to background first so any prior content (including a
                // rounded-corner path) is fully gone before the new tile is
                // laid down.
                g.FillRectangle(SystemBrushes.Control, x - margin, y - margin,
                    w + margin * 2, h + margin * 2);

                // 16:9 image area
                Rectangle imgRect = new Rectangle(x, y, w, imgH);
                Bitmap thumb = thumbs == null ? null : thumbs[idx];
                if (thumb != null)
                {
                    g.DrawImage(thumb, imgRect);
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

                if (picked.Contains(idx))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(26, Accent)))
                    {
                        g.FillRectangle(b, imgRect);
                    }
                }

                DrawCheckBox(g, x + 6, y + 6, picked.Contains(idx));

                if (cellLabelH > 0)
                {
                    Rectangle labelRect = new Rectangle(x, y + imgH, w, cellLabelH);
                    g.FillRectangle(SystemBrushes.Control, labelRect);
                    TextRenderer.DrawText(g, Path.GetFileName(items[idx]), labelFont,
                        new Rectangle(x + 3, y + imgH, w - 6, cellLabelH - 2),
                        Color.FromArgb(70, 70, 70),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                        TextFormatFlags.NoPrefix);
                }

                bool hot = picked.Contains(idx);
                using (GraphicsPath path = RoundedRect(new Rectangle(x, y, w - 1, h - 1), TileRadius))
                using (Pen pen = new Pen(hot ? Accent : BorderIdle, hot ? 2f : 1f))
                {
                    g.DrawPath(pen, path);
                }
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

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void DisposeCanvas()
        {
            if (thumbs != null)
            {
                foreach (Bitmap b in thumbs)
                {
                    if (b != null) b.Dispose();
                }
                thumbs = null;
            }
            if (canvas != null)
            {
                canvas.Dispose();
                canvas = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCanvas();
                if (labelFont != null) { labelFont.Dispose(); labelFont = null; }
                if (placeholderFont != null) { placeholderFont.Dispose(); placeholderFont = null; }
            }
            base.Dispose(disposing);
        }
    }
}