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
        private Button btnAll;
        private Button btnNone;
        private Button btnInvert;
        private Label lblSource;
        private ComboBox cmbSource;
        private TextBox txtFilter;
        private Label lblPlaceholder;
        private Label lblCount;
        private PickerCanvas canvas;
        private Label lblGridInfo;
        private Label lblBottomHint;
        private Button btnDisableAll;
        private Button btnSave;
        private ToolTip toolTip;

        private readonly List<string> allPaths = new List<string>();
        private readonly HashSet<string> picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Per-source split of the scan so the source filter can show a single
        // source at a time. ownerOf maps a normalized path to the index of the
        // source that contributed it (first source wins, exactly like the
        // dedupe in ImageScanner.ScanMany).
        private readonly List<string> sourceFolders = new List<string>();
        private readonly Dictionary<string, int> ownerOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int[] sourceCounts = new int[0];
        private SrcFilter srcMode = SrcFilter.All;
        private int srcIndex = -1;
        private bool comboUpdating;

        private bool scanFinished;
        private bool closing;
        private bool dirty;
        private string savedMessage;
        private float sf = 1f;

        private enum SrcFilter { All, Picked, One }

        public ManualPickerForm(Form owner)
        {
            ownerForm = owner;
            Text = Loc.F("picker.title", Application.ProductVersion);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;   // centered on the owner's screen in OnLoad
            // Same DPI rule as MainForm: the design basis (96 DPI) must be
            // assigned AFTER AutoScaleMode, because that setter resets
            // AutoScaleDimensions to the current device DPI. Otherwise the
            // window is laid out against a wrong basis and everything shrinks
            // or clips once it lands on a monitor with a different scaling.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = SystemColors.Control;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildChrome();
            CancelButton = btnSave;
        }

        private void BuildChrome()
        {
            // Source filter. Hidden by default: it only earns its row when the
            // picker draws from more than one source.
            lblSource = new Label();
            lblSource.Text = Loc.T("picker.src.label");
            lblSource.AutoSize = true;
            lblSource.TextAlign = ContentAlignment.MiddleLeft;
            lblSource.SetBounds(14, 16, 60, 22);
            lblSource.Visible = false;
            Controls.Add(lblSource);

            cmbSource = new ComboBox();
            cmbSource.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSource.SetBounds(80, 12, 260, 26);
            // Cap the drop-down at roughly eight rows; beyond that the list
            // gets its own scrollbar instead of running down the whole screen.
            cmbSource.DropDownHeight = 26 * 8;
            cmbSource.IntegralHeight = false;
            cmbSource.MaxDropDownItems = 8;
            cmbSource.Visible = false;
            cmbSource.SelectedIndexChanged += delegate { OnSourceFilterChanged(); };
            Controls.Add(cmbSource);

            btnAll = new Button();
            btnAll.Text = Loc.T("picker.all");
            btnAll.SetBounds(0, 12, 66, 28);
            btnAll.Click += delegate { BulkToggle(BulkKind.All); };
            Controls.Add(btnAll);

            btnNone = new Button();
            btnNone.Text = Loc.T("picker.none");
            btnNone.SetBounds(0, 12, 66, 28);
            btnNone.Click += delegate { BulkToggle(BulkKind.None); };
            Controls.Add(btnNone);

            btnInvert = new Button();
            btnInvert.Text = Loc.T("picker.invert");
            btnInvert.SetBounds(0, 12, 66, 28);
            btnInvert.Click += delegate { BulkToggle(BulkKind.Invert); };
            Controls.Add(btnInvert);

            txtFilter = new TextBox();
            txtFilter.SetBounds(0, 12, 300, 28);
            txtFilter.TextChanged += delegate { OnFilterChanged(); };
            txtFilter.Enter += delegate { UpdatePlaceholder(); };
            txtFilter.Leave += delegate { UpdatePlaceholder(); };
            Controls.Add(txtFilter);

            lblPlaceholder = new Label();
            lblPlaceholder.Text = Loc.T("picker.filter.hint");
            lblPlaceholder.ForeColor = Color.Gray;
            lblPlaceholder.AutoSize = false;
            lblPlaceholder.SetBounds(0, 15, 280, 22);
            lblPlaceholder.Click += delegate { txtFilter.Focus(); };
            Controls.Add(lblPlaceholder);

            lblCount = new Label();
            lblCount.Text = Loc.F("picker.count", 0, 0);
            lblCount.AutoSize = false;
            lblCount.TextAlign = ContentAlignment.MiddleRight;
            lblCount.ForeColor = Accent;
            lblCount.SetBounds(0, 12, 150, 26);
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

            // Save and close in one step: normal flow is "curate, then leave".
            btnSave = new Button();
            btnSave.Text = Loc.T("picker.saveclose");
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.BackColor = Accent;
            btnSave.ForeColor = Color.White;
            btnSave.SetBounds(0, 0, 108, 34);
            btnSave.Click += delegate { SaveAndClose(); };
            Controls.Add(btnSave);

            // Escape hatch: drop every check and leave manual mode entirely.
            btnDisableAll = new Button();
            btnDisableAll.Text = Loc.T("picker.disableall");
            btnDisableAll.SetBounds(0, 0, 150, 34);
            btnDisableAll.Click += delegate { DisableManualSelection(); };
            Controls.Add(btnDisableAll);

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 6000;
            toolTip.InitialDelay = 400;
            toolTip.ReshowDelay = 200;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            sf = DeviceDpi / 96f;

            canvas.ScaleFactor = sf;

            // The picker grid is fixed at 7 columns of 258x145 logical px tiles.
            // Minimum client width must fit the grid content plus margins and a
            // vertical scrollbar; add a small frame allowance.
            int frameAllowance = (int)(38 * sf);
            int gridContentW = canvas.Spacing + PickerCanvas.DesignCols * (canvas.CellWidth + canvas.Spacing);
            int minClientW = (int)(28 * sf) + gridContentW + (int)(17 * sf) + frameAllowance;
            int minW = minClientW;
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

            LayoutChrome();
            UpdateHint();
            StartScan();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (canvas != null && !closing) LayoutChrome();
        }

        // Width of the widest entry in the source combo, so the control sizes
        // to its content instead of stretching across the window.
        private int MeasureComboWidth()
        {
            if (cmbSource == null) return 200;
            int widest = 0;
            using (Graphics g = cmbSource.CreateGraphics())
            {
                foreach (object item in cmbSource.Items)
                {
                    string s = item as string;
                    if (string.IsNullOrEmpty(s)) continue;
                    int w = TextRenderer.MeasureText(g, s, cmbSource.Font).Width;
                    if (w > widest) widest = w;
                }
            }
            // Room for the drop-down arrow, the border and the scrollbar.
            return widest + 48;
        }

        private void LayoutChrome()
        {
            if (sf < 0.5f) sf = DeviceDpi / 96f;
            int right = ClientSize.Width - (int)(14 * sf);
            int gap = (int)(10 * sf);

            // The source filter owns a row of its own above the button row.
            // It must never share a line with the buttons: a long source name
            // used to stretch the combo across the row and push the bulk
            // buttons off the left edge. The combo also stays deliberately
            // narrow - it is a filter, not a headline - so its width is capped
            // well below the window width no matter how long the names are.
            int left = (int)(14 * sf);
            bool srcRow = cmbSource != null && cmbSource.Visible;
            int rowTop = srcRow ? (int)(48 * sf) : (int)(12 * sf);
            if (srcRow)
            {
                lblSource.Location = new Point(left, (int)(14 * sf) + (int)(4 * sf));
                cmbSource.Location = new Point(lblSource.Right + (int)(6 * sf), (int)(14 * sf));
                int want = MeasureComboWidth();
                int roomy = Math.Max((int)(160 * sf), right - cmbSource.Left);
                cmbSource.Width = Math.Max((int)(160 * sf),
                    Math.Min(Math.Min(want, (int)(240 * sf)), roomy));
                cmbSource.DropDownWidth = cmbSource.Width;
            }

            // Right-aligned: count label, then the filter box, then the three
            // bulk buttons marching left. Every button keeps its natural width
            // because the filter box yields the slack instead.
            lblCount.Location = new Point(right - lblCount.Width, rowTop + (int)(3 * sf));
            int btnBudget = btnAll.Width + btnNone.Width + btnInvert.Width + (int)(12 * sf);
            int filterMin = (int)(150 * sf);
            int filterW = Math.Max(filterMin, (int)(280 * sf));
            int fixedW = btnBudget + filterW + gap * 2;
            int avail = (lblCount.Left - (int)(10 * sf)) - left;
            if (fixedW > avail)
            {
                filterW = Math.Max((int)(90 * sf), avail - btnBudget - gap * 2);
            }
            txtFilter.Width = filterW;
            txtFilter.Location = new Point(right - lblCount.Width - gap - txtFilter.Width, rowTop);
            lblPlaceholder.Location = new Point(txtFilter.Left + 6, txtFilter.Top + 4);
            lblPlaceholder.Width = txtFilter.Width - 12;
            btnInvert.Location = new Point(txtFilter.Left - gap - btnInvert.Width, rowTop);
            btnNone.Location = new Point(btnInvert.Left - (int)(6 * sf) - btnNone.Width, rowTop);
            btnAll.Location = new Point(btnNone.Left - (int)(6 * sf) - btnAll.Width, rowTop);
            if (btnAll.Left < left)
            {
                btnAll.Left = left;
                btnNone.Left = btnAll.Right + (int)(6 * sf);
                btnInvert.Left = btnNone.Right + (int)(6 * sf);
                txtFilter.Left = btnInvert.Right + gap;
                txtFilter.Width = Math.Max((int)(90 * sf), lblCount.Left - gap - txtFilter.Left);
            }

            int top = Math.Max(btnAll.Bottom, txtFilter.Bottom) + (int)(10 * sf);
            int bottomBarH = (int)(54 * sf);
            canvas.SetBounds((int)(14 * sf), top,
                ClientSize.Width - (int)(28 * sf),
                Math.Max(120, ClientSize.Height - top - bottomBarH));

            lblGridInfo.SetBounds(canvas.Left + (int)(30 * sf), canvas.Top + (int)(26 * sf),
                canvas.Width - (int)(60 * sf), (int)(60 * sf));
            lblGridInfo.Font = Font;

            // Bottom bar, right to left: 保存并关闭, then 彻底关闭手动选择壁纸
            // to its left. The hint label only gets the leftover space.
            int barTop = ClientSize.Height - btnSave.Height - (int)(10 * sf);
            btnSave.Location = new Point(right - btnSave.Width, barTop);
            btnDisableAll.Location = new Point(btnSave.Left - btnDisableAll.Width - (int)(10 * sf), barTop);
            if (btnDisableAll.Left < (int)(14 * sf))
                btnDisableAll.Left = (int)(14 * sf);
            lblBottomHint.SetBounds((int)(16 * sf), btnSave.Top + (int)(4 * sf),
                Math.Max(100, btnDisableAll.Left - (int)(16 * sf) - (int)(10 * sf)), 22);
        }

        private void StartScan()
        {
            // Only enabled sources: a source turned off in the manager has no
            // wallpapers to offer here either.
            List<string> folders = Config.EnabledFolders();
            bool recursive = Config.Recursive;
            ShowGridInfo(Loc.T("picker.scanning"));
            Task.Run(delegate
            {
                // Scan each source on its own so the source filter can show one
                // of them later, then merge exactly the way ScanMany would:
                // first source wins a duplicate, natural-sorted result.
                List<string> merged = new List<string>();
                Dictionary<string, int> owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < folders.Count; i++)
                {
                    foreach (string p in ImageScanner.Scan(folders[i], recursive))
                    {
                        string norm = Normalize(p);
                        if (seen.Add(norm))
                        {
                            merged.Add(p);
                            owner[norm] = i;
                        }
                    }
                }
                merged.Sort(delegate (string a, string b) { return ImageScanner.NaturalCompare(a, b); });
                SafeUi(delegate { OnScanDone(merged, folders, owner); });
            });
        }

        private void OnScanDone(List<string> found, List<string> folders, Dictionary<string, int> owner)
        {
            allPaths.Clear();
            allPaths.AddRange(found);

            sourceFolders.Clear();
            sourceFolders.AddRange(folders);
            ownerOf.Clear();
            foreach (KeyValuePair<string, int> kv in owner) ownerOf[kv.Key] = kv.Value;
            sourceCounts = new int[sourceFolders.Count];
            foreach (string p in allPaths)
            {
                int i;
                if (ownerOf.TryGetValue(Normalize(p), out i) && i >= 0 && i < sourceCounts.Length)
                    sourceCounts[i]++;
            }

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
            BuildSourceCombo();
            LayoutChrome();
            UpdateCountText();
            if (allPaths.Count == 0)
            {
                ShowGridInfo(Loc.T("picker.nofolders"));
                return;
            }
            RefreshView();
        }

        // Push the current view (source filter + name filter) into the canvas
        // and explain an empty result.
        private void RefreshView()
        {
            List<string> display = FilteredPaths();
            HashSet<int> displayPicked = new HashSet<int>();
            for (int i = 0; i < display.Count; i++)
            {
                if (picked.Contains(Normalize(display[i]))) displayPicked.Add(i);
            }
            canvas.SetWallpapers(display, displayPicked);

            string msg = "";
            if (display.Count == 0)
            {
                if (srcMode == SrcFilter.Picked) msg = Loc.T("picker.nosrcpick");
                else if (currentFilter().Length > 0) msg = Loc.T("picker.nofiltermatch");
            }
            ShowGridInfo(msg);
        }

        private bool PassesSourceFilter(string path)
        {
            if (srcMode == SrcFilter.All) return true;
            string norm = Normalize(path);
            if (srcMode == SrcFilter.Picked) return picked.Contains(norm);
            int i;
            return ownerOf.TryGetValue(norm, out i) && i == srcIndex;
        }

        private List<string> FilteredPaths()
        {
            string f = currentFilter();
            List<string> r = new List<string>();
            foreach (string p in allPaths)
            {
                if (!PassesSourceFilter(p)) continue;
                if (f.Length > 0 &&
                    Path.GetFileName(p).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
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
            RefreshView();
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

        private int PickedCount()
        {
            int n = 0;
            foreach (string p in allPaths)
            {
                if (picked.Contains(Normalize(p))) n++;
            }
            return n;
        }

        private void UpdateCountText()
        {
            if (lblCount == null) return;
            lblCount.Text = Loc.F("picker.count", PickedCount(), allPaths.Count);
            UpdatePickedComboLabel();
        }

        // Fill the source combo. It only appears when the picker draws from
        // more than one source; with a single source there is nothing to
        // narrow down. A returning user starts on the checked view, so the
        // wallpapers picked last time are right there no matter which source
        // they came from; a first run starts on "all wallpapers".
        private void BuildSourceCombo()
        {
            if (cmbSource == null) return;
            bool multi = sourceFolders.Count >= 2;
            if (lblSource != null) lblSource.Visible = multi;
            cmbSource.Visible = multi;
            srcMode = SrcFilter.All;
            srcIndex = -1;
            if (!multi) return;

            comboUpdating = true;
            try
            {
                cmbSource.Items.Clear();
                cmbSource.Items.Add(Loc.T("picker.src.all"));
                cmbSource.Items.Add(Loc.F("picker.src.picked", PickedCount()));
                for (int i = 0; i < sourceFolders.Count; i++)
                {
                    cmbSource.Items.Add(Loc.F("picker.src.one",
                        ShortSourceName(sourceFolders[i]), sourceCounts[i]));
                }
                int def = PickedCount() > 0 ? 1 : 0;
                cmbSource.SelectedIndex = def;
                if (def == 1) srcMode = SrcFilter.Picked;
            }
            finally
            {
                comboUpdating = false;
            }
        }

        // Folder names are often far too long to show in full. Keep the head,
        // which is the part that usually tells sources apart, and elide the
        // rest so the drop-down stays readable in a narrow control.
        private static string ShortSourceName(string folder)
        {
            string name = SourceNames.Display(folder);
            const int Max = 16;
            if (name.Length <= Max) return name;
            return name.Substring(0, Max - 1) + "\u2026";
        }

        // Keep the "checked wallpapers (n)" entry in step with the live count
        // without letting the update look like a user selection.
        private void UpdatePickedComboLabel()
        {
            if (cmbSource == null || !cmbSource.Visible || cmbSource.Items.Count < 2) return;
            string text = Loc.F("picker.src.picked", PickedCount());
            if (string.Equals(cmbSource.Items[1] as string, text, StringComparison.Ordinal)) return;
            comboUpdating = true;
            try { cmbSource.Items[1] = text; }
            finally { comboUpdating = false; }
        }

        private void OnSourceFilterChanged()
        {
            if (comboUpdating || !scanFinished || cmbSource == null) return;
            int i = cmbSource.SelectedIndex;
            if (i == 0) { srcMode = SrcFilter.All; srcIndex = -1; }
            else if (i == 1) { srcMode = SrcFilter.Picked; srcIndex = -1; }
            else { srcMode = SrcFilter.One; srcIndex = i - 2; }
            RefreshView();
        }

        private void ShowGridInfo(string text)
        {
            lblGridInfo.Text = text;
            lblGridInfo.Visible = text.Length > 0;
        }

        private void Save()
        {
            List<string> picks = new List<string>();
            foreach (string p in allPaths)
            {
                if (picked.Contains(Normalize(p))) picks.Add(p);
            }
            Config.ManualPicked = picks;
            Config.Save();
            dirty = false;
            // The checked set alone drives the mode: non-empty = manual on,
            // empty = manual off. No master switch, no enable prompt.
            savedMessage = picks.Count > 0
                ? Loc.F("picker.saved.on", picks.Count)
                : Loc.T("picker.saved.off");
            UpdateHint();
        }

        // The 保存并关闭 button: persist the checked set, then leave. No extra
        // "unsaved changes" prompt, since the user just asked to save.
        private void SaveAndClose()
        {
            Save();
            closing = true;
            Close();
        }

        // 彻底关闭手动选择壁纸: drop every check, persist immediately, confirm,
        // then close both this window and the confirmation box.
        private void DisableManualSelection()
        {
            DialogResult r = MessageBox.Show(this,
                Loc.T("picker.disableall.confirm"),
                Loc.T("picker.caption"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;

            ClearAndSave();
            closing = true;
            Close();
        }

        // The state change behind 彻底关闭手动选择壁纸, split out so tests can
        // exercise it without a modal confirmation box.
        private void ClearAndSaveForTest() { ClearAndSave(); }

        private void ClearAndSave()
        {
            picked.Clear();
            if (canvas != null) canvas.ClearSelection();
            Save();                 // Config.ManualPicked = empty + write to disk
            savedMessage = Loc.T("picker.saved.off");
        }

        // Ask once per close attempt whether unsaved picks should be saved.
        // Returns false only when the user picked Cancel.
        private bool ConfirmCloseAllowed()
        {
            if (closing || !dirty) return true;
            DialogResult r = MessageBox.Show(this,
                Loc.T("picker.confirm.close"),
                Loc.T("picker.caption"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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
                : Loc.T("picker.bottom.hint");
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