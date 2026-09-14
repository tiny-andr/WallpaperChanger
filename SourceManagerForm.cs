using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Wallpaper source manager: add / remove sources and turn an individual
    // source on or off. A disabled source contributes nothing to rotation but
    // keeps its slot in the list (and its checked wallpapers), so re-enabling
    // is one click instead of re-adding the folder.
    //
    // Every row also reports how many wallpapers the source holds. The count
    // uses the very same scan the rotator uses (recursive, wallpaper formats
    // only, hidden/system files and broken images skipped), so it reflects
    // what actually participates -- an empty or missing source is obvious at
    // a glance. Counting runs on a background thread; a source whose folder is
    // gone shows as unavailable instead of 0.
    //
    // The dialog edits its own copies and only writes to Config when the user
    // saves, so the main window never sees a half-applied source list.
    public class SourceManagerForm : Form
    {
        private readonly Form ownerForm;
        private ListView lv;
        private ColumnHeader colState;
        private ColumnHeader colName;
        private ColumnHeader colPath;
        private ColumnHeader colCount;
        private Button btnAdd;
        private Button btnRemove;
        private Button btnAllOn;
        private Button btnAllOff;
        private Button btnRefresh;
        private Button btnSave;
        private Button btnClose;
        private Label lblHint;
        private ToolTip toolTip;

        private readonly List<string> folders = new List<string>();
        private readonly HashSet<string> disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> counting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pristine snapshot taken at construction and after every save. The
        // close prompt compares against it instead of trusting an event-driven
        // dirty bit: the list view echoes its initial check states back through
        // ItemChecked once its handle exists, which used to make a dialog the
        // user never touched look modified.
        private readonly List<string> pristineFolders = new List<string>();
        private readonly HashSet<string> pristineDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool loading;      // suppress ItemChecked while rows are rebuilt
        private bool closing;
        private bool sfReady;
        private float sf = 1f;
        private string flash;      // one-line note under the list (saved / duplicate / ...)

        // True once the dialog has written the source list back to Config.
        // The main window uses it to refresh its summary and restart rotation.
        public bool Changed { get; private set; }

        public SourceManagerForm(Form owner)
        {
            ownerForm = owner;
            Text = Loc.F("src.title", Application.ProductVersion);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            // Same DPI rule as every other window here: the design basis (96
            // DPI) must be assigned AFTER AutoScaleMode, because that setter
            // resets AutoScaleDimensions to the current device DPI.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(660, 470);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = SystemColors.Control;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            // Work on copies: nothing reaches Config until the user saves.
            folders.AddRange(Config.Folders);
            foreach (string d in Config.DisabledFolders)
            {
                if (d != null) disabled.Add(d.Trim());
            }
            SnapshotPristine();

            BuildChrome();
            RebuildList();
            CancelButton = btnClose;
            Theme.ApplyTo(this);
        }

        private void BuildChrome()
        {
            lv = new ListView();
            lv.View = View.Details;
            lv.CheckBoxes = true;
            lv.FullRowSelect = true;
            lv.MultiSelect = true;
            lv.HideSelection = false;
            lv.LabelEdit = false;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.SetBounds(14, 14, 632, 296);
            lv.ItemChecked += OnItemChecked;
            Controls.Add(lv);

            colState = new ColumnHeader();
            colState.Text = Loc.T("src.col.state");
            colName = new ColumnHeader();
            colName.Text = Loc.T("src.col.name");
            colPath = new ColumnHeader();
            colPath.Text = Loc.T("src.col.path");
            colCount = new ColumnHeader();
            colCount.Text = Loc.T("src.col.count");
            lv.Columns.Add(colState);
            lv.Columns.Add(colName);
            lv.Columns.Add(colPath);
            lv.Columns.Add(colCount);

            lblHint = new Label();
            lblHint.Text = "";
            lblHint.Tag = Theme.RoleMuted;
            lblHint.SetBounds(14, 316, 632, 44);
            Controls.Add(lblHint);

            btnAdd = new Button();
            btnAdd.Text = Loc.T("main.source.add");
            btnAdd.Click += delegate { BrowseFolder(); };
            Controls.Add(btnAdd);

            btnRemove = new Button();
            btnRemove.Text = Loc.T("main.source.remove");
            btnRemove.Click += delegate { RemoveSelected(); };
            Controls.Add(btnRemove);

            btnAllOn = new Button();
            btnAllOn.Text = Loc.T("src.allon");
            btnAllOn.Click += delegate { SetAll(true); };
            Controls.Add(btnAllOn);

            btnAllOff = new Button();
            btnAllOff.Text = Loc.T("src.alloff");
            btnAllOff.Click += delegate { SetAll(false); };
            Controls.Add(btnAllOff);

            btnRefresh = new Button();
            btnRefresh.Text = Loc.T("src.refresh");
            btnRefresh.Click += delegate { RefreshCounts(); };
            Controls.Add(btnRefresh);

            btnSave = new Button();
            btnSave.Text = Loc.T("picker.save");
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Tag = Theme.RoleAccent;
            btnSave.Click += delegate { Save(); };
            Controls.Add(btnSave);

            btnClose = new Button();
            btnClose.Text = Loc.T("picker.close");
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
            sfReady = true;
            MinimumSize = new Size((int)(620 * sf), (int)(430 * sf));
            LayoutContent();
            StartCounts();
            UpdateHint();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (sfReady && !closing) LayoutContent();
        }

        private void LayoutContent()
        {
            if (!sfReady) return;
            int m = (int)(14 * sf);
            int gap = (int)(8 * sf);
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int content = w - 2 * m;

            int stateW = (int)(74 * sf);
            int nameW = (int)(150 * sf);
            int countW = (int)(104 * sf);
            int pathW = Math.Max((int)(150 * sf), content - stateW - nameW - countW - (int)(24 * sf));
            colState.Width = stateW;
            colName.Width = nameW;
            colPath.Width = pathW;
            colCount.Width = countW;

            int saveH = (int)(34 * sf);
            int rowH = (int)(30 * sf);
            int hintH = (int)(66 * sf);   // summary line + a two-line hint
            int bottom = h - m - saveH;
            int btnRowY = bottom - gap - rowH;
            int hintY = btnRowY - gap - hintH;
            int lvH = Math.Max((int)(120 * sf), hintY - gap - m);

            lv.SetBounds(m, m, content, lvH);
            lblHint.SetBounds(m, hintY, content, hintH);

            int x = m;
            btnAdd.SetBounds(x, btnRowY, (int)(96 * sf), rowH);
            x = btnAdd.Right + gap;
            btnRemove.SetBounds(x, btnRowY, (int)(110 * sf), rowH);
            x = btnRemove.Right + gap;
            btnAllOn.SetBounds(x, btnRowY, (int)(110 * sf), rowH);
            x = btnAllOn.Right + gap;
            btnAllOff.SetBounds(x, btnRowY, (int)(110 * sf), rowH);
            x = btnAllOff.Right + gap;
            btnRefresh.SetBounds(x, btnRowY, (int)(110 * sf), rowH);

            int bw = (int)(96 * sf);
            btnClose.SetBounds(w - m - bw, bottom, bw, saveH);
            btnSave.SetBounds(btnClose.Left - gap - bw, bottom, bw, saveH);
        }

        // ---- list rendering -------------------------------------------------

        private static string SourceName(string folder)
        {
            return SourceNames.Display(folder);
        }

        private string CountText(string folder)
        {
            if (counting.Contains(folder)) return Loc.T("src.count.pending");
            int n;
            if (counts.TryGetValue(folder, out n))
                return n < 0 ? Loc.T("src.count.unavailable") : n.ToString();
            return Loc.T("src.count.pending");
        }

        private void RebuildList()
        {
            loading = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (string f in folders)
            {
                ListViewItem it = new ListViewItem(Loc.T("src.state.on"));
                it.SubItems.Add(SourceName(f));
                it.SubItems.Add(f);
                it.SubItems.Add(CountText(f));
                it.Tag = f;
                it.Checked = !disabled.Contains(f);
                lv.Items.Add(it);
                RefreshRow(it);
            }
            lv.EndUpdate();
            loading = false;
            UpdateHint();
        }

        // Repaint one row's state text + colour from the current enabled bit.
        private void RefreshRow(ListViewItem it)
        {
            if (it == null) return;
            string folder = it.Tag as string;
            if (folder == null) return;
            bool on = it.Checked;
            it.SubItems[0].Text = on ? Loc.T("src.state.on") : Loc.T("src.state.off");
            it.SubItems[3].Text = CountText(folder);

            int n = -1;
            bool known = counts.TryGetValue(folder, out n);
            Color row;
            if (!on) row = Theme.ForeMuted;
            else if (known && n < 0) row = Theme.Warn;
            else row = Theme.Fore;
            it.ForeColor = row;
        }

        private void UpdateHint()
        {
            if (lblHint == null) return;
            int total = folders.Count;
            int off = 0;
            long sum = 0;
            bool pending = false;
            foreach (string f in folders)
            {
                bool on = !disabled.Contains(f);
                if (!on) off++;
                int n;
                if (counting.Contains(f) || !counts.ContainsKey(f)) { pending = true; continue; }
                if (counts.TryGetValue(f, out n) && n >= 0 && on) sum += n;
            }
            string summary;
            if (total == 0)
                summary = Loc.T("src.noSource");
            else
                summary = Loc.F("src.summary", total, total - off, off,
                    pending ? Loc.T("src.count.pending") : sum.ToString());
            lblHint.Text = summary + "\r\n" + (flash != null ? flash : Loc.T("src.hint"));
        }

        // ---- count scanning -------------------------------------------------

        private void StartCounts()
        {
            List<string> targets = new List<string>();
            foreach (string f in folders)
            {
                if (!counts.ContainsKey(f) && !counting.Contains(f))
                {
                    counting.Add(f);
                    targets.Add(f);
                }
            }
            if (targets.Count == 0) return;
            bool recursive = Config.Recursive;
            Task.Run(delegate
            {
                foreach (string f in targets)
                {
                    int n;
                    try
                    {
                        n = Directory.Exists(f) ? ImageScanner.Scan(f, recursive).Count : -1;
                    }
                    catch
                    {
                        n = -1;
                    }
                    int val = n;
                    SafeUi(delegate { OnCountDone(f, val); });
                }
            });
        }

        private void RefreshCounts()
        {
            counts.Clear();
            counting.Clear();
            foreach (ListViewItem it in lv.Items) RefreshRow(it);
            StartCounts();
            UpdateHint();
        }

        private void OnCountDone(string folder, int n)
        {
            counting.Remove(folder);
            counts[folder] = n;
            foreach (ListViewItem it in lv.Items)
            {
                if (string.Equals(it.Tag as string, folder, StringComparison.OrdinalIgnoreCase))
                {
                    RefreshRow(it);
                    break;
                }
            }
            UpdateHint();
        }

        // ---- user actions ---------------------------------------------------

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (loading) return;
            string folder = e.Item.Tag as string;
            if (folder == null) return;
            // The control echoes every programmatic state change back through
            // this event. Only a click that contradicts the model is a real
            // edit; anything else is noise, notably the initial check states
            // replayed when the list view handle is created.
            if (e.Item.Checked == !disabled.Contains(folder)) return;
            if (e.Item.Checked) disabled.Remove(folder);
            else disabled.Add(folder);
            flash = null;
            RefreshRow(e.Item);
            UpdateHint();
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = Loc.T("dialog.pickfolder");
                string seed = FirstExistingFolder();
                if (seed != null) dlg.SelectedPath = seed;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string folder = dlg.SelectedPath;
                foreach (string f in folders)
                {
                    if (string.Equals(f.Trim(), folder, StringComparison.OrdinalIgnoreCase))
                    {
                        flash = Loc.T("status.folder.dup");
                        UpdateHint();
                        return;
                    }
                }
                folders.Add(folder);
                flash = null;
                RebuildList();
                StartCounts();
            }
        }

        private string FirstExistingFolder()
        {
            foreach (string f in folders)
            {
                if (Directory.Exists(f)) return f;
            }
            return null;
        }

        private void RemoveSelected()
        {
            if (lv.SelectedItems.Count == 0)
            {
                flash = Loc.T("status.folder.pickfirst");
                UpdateHint();
                return;
            }
            List<string> kill = new List<string>();
            foreach (ListViewItem it in lv.SelectedItems)
            {
                string f = it.Tag as string;
                if (f != null) kill.Add(f);
            }
            foreach (string f in kill)
            {
                folders.RemoveAll(delegate (string x)
                {
                    return string.Equals(x.Trim(), f.Trim(), StringComparison.OrdinalIgnoreCase);
                });
                disabled.Remove(f);
                counts.Remove(f);
                counting.Remove(f);
            }
            flash = null;
            RebuildList();
            StartCounts();
        }

        private void SetAll(bool enabled)
        {
            if (lv.Items.Count == 0) return;
            loading = true;
            foreach (ListViewItem it in lv.Items) it.Checked = enabled;
            loading = false;
            foreach (ListViewItem it in lv.Items)
            {
                string f = it.Tag as string;
                if (f == null) continue;
                if (enabled) disabled.Remove(f);
                else disabled.Add(f);
                RefreshRow(it);
            }
            flash = null;
            UpdateHint();
        }

        private void Save()
        {
            Config.Folders = new List<string>(folders);
            List<string> off = new List<string>();
            foreach (string f in folders)
            {
                if (disabled.Contains(f)) off.Add(f);
            }
            Config.DisabledFolders = off;
            Config.Save();
            SnapshotPristine();
            Changed = true;
            flash = Loc.F("src.saved", folders.Count, folders.Count - off.Count, off.Count);
            UpdateHint();
        }

        private void RequestClose()
        {
            if (!ConfirmCloseAllowed()) return;
            closing = true;
            Close();
        }

        // Remember the state that is currently on disk, so a later close can
        // tell whether anything actually needs saving.
        private void SnapshotPristine()
        {
            pristineFolders.Clear();
            pristineFolders.AddRange(folders);
            pristineDisabled.Clear();
            foreach (string d in disabled)
            {
                pristineDisabled.Add(d);
            }
        }

        // Modified means the source list or the enabled set really differs from
        // the last saved state. Comparing snapshots instead of tracking events
        // also means a source toggled off and back on leaves nothing to save.
        private bool IsDirty()
        {
            if (pristineFolders.Count != folders.Count) return true;
            for (int i = 0; i < folders.Count; i++)
            {
                if (!string.Equals(pristineFolders[i], folders[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            if (pristineDisabled.Count != disabled.Count) return true;
            foreach (string d in pristineDisabled)
            {
                if (!disabled.Contains(d)) return true;
            }
            return false;
        }

        // Ask once whether unsaved source changes should be saved. False only
        // when the user picked Cancel.
        private bool ConfirmCloseAllowed()
        {
            if (closing || !IsDirty()) return true;
            DialogResult r = MessageBox.Show(this,
                Loc.T("src.confirm.close"),
                Loc.T("src.caption"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) Save();
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Title-bar X / Alt+F4 land here with CloseReason.UserClosing. Ask
            // about unsaved changes, then LET THE ORIGINAL CLOSE FINISH (a
            // nested Close() from inside this handler is swallowed on a modal
            // form, which used to make the first X click do nothing).
            if (e.CloseReason == CloseReason.UserClosing && !ConfirmCloseAllowed())
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
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
            }
            base.Dispose(disposing);
        }
    }
}
