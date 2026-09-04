using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Manual wallpaper picker. Opens centered at ~3/4 of the working area on
    // the screen that owns the main window and can be maximized. The master
    // switch at the top-left is the gate: while off, the checked set below is
    // saved but does not restrict switching. The middle is a scrollable grid
    // of 16:9 tiles (7 columns at the default size, more when widened). The
    // checked set lives at the data level (scanned files + picked paths), so
    // filter / bulk actions are cheap even for huge libraries; tiles are only
    // materialized around the viewport and thumbnails decode in the
    // background. Explicit save model: only 保存 writes Config and the ini.
    public class ManualPickerForm : Form
    {
        private const int DesignCols = 7;     // columns at the default window size
        private static readonly Color Accent = Color.FromArgb(24, 95, 165);

        private readonly Form ownerForm;
        private CheckBox chkMaster;
        private Label lblMasterHint;
        private Button btnAll;
        private Button btnNone;
        private Button btnInvert;
        private TextBox txtFilter;
        private Label lblPlaceholder;
        private Label lblCount;
        private FlowLayoutPanel grid;
        private Label lblGridInfo;
        private Label lblBottomHint;
        private Button btnClose;
        private Button btnSave;
        private ToolTip toolTip;

        private readonly List<string> scanned = new List<string>();
        private readonly List<WallpaperTile> tiles = new List<WallpaperTile>();
        private readonly HashSet<string> picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim thumbGate = new SemaphoreSlim(4);

        private int nextTileIndex;
        private int cellW;
        private int cellImgH;
        private int cellLabelH;
        private int tileMargin;
        private bool scanFinished;
        private bool closing;
        private bool dirty;
        private bool loadingInitial = true;
        private string savedMessage;
        private float sf = 1f;

        public ManualPickerForm(Form owner)
        {
            ownerForm = owner;
            Text = "手动壁纸选择 - WallpaperChanger v" + Application.ProductVersion;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;   // centered on the owner's screen in OnLoad
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = SystemColors.Control;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildChrome();
            CancelButton = btnClose;
        }

        private void BuildChrome()
        {
            chkMaster = new CheckBox();
            chkMaster.Text = "启用手动选择功能";
            chkMaster.Font = new Font(Font, FontStyle.Bold);
            chkMaster.SetBounds(16, 12, 220, 26);
            chkMaster.CheckedChanged += delegate { if (!loadingInitial) dirty = true; };
            Controls.Add(chkMaster);

            lblMasterHint = new Label();
            lblMasterHint.Text = "总开关 · 关闭状态下，下方勾选只保存、不参与切换";
            lblMasterHint.AutoSize = true;
            lblMasterHint.SetBounds(244, 15, 10, 10);
            lblMasterHint.ForeColor = Color.FromArgb(96, 96, 96);
            Controls.Add(lblMasterHint);

            btnAll = new Button();
            btnAll.Text = "全选";
            btnAll.SetBounds(0, 48, 66, 28);
            btnAll.Click += delegate { BulkToggle(BulkKind.All); };
            Controls.Add(btnAll);

            btnNone = new Button();
            btnNone.Text = "全不选";
            btnNone.SetBounds(0, 48, 66, 28);
            btnNone.Click += delegate { BulkToggle(BulkKind.None); };
            Controls.Add(btnNone);

            btnInvert = new Button();
            btnInvert.Text = "反选";
            btnInvert.SetBounds(0, 48, 66, 28);
            btnInvert.Click += delegate { BulkToggle(BulkKind.Invert); };
            Controls.Add(btnInvert);

            txtFilter = new TextBox();
            txtFilter.SetBounds(0, 48, 300, 28);
            txtFilter.TextChanged += delegate { OnFilterChanged(); };
            txtFilter.Enter += delegate { UpdatePlaceholder(); };
            txtFilter.Leave += delegate { UpdatePlaceholder(); };
            Controls.Add(txtFilter);

            lblPlaceholder = new Label();
            lblPlaceholder.Text = "筛选文件名（不区分大小写）…";
            lblPlaceholder.ForeColor = Color.Gray;
            lblPlaceholder.AutoSize = false;
            lblPlaceholder.SetBounds(0, 51, 280, 22);
            lblPlaceholder.Click += delegate { txtFilter.Focus(); };
            Controls.Add(lblPlaceholder);

            lblCount = new Label();
            lblCount.Text = "已选 0 / 共 0";
            lblCount.AutoSize = false;
            lblCount.TextAlign = ContentAlignment.MiddleRight;
            lblCount.ForeColor = Accent;
            lblCount.SetBounds(0, 48, 150, 26);
            Controls.Add(lblCount);

            grid = new FlowLayoutPanel();
            grid.AutoScroll = true;
            grid.WrapContents = true;
            grid.BackColor = SystemColors.Control;
            grid.SetBounds(14, 92, 400, 400);
            grid.Scroll += delegate { OnGridScroll(); };
            Controls.Add(grid);

            lblGridInfo = new Label();
            lblGridInfo.Text = "";
            lblGridInfo.ForeColor = Color.Gray;
            lblGridInfo.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(lblGridInfo);

            lblBottomHint = new Label();
            lblBottomHint.Text = "";
            lblBottomHint.ForeColor = Color.FromArgb(96, 96, 96);
            lblBottomHint.AutoEllipsis = true;
            lblBottomHint.SetBounds(16, 0, 600, 22);
            Controls.Add(lblBottomHint);

            btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.BackColor = Accent;
            btnSave.ForeColor = Color.White;
            btnSave.SetBounds(0, 0, 96, 34);
            btnSave.Click += delegate { Save(); };
            Controls.Add(btnSave);

            btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.SetBounds(0, 0, 96, 34);
            btnClose.Click += delegate { RequestClose(); };
            Controls.Add(btnClose);

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 6000;
            toolTip.InitialDelay = 400;
            toolTip.ReshowDelay = 200;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            sf = DeviceDpi / 96f;

            int minW = (int)(940 * sf);
            int minH = (int)(620 * sf);
            MinimumSize = new Size(minW, minH);

            Screen scr = Screen.FromControl(ownerForm ?? this);
            Rectangle wa = scr.WorkingArea;
            int w = (int)(wa.Width * 0.75f);
            int h = (int)(wa.Height * 0.75f);
            if (w > wa.Width - 24) w = wa.Width - 24;
            if (h > wa.Height - 24) h = wa.Height - 24;
            if (w < minW) w = Math.Min(minW, wa.Width - 24);
            if (h < minH) h = Math.Min(minH, wa.Height - 24);
            Size = new Size(w, h);
            Location = new Point(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);

            loadingInitial = true;
            chkMaster.Checked = Config.ManualSelectionEnabled;
            loadingInitial = false;

            LayoutChrome();
            UpdateHint();
            StartScan();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (grid != null && !closing) LayoutChrome();
        }

        // Positions the right-aligned chrome and the middle grid. Controls the
        // AutoScale pass already converted to physical pixels keep their Y;
        // only sizes/offsets that depend on the current window size move.
        private void LayoutChrome()
        {
            if (sf < 0.5f) sf = DeviceDpi / 96f;
            int right = ClientSize.Width - (int)(14 * sf);
            int gap = (int)(10 * sf);

            lblCount.Location = new Point(right - lblCount.Width, btnAll.Top);
            txtFilter.Width = Math.Max(220, (int)(300 * sf));
            txtFilter.Location = new Point(lblCount.Left - txtFilter.Width - gap, btnAll.Top);
            lblPlaceholder.Location = new Point(txtFilter.Left + 6, txtFilter.Top + 4);
            lblPlaceholder.Width = txtFilter.Width - 12;
            btnInvert.Location = new Point(txtFilter.Left - btnInvert.Width - gap, btnAll.Top);
            btnNone.Location = new Point(btnInvert.Left - btnNone.Width - (int)(6 * sf), btnAll.Top);
            btnAll.Location = new Point(btnNone.Left - btnAll.Width - (int)(6 * sf), btnAll.Top);

            int top = Math.Max(btnAll.Bottom, txtFilter.Bottom) + (int)(10 * sf);
            int bottomBarH = (int)(54 * sf);
            grid.SetBounds((int)(14 * sf), top,
                ClientSize.Width - (int)(28 * sf),
                Math.Max(120, ClientSize.Height - top - bottomBarH));

            lblGridInfo.SetBounds(grid.Left + (int)(30 * sf), grid.Top + (int)(26 * sf),
                grid.Width - (int)(60 * sf), (int)(60 * sf));
            lblGridInfo.Font = Font;

            btnSave.Location = new Point(right - btnSave.Width,
                ClientSize.Height - btnSave.Height - (int)(10 * sf));
            btnClose.Location = new Point(btnSave.Left - btnClose.Width - (int)(10 * sf),
                btnSave.Top);
            lblBottomHint.SetBounds((int)(16 * sf), btnSave.Top + (int)(4 * sf),
                Math.Max(100, btnClose.Left - (int)(16 * sf) - (int)(26 * sf)), 22);
        }

        private void StartScan()
        {
            List<string> folders = new List<string>(Config.Folders);
            bool recursive = Config.Recursive;
            ShowGridInfo("正在扫描图片…");
            Task.Run(delegate
            {
                List<string> found = ImageScanner.ScanMany(folders, recursive);
                SafeUi(delegate { OnScanDone(found); });
            });
        }

        private void OnScanDone(List<string> found)
        {
            scanned.Clear();
            scanned.AddRange(found);

            picked.Clear();
            HashSet<string> saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in Config.ManualPicked)
            {
                try { saved.Add(Path.GetFullPath(p)); }
                catch { }
            }
            foreach (string p in scanned)
            {
                if (saved.Contains(Normalize(p))) picked.Add(Normalize(p));
            }

            scanFinished = true;
            UpdateCountText();
            if (scanned.Count == 0)
            {
                ShowGridInfo("没有找到可用壁纸，请先在主窗口的壁纸源里添加图片文件夹");
                return;
            }
            EnsureCellMetrics();
            ShowGridInfo("");
            EnsureEnoughTiles();
        }

        private void EnsureCellMetrics()
        {
            int padH = (int)(10 * sf);
            int sbW = SystemInformation.VerticalScrollBarWidth;
            int avail = grid.ClientSize.Width - padH * 2 - sbW;
            int gap = (int)(8 * sf);
            cellW = (avail - (DesignCols - 1) * gap) / DesignCols;
            if (cellW < 120) cellW = 120;
            cellImgH = (int)(cellW * 9f / 16f);
            cellLabelH = TextRenderer.MeasureText("Ag", Font).Height + 7;
            tileMargin = gap / 2;
            if (tileMargin < 3) tileMargin = 3;
        }

        // Column pitch (cell + margins), used to translate pixels to tiles.
        private int TilePitchX()
        {
            return cellW + tileMargin * 2;
        }

        private int TilePitchY()
        {
            return cellImgH + cellLabelH + tileMargin * 2;
        }

        private int EstimateColumns()
        {
            if (cellW <= 0) return DesignCols;
            int avail = grid.ClientSize.Width - (int)(20 * sf) - SystemInformation.VerticalScrollBarWidth;
            return Math.Max(1, avail / Math.Max(1, TilePitchX()));
        }

        // Create every remaining tile so the grid shows the full library up
        // front (no hidden "load more" paginator, no scroll-to-bottom dance).
        // Tile Controls are very cheap; thumbnails decode in the background.
        private void EnsureEnoughTiles()
        {
            if (!scanFinished || closing || cellW <= 0) return;
            while (nextTileIndex < scanned.Count)
            {
                string path = scanned[nextTileIndex];
                nextTileIndex++;
                CreateTile(path);
            }
            ApplyFilter();
        }

        private void OnGridScroll()
        {
            if (!scanFinished || closing) return;
            // Idempotent: any tiles not yet created (e.g. user typed in the
            // filter before scan finished) get materialised now. Otherwise
            // this is a cheap no-op.
            EnsureEnoughTiles();
        }

        private void OnFilterChanged()
        {
            UpdatePlaceholder();
            if (!scanFinished || cellW <= 0) return;
            ApplyFilter();
            string f = currentFilter();
            if (f.Length > 0)
            {
                ScrollToFirstMatch();
                ShowGridInfo(CountVisibleTiles() == 0 ? "没有匹配的文件名" : "");
            }
            else
            {
                ShowGridInfo("");
            }
        }

        // Bring the first visible tile into the viewport so the user sees
        // that the filter found something. Best-effort; scroll metrics are
        // not always available during layout.
        private void ScrollToFirstMatch()
        {
            if (grid == null || tiles.Count == 0) return;
            int cols = EstimateColumns();
            for (int i = 0; i < tiles.Count; i++)
            {
                if (!tiles[i].Visible) continue;
                int row = i / Math.Max(1, cols);
                int y = Math.Max(0, row * TilePitchY() - (int)(4 * sf));
                try { grid.AutoScrollPosition = new Point(0, y); }
                catch { }
                return;
            }
        }

        private int CountVisibleTiles()
        {
            int n = 0;
            foreach (WallpaperTile t in tiles)
            {
                if (t.Visible) n++;
            }
            return n;
        }

        private void CreateTile(string path)
        {
            WallpaperTile tile = new WallpaperTile(path, cellW, cellImgH, cellLabelH);
            tile.Margin = new Padding(tileMargin);
            tile.Selected = picked.Contains(Normalize(path));
            tile.Click += delegate { OnTileClick(tile); };
            toolTip.SetToolTip(tile, path);
            grid.Controls.Add(tile);
            tiles.Add(tile);
            QueueThumbnail(tile);
        }

        private void OnTileClick(WallpaperTile tile)
        {
            string norm = Normalize(tile.FilePath);
            if (tile.Selected) picked.Add(norm);
            else picked.Remove(norm);
            dirty = true;
            UpdateCountText();
        }

        private void QueueThumbnail(WallpaperTile tile)
        {
            string path = tile.FilePath;
            int w = cellW;
            int h = cellImgH;
            Task.Run(async delegate
            {
                Bitmap bmp = null;
                await thumbGate.WaitAsync();
                try
                {
                    bmp = await Task.Run(delegate { return MakeThumb(path, w, h); });
                }
                catch { }
                finally
                {
                    thumbGate.Release();
                }
                if (bmp == null) return;
                SafeUi(delegate
                {
                    if (!closing && !tile.IsDisposed) tile.SetThumbnail(bmp);
                    else bmp.Dispose();
                });
            });
        }

        // Decode once and draw a centered cover-crop at the tile's aspect so
        // the grid stays uniform regardless of each file's native shape.
        // Cover means: scale the image to fill the whole tile and crop the
        // overflow evenly on both sides, like the Windows "Fill" style --
        // never stretch the source to the tile's aspect ratio.
        private static Bitmap MakeThumb(string path, int w, int h)
        {
            if (w < 8 || h < 8) return null;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (Image src = Image.FromStream(fs))
                {
                    if (src.Width < 8 || src.Height < 8) return null;
                    // Uniform scale that makes the image cover the tile
                    // (at least one dimension matches the destination).
                    float scale = Math.Max((float)w / src.Width, (float)h / src.Height);
                    // The part of the source visible inside the tile keeps
                    // the tile's aspect, so no distortion can happen.
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

        private enum BulkKind { All, None, Invert }

        // Bulk actions run on the data level (whole filtered file list), so
        // they are exact even for images whose tiles are not materialized yet;
        // existing tiles are only refreshed afterwards for the visual state.
        private void BulkToggle(BulkKind kind)
        {
            if (!scanFinished || scanned.Count == 0) return;
            string f = currentFilter();
            bool hasFilter = f.Length > 0;
            List<string> targets = new List<string>();
            foreach (string p in scanned)
            {
                if (!hasFilter || MatchesFilter(p)) targets.Add(p);
            }
            if (targets.Count == 0) return;

            dirty = true;
            foreach (string p in targets)
            {
                string norm = Normalize(p);
                if (kind == BulkKind.All)
                {
                    if (!picked.Contains(norm)) { picked.Add(norm); }
                }
                else if (kind == BulkKind.None)
                {
                    picked.Remove(norm);
                }
                else
                {
                    if (!picked.Remove(norm)) picked.Add(norm);
                }
            }
            foreach (WallpaperTile t in tiles)
            {
                bool want;
                if (kind == BulkKind.All) want = true;
                else if (kind == BulkKind.None) want = false;
                else want = !t.Selected;
                if (t.Selected != want) t.Selected = want;
            }
            UpdateCountText();
        }

        private string currentFilter()
        {
            return txtFilter == null ? "" : txtFilter.Text.Trim();
        }

        private bool MatchesFilter(string path)
        {
            string f = currentFilter();
            if (f.Length == 0) return true;
            return Path.GetFileName(path).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyFilter()
        {
            foreach (WallpaperTile t in tiles)
            {
                t.Visible = MatchesFilter(t.FilePath);
            }
        }

        private void UpdatePlaceholder()
        {
            if (lblPlaceholder == null || txtFilter == null) return;
            lblPlaceholder.Visible = txtFilter.Text.Trim().Length == 0 && !txtFilter.Focused;
        }

        private void UpdateCountText()
        {
            if (lblCount == null) return;
            int n = 0;
            foreach (string p in scanned)
            {
                if (picked.Contains(Normalize(p))) n++;
            }
            lblCount.Text = "已选 " + n + " / 共 " + scanned.Count;
        }

        private void ShowGridInfo(string text)
        {
            lblGridInfo.Text = text;
            lblGridInfo.Visible = text.Length > 0;
        }

        private void Save()
        {
            int pickedCount = CountPicked();
            if (chkMaster.Checked && pickedCount == 0)
            {
                MessageBox.Show(this,
                    "已开启总开关，请至少勾选一张壁纸（否则没有任何图片可切换）。\n\n" +
                    "也可以先取消勾选总开关，仅保存勾选集合，选好后再回来开启。",
                    "手动壁纸选择", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Config.ManualSelectionEnabled = chkMaster.Checked;
            List<string> picks = new List<string>();
            foreach (string p in scanned)
            {
                if (picked.Contains(Normalize(p))) picks.Add(p);
            }
            Config.ManualPicked = picks;
            Config.Save();
            dirty = false;
            savedMessage = chkMaster.Checked
                ? "已保存：手动壁纸选择已启用，切换范围为已勾选的 " + picks.Count + " 张壁纸"
                : "已保存：手动壁纸选择已关闭（勾选集合已保留，" + picks.Count + " 张）";
            UpdateHint();
        }

        private int CountPicked()
        {
            int n = 0;
            foreach (string p in scanned)
            {
                if (picked.Contains(Normalize(p))) n++;
            }
            return n;
        }

        private void RequestClose()
        {
            if (dirty)
            {
                DialogResult r = MessageBox.Show(this,
                    "有未保存的勾选更改，关闭前要保存吗？",
                    "手动壁纸选择", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes)
                {
                    Save();
                    if (dirty) return;   // Save was blocked (master on, nothing picked)
                }
            }
            closing = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !closing)
            {
                e.Cancel = true;
                RequestClose();
                return;
            }
            base.OnFormClosing(e);
        }

        private void UpdateHint()
        {
            if (lblBottomHint == null) return;
            lblBottomHint.Text = savedMessage != null
                ? savedMessage
                : "未勾选的壁纸不参与自动 / 手动切换；随机顺序开关不受影响，仍在勾选池内打乱";
        }

        private static string Normalize(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        private void SafeUi(Action a)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired) Invoke(a);
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
                closing = true;
                if (thumbGate != null) thumbGate.Dispose();
                if (toolTip != null) toolTip.Dispose();
                foreach (WallpaperTile t in tiles)
                {
                    try { t.Dispose(); }
                    catch { }
                }
                tiles.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
