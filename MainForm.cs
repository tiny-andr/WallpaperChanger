using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperChanger
{
    public class MainForm : Form
    {
        private ListBox lstFolders;
        private Button btnAdd;
        private Button btnRemove;
        private Button btnClearAll;
        private Button btnHelp;
        private ComboBox cmbStyle;
        private ComboBox cmbInterval;
        private CheckBox chkRandom;
        private CheckBox chkAutoStart;
        private Button btnNext;
        private Label lblStatus;

        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem miPause;
        private ComboBox cmbHotkey;
        private ComboBox cmbHotkeyPrev;
        private readonly HotkeyManager hotkeyManager;

        private System.Windows.Forms.Timer rotateTimer;
        private bool busy;
        private bool reallyExit;
        private bool trayNotified;
        private readonly Random rng = new Random();

        private List<string> workList = new List<string>();
        private int workIndex;
        private string lastApplied;
        private bool loadingUi;   // suppress change handlers while the UI is being initialized
        private bool dirty;       // unsaved changes present

        // Wallpapers actually applied by this program since startup, newest
        // last. The first entry is the wallpaper that was up when the program
        // started, so "previous" can step all the way back to it.
        private readonly List<string> history = new List<string>();
        private const int HistoryLimit = 300;

        // "Forward" stack for redo (browser-style back/forward): whenever
        // "previous" steps away from a wallpaper, that wallpaper is pushed
        // here, and the next "next" pops it and re-applies it instead of
        // picking a fresh random one. This keeps Next -> Prev -> Next
        // returning to the exact same image the user just stepped back from.
        private readonly List<string> forward = new List<string>();
        private int lastTotal;   // image count of the most recent scan, for the redo status line

        public MainForm()
        {
            Text = "WallpaperChanger v" + Application.ProductVersion;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            // High-DPI support: declare the 96 DPI design basis and let
            // WinForms scale the whole layout (controls + font) proportionally
            // on any monitor, so 100% and 150% screens look identical.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 492);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = SystemColors.Control;

            hotkeyManager = new HotkeyManager(this);
            BuildUi();
            BuildTray();

            rotateTimer = new System.Windows.Forms.Timer();
            // Automatic rotation always advances to a fresh wallpaper (never
            // "redo"s a manual previous/next step).
            rotateTimer.Tick += delegate { AutoRotate(); };

            Config.Load();
            Log.Write("config: hotkey=" + Config.Hotkey + ", folders=" + Config.Folders.Count);

            // Populate the controls without letting any "changed -> save"
            // handler run half-initialized, then persist once with the real
            // values. This keeps hotkey= from being clobbered to -1 on startup.
            loadingUi = true;
            try
            {
                LoadSettingsIntoUi();
                SyncAutoStartCheckbox();
            }
            finally
            {
                loadingUi = false;
            }
            SaveFromUi();

            if (Config.AutoStart) AutoStartHelper.SetAutoStart(true);

            // Remember the wallpaper that was up at startup as the oldest
            // "previous" target, so going back can reach it.
            try
            {
                List<string> startWalls = WallpaperEngine.GetCurrentWallpaperPaths();
                foreach (string p in startWalls)
                {
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        PushHistory(p);
                        break;
                    }
                }
            }
            catch
            {
            }

            // If the current desktop wallpaper is already one from the configured
            // sources, don't immediately swap it - just start the timer.
            if (HasValidFolders() && !CurrentWallpaperInSource())
            {
                NextWallpaper();
            }
            RestartTimer();
        }

        private void BuildUi()
        {
            GroupBox gbSource = new GroupBox();
            gbSource.Text = "壁纸源（通过按钮添加，不可手动输入）";
            gbSource.SetBounds(12, 12, 456, 168);
            Controls.Add(gbSource);

            lstFolders = new ListBox();
            lstFolders.SetBounds(15, 42, 336, 114);
            lstFolders.SelectionMode = SelectionMode.One;
            gbSource.Controls.Add(lstFolders);

            btnAdd = new Button();
            btnAdd.Text = "添加...";
            btnAdd.SetBounds(361, 42, 82, 30);
            btnAdd.Click += delegate { BrowseFolder(); };
            gbSource.Controls.Add(btnAdd);

            btnRemove = new Button();
            btnRemove.Text = "删除选中";
            btnRemove.SetBounds(361, 76, 82, 30);
            btnRemove.Click += delegate { RemoveSelectedFolder(); };
            gbSource.Controls.Add(btnRemove);

            btnClearAll = new Button();
            btnClearAll.Text = "清空全部";
            btnClearAll.SetBounds(361, 110, 82, 30);
            btnClearAll.Click += delegate { ClearAllFolders(); };
            gbSource.Controls.Add(btnClearAll);

            GroupBox gbSettings = new GroupBox();
            gbSettings.Text = "轮换设置";
            gbSettings.SetBounds(12, 188, 456, 200);
            Controls.Add(gbSettings);

            Label l2 = new Label();
            l2.Text = "壁纸样式:";
            l2.SetBounds(15, 28, 70, 22);
            gbSettings.Controls.Add(l2);

            cmbStyle = new ComboBox();
            cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStyle.SetBounds(90, 25, 170, 25);
            cmbStyle.Items.AddRange(new object[] { "填充（默认）", "适应", "拉伸", "平铺", "居中", "跨区" });
            cmbStyle.SelectedIndex = 0;
            cmbStyle.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; RestartTimer(); };
            gbSettings.Controls.Add(cmbStyle);

            Label l3 = new Label();
            l3.Text = "切换频率:";
            l3.SetBounds(15, 62, 70, 22);
            gbSettings.Controls.Add(l3);

            cmbInterval = new ComboBox();
            cmbInterval.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbInterval.SetBounds(90, 59, 170, 25);
            cmbInterval.Items.AddRange(new object[] { "1 分钟", "5 分钟", "10 分钟", "30 分钟", "1 小时", "6 小时", "12 小时", "24 小时" });
            cmbInterval.SelectedIndex = 2;
            cmbInterval.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; RestartTimer(); };
            gbSettings.Controls.Add(cmbInterval);

            chkRandom = new CheckBox();
            chkRandom.Text = "随机图片顺序";
            chkRandom.SetBounds(15, 96, 200, 22);
            chkRandom.Checked = true;
            chkRandom.CheckedChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; };
            gbSettings.Controls.Add(chkRandom);

            Label lh = new Label();
            lh.Text = "下一张:";
            lh.SetBounds(15, 133, 52, 22);
            gbSettings.Controls.Add(lh);

            cmbHotkey = new ComboBox();
            cmbHotkey.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbHotkey.SetBounds(67, 129, 128, 25);
            cmbHotkey.Items.AddRange(new object[] { "无快捷键", "Ctrl+0", "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4", "Ctrl+5", "Ctrl+6", "Ctrl+7", "Ctrl+8", "Ctrl+9" });
            cmbHotkey.SelectedIndex = 0;
            cmbHotkey.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; ApplyHotkey(); };
            gbSettings.Controls.Add(cmbHotkey);

            Label lh2 = new Label();
            lh2.Text = "上一张:";
            lh2.SetBounds(202, 133, 52, 22);
            gbSettings.Controls.Add(lh2);

            cmbHotkeyPrev = new ComboBox();
            cmbHotkeyPrev.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbHotkeyPrev.SetBounds(254, 129, 150, 25);
            cmbHotkeyPrev.Items.AddRange(new object[] { "无快捷键", "Ctrl+0", "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4", "Ctrl+5", "Ctrl+6", "Ctrl+7", "Ctrl+8", "Ctrl+9" });
            cmbHotkeyPrev.SelectedIndex = 0;
            cmbHotkeyPrev.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; ApplyHotkey(); };
            gbSettings.Controls.Add(cmbHotkeyPrev);

            chkAutoStart = new CheckBox();
            chkAutoStart.Text = "开机自动启动（启动文件夹快捷方式）";
            chkAutoStart.SetBounds(15, 164, 340, 22);
            chkAutoStart.CheckedChanged += delegate
            {
                if (loadingUi) return;
                ApplyFromUi();
                dirty = true;
                AutoStartHelper.SetAutoStart(Config.AutoStart);
            };
            gbSettings.Controls.Add(chkAutoStart);

            btnNext = new Button();
            btnNext.Text = "下一张壁纸";
            btnNext.SetBounds(118, 396, 100, 32);
            btnNext.Click += delegate { NextWallpaper(); };
            Controls.Add(btnNext);

            Button btnPrev = new Button();
            btnPrev.Text = "上一张壁纸";
            btnPrev.SetBounds(12, 396, 100, 32);
            btnPrev.Click += delegate { PrevWallpaper(); };
            Controls.Add(btnPrev);

            Button btnSave = new Button();
            btnSave.Text = "保存设置";
            btnSave.SetBounds(224, 396, 100, 32);
            btnSave.Click += delegate
            {
                SaveFromUi();
                dirty = false;
                SetStatus("设置已保存");
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", "设置已保存", ToolTipIcon.Info);
            };
            Controls.Add(btnSave);

            btnHelp = new Button();
            btnHelp.Text = "帮助";
            btnHelp.SetBounds(330, 396, 100, 32);
            btnHelp.Click += delegate { new HelpForm().ShowDialog(this); };
            Controls.Add(btnHelp);

            lblStatus = new Label();
            lblStatus.SetBounds(12, 440, 456, 40);
            lblStatus.ForeColor = Color.FromArgb(0, 90, 158);
            Controls.Add(lblStatus);
        }

        private void BuildTray()
        {
            trayMenu = new ContextMenuStrip();

            ToolStripMenuItem miNext = new ToolStripMenuItem("下一张壁纸");
            miNext.Click += delegate { NextWallpaper(); };
            trayMenu.Items.Add(miNext);

            ToolStripMenuItem miPrev = new ToolStripMenuItem("上一张壁纸");
            miPrev.Click += delegate { PrevWallpaper(); };
            trayMenu.Items.Add(miPrev);

            miPause = new ToolStripMenuItem("暂停轮换");
            miPause.Click += delegate { TogglePause(); };
            trayMenu.Items.Add(miPause);

            ToolStripMenuItem miOpen = new ToolStripMenuItem("打开设置");
            miOpen.Click += delegate { ShowWindow(); };
            trayMenu.Items.Add(miOpen);

            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate
            {
                if (dirty)
                {
                    DialogResult r = MessageBox.Show("有未保存的设置更改，退出前要保存吗？",
                        "WallpaperChanger", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) return;
                    if (r == DialogResult.Yes) SaveFromUi();
                }
                reallyExit = true;
                notifyIcon.Visible = false;
                Application.Exit();
            };
            trayMenu.Items.Add(miExit);

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = LoadAppIcon();
            notifyIcon.Text = "WallpaperChanger - 壁纸轮换";
            notifyIcon.ContextMenuStrip = trayMenu;
            notifyIcon.DoubleClick += delegate { ShowWindow(); };
            notifyIcon.Visible = true;
        }

        // Use the exe's own icon (the nice one embedded via ApplicationIcon)
        // so the tray and the window agree. Fall back to default if anything fails.
        private Icon LoadAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择一个壁纸图片文件夹";
                string seed = FirstExistingFolder();
                if (seed != null) dlg.SelectedPath = seed;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    AppendFolder(dlg.SelectedPath);
                }
            }
        }

        private string FirstExistingFolder()
        {
            foreach (string f in Config.Folders)
            {
                if (Directory.Exists(f)) return f;
            }
            return null;
        }

        private void AppendFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            // dedupe: same folder added again -> just inform
            foreach (string existing in Config.Folders)
            {
                if (string.Equals(existing.Trim(), folder, StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus("该文件夹已经在壁纸源列表里");
                    return;
                }
            }

            // Only auto-apply when there was NO valid source before, so adding
            // another folder never yanks the wallpaper away from the user.
            bool hadValidSource = HasValidFolders();

            Config.Folders.Add(folder);
            SyncFolderList();
            dirty = true;
            RestartTimer();
            if (!hadValidSource) NextWallpaper();
        }

        private void RemoveSelectedFolder()
        {
            int i = lstFolders.SelectedIndex;
            if (i < 0)
            {
                SetStatus("请先在列表中选中要删除的壁纸源");
                return;
            }
            Config.Folders.RemoveAt(i);
            SyncFolderList();
            dirty = true;
            RestartTimer();
        }

        private void ClearAllFolders()
        {
            if (Config.Folders.Count == 0)
            {
                SetStatus("壁纸源列表已经是空的");
                return;
            }
            Config.Folders.Clear();
            SyncFolderList();
            dirty = true;
            RestartTimer();
        }

        // Mirror the in-memory Config.Folders into the read-only list box.
        private void SyncFolderList()
        {
            lstFolders.BeginUpdate();
            lstFolders.Items.Clear();
            foreach (string f in Config.Folders) lstFolders.Items.Add(f);
            lstFolders.EndUpdate();
        }

        private void LoadSettingsIntoUi()
        {
            SyncFolderList();
            cmbStyle.SelectedIndex = (int)Config.Style;
            int idx = IndexOfInterval(Config.IntervalMinutes);
            cmbInterval.SelectedIndex = idx >= 0 ? idx : 2;
            chkRandom.Checked = Config.RandomOrder;
            chkAutoStart.Checked = Config.AutoStart;
            cmbHotkey.SelectedIndex = (Config.Hotkey >= 0 && Config.Hotkey <= 9) ? Config.Hotkey + 1 : 0;
            cmbHotkeyPrev.SelectedIndex = (Config.HotkeyPrev >= 0 && Config.HotkeyPrev <= 9) ? Config.HotkeyPrev + 1 : 0;
        }

        private int IndexOfInterval(int minutes)
        {
            int[] vals = { 1, 5, 10, 30, 60, 360, 720, 1440 };
            for (int i = 0; i < vals.Length; i++) if (vals[i] == minutes) return i;
            return -1;
        }

        private int IntervalFromIndex(int idx)
        {
            int[] vals = { 1, 5, 10, 30, 60, 360, 720, 1440 };
            if (idx < 0 || idx >= vals.Length) return 10;
            return vals[idx];
        }

        // Read the controls into the in-memory Config (no disk write).
        private void ApplyFromUi()
        {
            // Folders live in the read-only list (a mirror of Config), so
            // rebuild from the list to stay in sync with any UI-side change.
            List<string> folders = new List<string>();
            foreach (object item in lstFolders.Items)
            {
                string s = item.ToString().Trim();
                if (s.Length > 0) folders.Add(s);
            }
            Config.Folders = folders;
            Config.Style = (WallpaperStyle)Math.Max(0, cmbStyle.SelectedIndex);
            Config.IntervalMinutes = IntervalFromIndex(cmbInterval.SelectedIndex);
            Config.RandomOrder = chkRandom.Checked;
            Config.AutoStart = chkAutoStart.Checked;
            // keep the previous values while the hotkey combos are uninitialized
            if (cmbHotkey != null && cmbHotkey.SelectedIndex >= 0)
                Config.Hotkey = cmbHotkey.SelectedIndex - 1;
            if (cmbHotkeyPrev != null && cmbHotkeyPrev.SelectedIndex >= 0)
                Config.HotkeyPrev = cmbHotkeyPrev.SelectedIndex - 1;
        }

        // Apply controls to memory AND persist to disk (save button / exit).
        private void SaveFromUi()
        {
            ApplyFromUi();
            Config.Save();
        }

        private bool HasValidFolders()
        {
            if (Config.Folders.Count == 0) return false;
            foreach (string f in Config.Folders)
            {
                if (Directory.Exists(f)) return true;
            }
            return false;
        }

        private void SyncAutoStartCheckbox()
        {
            chkAutoStart.Checked = AutoStartHelper.AutoStartExists();
        }

        // (Re)register the system-wide hotkeys (next + previous) from config.
        private void ApplyHotkey()
        {
            if (hotkeyManager == null || !IsHandleCreated) return;
            string problem = hotkeyManager.Set(Config.Hotkey, Config.HotkeyPrev);
            if (problem != null) SetStatus(problem);
        }

        private void RestartTimer()
        {
            rotateTimer.Stop();
            if (Config.IntervalMinutes > 0 && HasValidFolders())
            {
                rotateTimer.Interval = Config.IntervalMinutes * 60000;
                rotateTimer.Start();
            }
            RefreshStatusLine();
        }

        private void TogglePause()
        {
            if (rotateTimer.Enabled)
            {
                rotateTimer.Stop();
                miPause.Text = "继续轮换";
                SetStatus("已暂停轮换");
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", "轮换已暂停", ToolTipIcon.Info);
            }
            else
            {
                rotateTimer.Start();
                miPause.Text = "暂停轮换";
                SetStatus("轮换已恢复");
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", "轮换已恢复", ToolTipIcon.Info);
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // Manual "next" entry (hotkey / button / tray): redo-aware. If the
        // user pressed "previous" and is pressing "next" again, restore the
        // wallpaper they stepped away from instead of jumping to a new pick.
        private void NextWallpaper()
        {
            if (busy) return;
            if (!HasValidFolders())
            {
                SetStatus("请先添加至少一个有效的图片文件夹");
                return;
            }

            busy = true;

            if (forward.Count > 0)
            {
                StartRedoTask();
                return;
            }

            StartFreshPickTask();
        }

        // Automatic rotation: always move to a fresh wallpaper and abandon
        // any pending redo (the user is effectively navigating anew), so a
        // stale "forward" entry can never pop back up later by surprise.
        private void AutoRotate()
        {
            if (busy) return;
            if (!HasValidFolders()) return;
            busy = true;
            StartFreshPickTask();
        }

        // Restore the most recent "previous"-departed wallpaper (redo).
        private void StartRedoTask()
        {
            string path = forward[forward.Count - 1];
            forward.RemoveAt(forward.Count - 1);
            int total = lastTotal;
            Task.Run(delegate
            {
                bool ok = false;
                try
                {
                    ok = WallpaperEngine.Apply(path, Config.Style);
                }
                catch
                {
                    ok = false;
                }
                string name = Path.GetFileName(path);
                SafeUi(delegate
                {
                    try
                    {
                        if (ok)
                        {
                            PushHistory(path);
                            Log.Write("next(redo): " + path);
                            SetStatus("当前壁纸: " + name + "（共 " + total + " 张）");
                        }
                        else
                        {
                            Log.Write("redo apply failed: " + path);
                            SetStatus("壁纸设置失败: " + name);
                        }
                    }
                    finally
                    {
                        busy = false;
                    }
                });
            });
        }

        private void StartFreshPickTask()
        {
            forward.Clear();
            List<string> folders = new List<string>(Config.Folders);
            bool recursive = Config.Recursive;

            Task.Run(delegate
            {
                string picked = null;
                int count = 0;
                try
                {
                    List<string> imgs = ImageScanner.ScanMany(folders, recursive);
                    count = imgs.Count;
                    if (count == 0)
                    {
                        SafeUi(delegate
                        {
                            try { SetStatus("所有文件夹里都没有可用图片"); }
                            finally { busy = false; }
                        });
                        return;
                    }
                    picked = PickNext(imgs);
                }
                catch (Exception ex)
                {
                    Log.Write("scan error: " + ex.Message);
                    busy = false;
                    return;
                }

                if (picked != null)
                {
                    SafeUi(delegate { ApplyOnUiThread(picked, count); });
                }
                else
                {
                    busy = false;
                }
            });
        }

        // Read every monitor's current wallpaper via IDesktopWallpaper and
        // return true only when ALL of them already belong to the source set.
        // Returns false on any error (so we err on the side of swapping).
        private bool CurrentWallpaperInSource()
        {
            try
            {
                List<string> current = WallpaperEngine.GetCurrentWallpaperPaths();
                if (current.Count == 0) return false;

                HashSet<string> currentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string p in current)
                {
                    try { currentSet.Add(Path.GetFullPath(p)); }
                    catch { currentSet.Add(p); }
                }

                List<string> source = ImageScanner.ScanMany(Config.Folders, Config.Recursive);
                if (source.Count == 0) return false;

                HashSet<string> sourceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string s in source)
                {
                    try { sourceSet.Add(Path.GetFullPath(s)); }
                    catch { sourceSet.Add(s); }
                }

                foreach (string p in currentSet)
                {
                    if (!sourceSet.Contains(p)) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyOnUiThread(string path, int total)
        {
            try
            {
                bool ok = WallpaperEngine.Apply(path, Config.Style);
                if (ok)
                {
                    Log.Write("applied: " + path);
                    PushHistory(path);
                    lastTotal = total;
                    SetStatus("当前壁纸: " + Path.GetFileName(path) + "（共 " + total + " 张）");
                }
                else
                {
                    Log.Write("apply failed: " + path);
                    SetStatus("壁纸设置失败: " + Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                Log.Write("apply error: " + ex.Message);
                SetStatus("出错: " + ex.Message);
            }
            finally
            {
                busy = false;
            }
        }

        private string PickNext(List<string> imgs)
        {
            if (Config.RandomOrder)
            {
                bool sameSet = workList.Count == imgs.Count;
                if (sameSet)
                {
                    for (int i = 0; i < imgs.Count; i++)
                    {
                        if (!string.Equals(workList[i], imgs[i], StringComparison.OrdinalIgnoreCase))
                        {
                            sameSet = false;
                            break;
                        }
                    }
                }
                if (!sameSet || workIndex >= workList.Count)
                {
                    workList = new List<string>(imgs);
                    ImageScanner.Shuffle(workList, rng);
                    workIndex = 0;
                    if (lastApplied != null && workList.Count > 1 &&
                        string.Equals(workList[0], lastApplied, StringComparison.OrdinalIgnoreCase))
                    {
                        string first = workList[0];
                        workList.RemoveAt(0);
                        workList.Add(first);
                    }
                }
                string p = workList[workIndex];
                workIndex = (workIndex + 1) % workList.Count;
                lastApplied = p;
                return p;
            }
            else
            {
                if (workIndex >= imgs.Count) workIndex = 0;
                string p = imgs[workIndex];
                workIndex = (workIndex + 1) % imgs.Count;
                lastApplied = p;
                return p;
            }
        }

        // Remember an applied wallpaper (newest last). Called on the UI thread
        // only; duplicates of the current entry are ignored.
        private void PushHistory(string path)
        {
            try { path = Path.GetFullPath(path); }
            catch { }
            int n = history.Count;
            if (n > 0 && string.Equals(history[n - 1], path, StringComparison.OrdinalIgnoreCase)) return;
            history.Add(path);
            if (history.Count > HistoryLimit) history.RemoveRange(0, history.Count - HistoryLimit);
        }

        // Park a wallpaper that "previous" stepped away from (newest pushed
        // last). "Next" pops this stack to redo. Mirrors PushHistory's dedupe.
        private void PushForward(string path)
        {
            try { path = Path.GetFullPath(path); }
            catch { }
            int n = forward.Count;
            if (n > 0 && string.Equals(forward[n - 1], path, StringComparison.OrdinalIgnoreCase)) return;
            forward.Add(path);
        }

        // Step back to the wallpaper that was up before the current one.
        // Re-applies the file directly (no scan needed); each press walks one
        // step further back, and "next" afterwards redo-restores the departed
        // wallpaper. Once the forward stack is empty, "next" resumes normal
        // fresh picks.
        private void PrevWallpaper()
        {
            if (busy) return;
            if (history.Count < 2)
            {
                SetStatus("没有更早的壁纸了（这是本次启动后的第一张）");
                return;
            }

            // busy stays set until the UI-thread callback finishes (the
            // history pop and status update), so two quick presses can never
            // read the same pre-pop state twice.
            busy = true;
            string target = history[history.Count - 2];
            Task.Run(delegate
            {
                bool ok = false;
                try
                {
                    ok = WallpaperEngine.Apply(target, Config.Style);
                }
                catch
                {
                    ok = false;
                }

                string name = Path.GetFileName(target);
                SafeUi(delegate
                {
                    try
                    {
                        if (ok)
                        {
                            // Park the wallpaper we just stepped away from so a
                            // following "next" can return to it (redo).
                            string departed = history[history.Count - 1];
                            history.RemoveAt(history.Count - 1);   // drop the current entry
                            PushForward(departed);
                            Log.Write("previous: " + target);
                            SetStatus("当前壁纸: " + name + "（上一张）");
                        }
                        else
                        {
                            SetStatus("壁纸回退失败: " + name);
                        }
                    }
                    finally
                    {
                        busy = false;
                    }
                });
            });
        }

        private void SetStatus(string line)
        {
            lblStatus.Text = line + "\r\n" + NextSwitchText();
        }

        // Keep whatever is on the first line, just refresh the second
        // (the "next switch" / "paused" line) so the countdown updates.
        private void RefreshStatusLine()
        {
            string t = lblStatus.Text ?? "";
            int i = t.IndexOf('\r');
            string first = (i >= 0) ? t.Substring(0, i) : t;
            lblStatus.Text = first + "\r\n" + NextSwitchText();
        }

        private string NextSwitchText()
        {
            if (rotateTimer.Enabled)
                return "下次切换: " + DateTime.Now.AddMilliseconds(rotateTimer.Interval).ToString("HH:mm:ss");
            return "轮换已暂停";
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !reallyExit)
            {
                e.Cancel = true;
                Hide();
                if (!trayNotified)
                {
                    trayNotified = true;
                    notifyIcon.ShowBalloonTip(2000, "WallpaperChanger",
                        "程序仍在后台运行，右键托盘图标可暂停 / 退出", ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        // The handle is (re)created on show and on DPI changes - (re)register
        // the hotkey each time so it never goes stale.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (hotkeyManager != null) ApplyHotkey();
        }

        // System-wide hotkeys arrive as WM_HOTKEY regardless of focus.
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (hotkeyManager == null) return;
            HotkeyAction action = hotkeyManager.Identify((uint)m.Msg, m.WParam);
            if (action == HotkeyAction.Next)
            {
                Log.Write("hotkey: next");
                NextWallpaper();
            }
            else if (action == HotkeyAction.Prev)
            {
                Log.Write("hotkey: prev");
                PrevWallpaper();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (hotkeyManager != null) hotkeyManager.Dispose();
                if (notifyIcon != null) notifyIcon.Dispose();
                if (rotateTimer != null) rotateTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
