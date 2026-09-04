using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Manual wallpaper picker. Opens centered at ~3/4 of the working area on
    // the screen that owns the main window and can be maximized. The master
    // switch at the top-left is the gate: while off, the checked set below is
    // saved but does not restrict switching. The middle is a virtualized
    // 16:9 thumbnail grid (PickerCanvas): it only renders the visible
    // viewport, so opening, scrolling, resizing and maximizing stay smooth
    // regardless of library size. This form is the data layer -- the canvas
    // raises TileToggled and the form keeps Config + ini state in sync.
    public class ManualPickerForm : Form
    {
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
        private PickerCanvas canvas;
        private Label lblGridInfo;
        private Label lblBottomHint;
        private Button btnClose;
        private Button btnSave;
        private ToolTip toolTip;

        private readonly List<string> allPaths = new List<string>();
        private readonly HashSet<string> picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            canvas = new PickerCanvas();
            canvas.TileToggled += OnCanvasTileToggled;
            canvas.SetBounds(14, 92, 400, 400);
            Controls.Add(canvas);

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
            if (canvas != null && !closing) LayoutChrome();
        }

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
            canvas.SetBounds((int)(14 * sf), top,
                ClientSize.Width - (int)(28 * sf),
                Math.Max(120, ClientSize.Height - top - bottomBarH));

            lblGridInfo.SetBounds(canvas.Left + (int)(30 * sf), canvas.Top + (int)(26 * sf),
                canvas.Width - (int)(60 * sf), (int)(60 * sf));
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
            allPaths.Clear();
            allPaths.AddRange(found);

            picked.Clear();
            HashSet<string> saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in Config.ManualPicked)
            {
                try { saved.Add(Normalize(p)); }
                catch { }
            }
            foreach (string p in allPaths)
            {
                if (saved.Contains(Normalize(p))) picked.Add(Normalize(p));
            }

            scanFinished = true;
            UpdateCountText();
            if (allPaths.Count == 0)
            {
                ShowGridInfo("没有找到可用壁纸，请先在主窗口的壁纸源里添加图片文件夹");
                return;
            }
            ShowGridInfo("");
            RefreshCanvas();
        }

        // Push the current filter subset + picked set into the canvas.
        private void RefreshCanvas()
        {
            List<string> display = FilteredPaths();
            HashSet<int> displayPicked = new HashSet<int>();
            for (int i = 0; i < display.Count; i++)
            {
                if (picked.Contains(Normalize(display[i]))) displayPicked.Add(i);
            }
            canvas.SetWallpapers(display, displayPicked);
        }

        private List<string> FilteredPaths()
        {
            string f = currentFilter();
            if (f.Length == 0) return new List<string>(allPaths);
            List<string> r = new List<string>();
            foreach (string p in allPaths)
            {
                if (Path.GetFileName(p).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    r.Add(p);
            }
            return r;
        }

        private void OnCanvasTileToggled(int idx, bool nowPicked)
        {
            if (canvas == null || idx >= canvas.ItemCount) return;
            string path = canvas.ItemAt(idx);
            string norm = Normalize(path);
            if (nowPicked) picked.Add(norm);
            else picked.Remove(norm);
            dirty = true;
            UpdateCountText();
        }

        private void OnFilterChanged()
        {
            UpdatePlaceholder();
            if (!scanFinished) return;
            List<string> display = FilteredPaths();
            RefreshCanvas();
            ShowGridInfo(currentFilter().Length > 0 && display.Count == 0
                ? "没有匹配的文件名" : "");
        }

        private enum BulkKind { All, None, Invert }

        // Bulk actions run on the data level (whole filtered file list).
        private void BulkToggle(BulkKind kind)
        {
            if (!scanFinished || allPaths.Count == 0) return;
            List<string> targets = FilteredPaths();
            if (targets.Count == 0) return;
            dirty = true;
            foreach (string p in targets)
            {
                string norm = Normalize(p);
                bool nowPicked;
                if (kind == BulkKind.All) nowPicked = true;
                else if (kind == BulkKind.None) nowPicked = false;
                else nowPicked = !picked.Contains(norm);
                if (nowPicked) picked.Add(norm);
                else picked.Remove(norm);
            }
            // Push the new picked bits into the canvas for repaint.
            HashSet<int> canvasPicked = new HashSet<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (picked.Contains(Normalize(targets[i]))) canvasPicked.Add(i);
            }
            canvas.ApplyPicked(canvasPicked);
            UpdateCountText();
        }

        private string currentFilter()
        {
            return txtFilter == null ? "" : txtFilter.Text.Trim();
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
            foreach (string p in allPaths)
            {
                if (picked.Contains(Normalize(p))) n++;
            }
            lblCount.Text = "已选 " + n + " / 共 " + allPaths.Count;
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
                // Master on with no picks leaves no pool to switch from. The
                // old modal loop made every close attempt re-popup; instead,
                // turn the master off automatically and save the (empty)
                // selection so the close path always succeeds in one click.
                chkMaster.Checked = false;
            }

            Config.ManualSelectionEnabled = chkMaster.Checked;
            List<string> picks = new List<string>();
            foreach (string p in allPaths)
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
            foreach (string p in allPaths)
            {
                if (picked.Contains(Normalize(p))) n++;
            }
            return n;
        }

        // The 关闭 button. This runs from a Click handler, so calling Close()
        // here is a top-level call (NOT inside OnFormClosing) and is safe.
        private void RequestClose()
        {
            if (!ConfirmCloseAllowed()) return;
            closing = true;
            Close();
        }

        // Ask once per close attempt whether unsaved picks should be saved.
        // Returns false only when the user picked Cancel.
        private bool ConfirmCloseAllowed()
        {
            if (closing || !dirty) return true;
            DialogResult r = MessageBox.Show(this,
                "有未保存的勾选更改，关闭前要保存吗？",
                "手动壁纸选择", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) Save();
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Title-bar X / Alt+F4 arrive here with CloseReason.UserClosing.
            // Ask about unsaved picks, then LET THE ORIGINAL CLOSE FINISH.
            // The previous code cancelled this close and called Close() again
            // from inside the handler; on a modal (ShowDialog) form WinForms
            // swallows that nested close, so the first X click did nothing
            // and a second one was required to actually leave.
            if (e.CloseReason == CloseReason.UserClosing && !ConfirmCloseAllowed())
            {
                e.Cancel = true;
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
                if (toolTip != null) toolTip.Dispose();
                if (canvas != null) canvas.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}