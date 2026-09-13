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
        private Button btnSources;
        private Label lblSourceSummary;
        private Button btnHelp;
        private Button btnManualPick;
        private ComboBox cmbStyle;
        private ComboBox cmbInterval;
        private ComboBox cmbLang;
        private CheckBox chkRandom;
        private CheckBox chkAutoStart;
        private Button btnNext;
        private Button btnPrev;
        private Button btnSave;
        private Label lblStatus;
        private GroupBox gbSource;
        private GroupBox gbSettings;
        private Label lblStyle;
        private Label lblInterval;
        private Label lblHotkey;
        private Label lblHotkeyPrev;
        private Label lblLang;

        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem miPause;
        private ToolStripMenuItem miNext;
        private ToolStripMenuItem miPrev;
        private ToolStripMenuItem miManual;
        private ToolStripMenuItem miOpen;
        private ToolStripMenuItem miExit;
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
            // WinForms scale the whole layout proportionally on any monitor.
            // Order matters: the AutoScaleMode setter resets AutoScaleDimensions,
            // so the design basis must be assigned AFTER the mode.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(480, 560);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = SystemColors.Control;

            // Suppress all "changed -> save" handlers from the very first
            // control creation (ApplyTexts also touches combo selections).
            loadingUi = true;
            hotkeyManager = new HotkeyManager(this);
            BuildUi();
            BuildTray();

            rotateTimer = new System.Windows.Forms.Timer();
            // Automatic rotation always advances to a fresh wallpaper (never
            // "redo"s a manual previous/next step).
            rotateTimer.Tick += delegate { AutoRotate(); };

            Config.Load();
            Log.Write("config: hotkey=" + Config.Hotkey + ", folders=" + Config.Folders.Count
                + ", disabled=" + Config.DisabledFolders.Count);

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
            gbSource = new GroupBox();
            gbSource.SetBounds(12, 12, 456, 168);
            Controls.Add(gbSource);

            // One entry point into the source manager. The old inline list
            // and its add / remove / clear buttons moved into that dialog,
            // which also gained per-source enable/disable and image counts.
            btnSources = new Button();
            btnSources.SetBounds(15, 40, 426, 46);
            btnSources.Click += delegate { OpenSourceManager(); };
            gbSource.Controls.Add(btnSources);

            lblSourceSummary = new Label();
            lblSourceSummary.SetBounds(15, 96, 426, 56);
            lblSourceSummary.ForeColor = Color.FromArgb(96, 96, 96);
            gbSource.Controls.Add(lblSourceSummary);

            // Manual wallpaper picker: opens the selection dialog where the
            // user curates which wallpapers participate in switching.
            btnManualPick = new Button();
            btnManualPick.SetBounds(12, 186, 456, 32);
            btnManualPick.Click += delegate { OpenManualPicker(); };
            Controls.Add(btnManualPick);

            gbSettings = new GroupBox();
            gbSettings.SetBounds(12, 226, 456, 232);
            Controls.Add(gbSettings);

            lblStyle = new Label();
            lblStyle.SetBounds(15, 28, 74, 22);
            gbSettings.Controls.Add(lblStyle);

            cmbStyle = new ComboBox();
            cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStyle.SetBounds(92, 25, 170, 25);
            cmbStyle.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; RestartTimer(); };
            gbSettings.Controls.Add(cmbStyle);

            lblInterval = new Label();
            lblInterval.SetBounds(15, 62, 74, 22);
            gbSettings.Controls.Add(lblInterval);

            cmbInterval = new ComboBox();
            cmbInterval.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbInterval.SetBounds(92, 59, 170, 25);
            cmbInterval.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; RestartTimer(); };
            gbSettings.Controls.Add(cmbInterval);

            chkRandom = new CheckBox();
            chkRandom.SetBounds(15, 96, 220, 22);
            chkRandom.Checked = true;
            chkRandom.CheckedChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; };
            gbSettings.Controls.Add(chkRandom);

            lblHotkey = new Label();
            lblHotkey.SetBounds(15, 133, 52, 22);
            gbSettings.Controls.Add(lblHotkey);

            cmbHotkey = new ComboBox();
            cmbHotkey.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbHotkey.SetBounds(67, 129, 128, 25);
            cmbHotkey.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; ApplyHotkey(); };
            gbSettings.Controls.Add(cmbHotkey);

            lblHotkeyPrev = new Label();
            lblHotkeyPrev.SetBounds(202, 133, 52, 22);
            gbSettings.Controls.Add(lblHotkeyPrev);

            cmbHotkeyPrev = new ComboBox();
            cmbHotkeyPrev.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbHotkeyPrev.SetBounds(254, 129, 150, 25);
            cmbHotkeyPrev.SelectedIndexChanged += delegate { if (loadingUi) return; ApplyFromUi(); dirty = true; ApplyHotkey(); };
            gbSettings.Controls.Add(cmbHotkeyPrev);

            chkAutoStart = new CheckBox();
            chkAutoStart.SetBounds(15, 164, 340, 22);
            chkAutoStart.CheckedChanged += delegate
            {
                if (loadingUi) return;
                ApplyFromUi();
                dirty = true;
                AutoStartHelper.SetAutoStart(Config.AutoStart);
            };
            gbSettings.Controls.Add(chkAutoStart);

            // UI language selector: native names (中文 / English / 日本語),
            // applied immediately and persisted at once.
            lblLang = new Label();
            lblLang.AutoSize = true;
            lblLang.SetBounds(15, 199, 0, 22);
            gbSettings.Controls.Add(lblLang);
            lblLang.Text = Loc.T("main.settings.language");

            cmbLang = new ComboBox();
            cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLang.SetBounds(110, 193, 170, 25);
            cmbLang.Items.AddRange(Loc.LanguageDisplayNames);
            cmbLang.SelectedIndex = 0;
            cmbLang.SelectedIndexChanged += delegate
            {
                if (loadingUi) return;
                int i = cmbLang.SelectedIndex;
                if (i < 0 || i >= Loc.LanguageCodes.Length) return;
                ChangeLanguage(Loc.LanguageCodes[i]);
            };
            gbSettings.Controls.Add(cmbLang);

            btnNext = new Button();
            btnNext.SetBounds(118, 466, 100, 32);
            btnNext.Click += delegate { NextWallpaper(); };
            Controls.Add(btnNext);

            btnPrev = new Button();
            btnPrev.SetBounds(12, 466, 100, 32);
            btnPrev.Click += delegate { PrevWallpaper(); };
            Controls.Add(btnPrev);

            btnSave = new Button();
            btnSave.SetBounds(224, 466, 100, 32);
            btnSave.Click += delegate
            {
                SaveFromUi();
                dirty = false;
                SetStatus(Loc.T("status.saved"));
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", Loc.T("status.saved"), ToolTipIcon.Info);
            };
            Controls.Add(btnSave);

            btnHelp = new Button();
            btnHelp.SetBounds(330, 466, 100, 32);
            btnHelp.Click += delegate { new HelpForm().ShowDialog(this); };
            Controls.Add(btnHelp);

            lblStatus = new Label();
            lblStatus.SetBounds(12, 508, 456, 44);
            lblStatus.ForeColor = Color.FromArgb(0, 90, 158);
            Controls.Add(lblStatus);
        }

        // Re-apply every user-visible string of this form (and the tray) in
        // the active language. Called once after the controls exist and
        // again whenever the user switches the language, so no restart is
        // needed. Combo selections survive the item rebuilds.
        private void ApplyTexts()
        {
            gbSource.Text = Loc.T("main.source.group");
            btnSources.Text = Loc.T("main.source.manage");
            btnManualPick.Text = Loc.T("main.manual.btn");
            gbSettings.Text = Loc.T("main.settings.group");
            lblStyle.Text = Loc.T("main.settings.style");
            lblInterval.Text = Loc.T("main.settings.interval");
            chkRandom.Text = Loc.T("main.settings.random");
            lblHotkey.Text = Loc.T("main.settings.next");
            lblHotkeyPrev.Text = Loc.T("main.settings.prev");
            chkAutoStart.Text = Loc.T("main.settings.autostart");
            lblLang.Text = Loc.T("main.settings.language");
            btnNext.Text = Loc.T("main.btn.next");
            btnPrev.Text = Loc.T("main.btn.prev");
            btnSave.Text = Loc.T("main.btn.save");
            btnHelp.Text = Loc.T("main.btn.help");
            RefreshSourceSummary();

            SetComboItems(cmbStyle, Loc.StyleNames());
            SetComboItems(cmbInterval, Loc.IntervalNames());
            SetComboItems(cmbHotkey, HotkeyItems());
            SetComboItems(cmbHotkeyPrev, HotkeyItems());

            miNext.Text = Loc.T("tray.next");
            miPrev.Text = Loc.T("tray.prev");
            miPause.Text = rotateTimer != null && !rotateTimer.Enabled ? Loc.T("tray.resume") : Loc.T("tray.pause");
            miManual.Text = Loc.T("tray.manual");
            miOpen.Text = Loc.T("tray.open");
            miExit.Text = Loc.T("tray.exit");
            notifyIcon.Text = Loc.T("tray.tip");

            // Refresh only the countdown line; the first status line (if any)
            // is left to the next status event, which writes in the new
            // language. Avoids seeding a bogus "paused" line during
            // construction, when the rotate timer does not exist yet.
            RefreshStatusLine();
        }

        private static string[] HotkeyItems()
        {
            List<string> items = new List<string>();
            items.Add(Loc.T("main.hotkey.none"));
            for (int d = 0; d <= 9; d++) items.Add("Ctrl+" + d);
            return items.ToArray();
        }

        // Replace a DropDownList's items while keeping the selection.
        private static void SetComboItems(ComboBox cmb, string[] items)
        {
            if (cmb == null) return;
            int sel = cmb.SelectedIndex;
            cmb.Items.Clear();
            cmb.Items.AddRange(items);
            if (sel >= 0 && sel < items.Length) cmb.SelectedIndex = sel;
        }

        // Switch the whole UI language at runtime: update Loc, remember it
        // in the config and persist right away (a language choice is an
        // unambiguous one-click decision, no separate "save" needed).
        private void ChangeLanguage(string lang)
        {
            Loc.SetLanguage(lang);
            Config.Language = lang;
            Config.Save();
            ApplyTexts();
        }

        private void BuildTray()
        {
            trayMenu = new ContextMenuStrip();

            miNext = new ToolStripMenuItem();
            miNext.Click += delegate { NextWallpaper(); };
            trayMenu.Items.Add(miNext);

            miPrev = new ToolStripMenuItem();
            miPrev.Click += delegate { PrevWallpaper(); };
            trayMenu.Items.Add(miPrev);

            miPause = new ToolStripMenuItem();
            miPause.Click += delegate { TogglePause(); };
            trayMenu.Items.Add(miPause);

            miManual = new ToolStripMenuItem();
            miManual.Click += delegate { OpenManualPicker(); };
            trayMenu.Items.Add(miManual);

            miOpen = new ToolStripMenuItem();
            miOpen.Click += delegate { ShowWindow(); };
            trayMenu.Items.Add(miOpen);

            trayMenu.Items.Add(new ToolStripSeparator());

            miExit = new ToolStripMenuItem();
            miExit.Click += delegate
            {
                if (dirty)
                {
                    DialogResult r = MessageBox.Show(Loc.T("tray.exit.confirm"),
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
            notifyIcon.ContextMenuStrip = trayMenu;
            notifyIcon.DoubleClick += delegate { ShowWindow(); };
            notifyIcon.Visible = true;

            // Fill every caption (controls + tray) in the active language.
            ApplyTexts();
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

        // Open the source manager (modal). It persists to Config on its own
        // save button, so here we only refresh the summary and restart the
        // timer; the first valid source also kicks off a wallpaper swap.
        private void OpenSourceManager()
        {
            bool hadValid = HasValidFolders();
            bool changed;
            using (SourceManagerForm dlg = new SourceManagerForm(this))
            {
                dlg.ShowDialog(this);
                changed = dlg.Changed;
            }
            if (!changed) return;
            RefreshSourceSummary();
            RestartTimer();
            if (!hadValid && HasValidFolders()) NextWallpaper();
        }

        // Two-line summary under the manager button: how many sources exist,
        // how many are enabled, and which ones are currently disabled.
        private void RefreshSourceSummary()
        {
            if (lblSourceSummary == null) return;
            int total = Config.Folders.Count;
            if (total == 0)
            {
                lblSourceSummary.Text = Loc.T("main.source.summary.none");
                return;
            }
            List<string> off = new List<string>();
            foreach (string f in Config.Folders)
            {
                if (!Config.IsSourceEnabled(f)) off.Add(SourceName(f));
            }
            string head = Loc.F("main.source.summary", total, total - off.Count, off.Count);
            if (off.Count == 0)
                head += "\r\n" + Loc.T("main.source.all.on");
            else
                head += "\r\n" + Loc.F("main.source.off.list",
                    string.Join(Loc.T("main.source.off.sep"), off.ToArray()));
            lblSourceSummary.Text = head;
        }

        private static string SourceName(string folder)
        {
            try
            {
                string n = Path.GetFileName(folder.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch
            {
            }
            return folder;
        }

        private void LoadSettingsIntoUi()
        {
            RefreshSourceSummary();
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
            // Sources are owned by the source manager, which writes straight
            // to Config; there is nothing to collect from the UI here.
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

        // At least one ENABLED source must exist on disk. Disabled sources
        // are ignored, so turning every source off also stops the timer.
        private bool HasValidFolders()
        {
            foreach (string f in Config.EnabledFolders())
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
                miPause.Text = Loc.T("tray.resume");
                SetStatus(Loc.T("status.rotate.paused"));
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", Loc.T("status.paused"), ToolTipIcon.Info);
            }
            else
            {
                rotateTimer.Start();
                miPause.Text = Loc.T("tray.pause");
                SetStatus(Loc.T("status.rotate.resumed"));
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", Loc.T("status.rotate.resumed"), ToolTipIcon.Info);
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // Open the manual wallpaper picker (modal, on the main window's own
        // screen). The picker persists straight to Config on its own 保存
        // button, so after it closes we only mirror a mode change.
        private void OpenManualPicker()
        {
            bool wasOn = Config.ManualPicked.Count > 0;
            using (ManualPickerForm dlg = new ManualPickerForm(this))
            {
                dlg.ShowDialog(this);
            }
            bool nowOn = Config.ManualPicked.Count > 0;
            if (wasOn != nowOn)
            {
                RefreshStatusLine();
                if (nowOn)
                    notifyIcon.ShowBalloonTip(1800, "WallpaperChanger",
                        Loc.F("balloon.manual.on", Config.ManualPicked.Count),
                        ToolTipIcon.Info);
                else
                    notifyIcon.ShowBalloonTip(1800, "WallpaperChanger",
                        Loc.T("balloon.manual.off"), ToolTipIcon.Info);
            }
        }

        // Manual "next" entry (hotkey / button / tray): redo-aware. If the
        // user pressed "previous" and is pressing "next" again, restore the
        // wallpaper they stepped away from instead of jumping to a new pick.
        private void NextWallpaper()
        {
            if (busy) return;
            if (!HasValidFolders())
            {
                SetStatus(Loc.T("status.novalidfolder"));
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
                            SetStatus(Loc.F("status.current", name, total) + ModeTag());
                        }
                        else
                        {
                            Log.Write("redo apply failed: " + path);
                            SetStatus(Loc.F("status.applyfail", name));
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
            List<string> folders = Config.EnabledFolders();
            bool recursive = Config.Recursive;

            Task.Run(delegate
            {
                string picked = null;
                int count = 0;
                try
                {
                    List<string> imgs = ImageScanner.ScanMany(folders, recursive);
                    List<string> pool = RestrictToPicked(imgs);
                    count = pool.Count;
                    if (count == 0)
                    {
                        SafeUi(delegate
                        {
                            try
                            {
                                SetStatus(Config.ManualPicked.Count > 0
                                    ? Loc.T("status.manual.emptypool")
                                    : Loc.T("status.nopictures"));
                            }
                            finally { busy = false; }
                        });
                        return;
                    }
                    picked = PickNext(pool);
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

                List<string> source = ImageScanner.ScanMany(Config.EnabledFolders(), Config.Recursive);
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
                    SetStatus(Loc.F("status.current", Path.GetFileName(path), total) + ModeTag());
                }
                else
                {
                    Log.Write("apply failed: " + path);
                    SetStatus(Loc.F("status.applyfail", Path.GetFileName(path)));
                }
            }
            catch (Exception ex)
            {
                Log.Write("apply error: " + ex.Message);
                SetStatus(Loc.F("status.error", ex.Message));
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

        // When any wallpaper is checked, narrow a fresh scan result down to
        // the checked set (unchecked files never enter rotation). Otherwise
        // the list is returned unchanged, so order/random modes keep working
        // on the full pool exactly as before.
        private List<string> RestrictToPicked(List<string> imgs)
        {
            if (Config.ManualPicked.Count == 0) return imgs;
            HashSet<string> pick = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in Config.ManualPicked)
            {
                try { pick.Add(Path.GetFullPath(p)); }
                catch { }
            }
            List<string> pool = new List<string>();
            foreach (string p in imgs)
            {
                bool ok = false;
                try { ok = pick.Contains(Path.GetFullPath(p)); }
                catch { }
                if (ok) pool.Add(p);
            }
            return pool;
        }

        // Status suffix shown while any wallpaper is checked, so the mode
        // is visible on every wallpaper line without extra dialogs.
        private string ModeTag()
        {
            return Config.ManualPicked.Count > 0
                ? Loc.F("mode.tag", Config.ManualPicked.Count)
                : "";
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
                SetStatus(Loc.T("status.noprev"));
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
                            SetStatus(Loc.F("status.current.prev", name) + ModeTag());
                        }
                        else
                        {
                            SetStatus(Loc.F("status.prevfail", name));
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
            // rotateTimer is created after BuildUi/BuildTray, and ApplyTexts
            // runs inside BuildTray, so it can still be null here.
            if (rotateTimer != null && rotateTimer.Enabled)
                return Loc.F("status.nextswitch", DateTime.Now.AddMilliseconds(rotateTimer.Interval).ToString("HH:mm:ss"));
            return Loc.T("status.paused");
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
                        Loc.T("balloon.stillrunning"), ToolTipIcon.Info);
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
            Log.Write("ui: dpi=" + DeviceDpi + " scale=" + (DeviceDpi / 96f).ToString("0.00")
                + " client=" + ClientSize.Width + "x" + ClientSize.Height);
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
