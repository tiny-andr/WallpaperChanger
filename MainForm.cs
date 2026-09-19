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
        // ---- shell ---------------------------------------------------------
        private TitleBar bar;
        private NavRail rail;
        private StatusBar footer;
        private Panel content;
        private readonly List<PageStack> pages = new List<PageStack>();

        // ---- overview page -------------------------------------------------
        private PreviewBox nowPreview;
        private FlatButton btnPrev;
        private FlatButton btnNext;
        // The overview settings row: the three rotation knobs as editable
        // dropdowns, so changing them needs no separate page.
        private KitDropdown cmbStyle;
        private KitDropdown cmbInterval;
        private KitDropdown cmbOrder;
        private HistoryList histList;
        private CardPanel histCard;
        private PageStack histPage;
        private readonly KitLabel[] factVal = new KitLabel[3];
        private readonly KitLabel[] factLab = new KitLabel[3];

        // ---- sources page --------------------------------------------------
        private CardPanel cardSrc;
        private SourceList srcList;
        private KitLabel lblSrcListNote;
        private FlatButton btnAddFolder;
        private FlatButton btnManualPick;
        private FlatButton btnAllOn;
        private FlatButton btnAllOff;
        private FlatButton btnRecount;
        // Bumped on every recount so a scan that is still running when the
        // list changes cannot write stale counts into the new rows.
        private int countGeneration;

        // ---- hotkeys (live on the general page) ----------------------------
        private KitLabel lblHkNextName;
        private KitLabel lblHkPrevName;
        private KitLabel lblHkNext;
        private KitLabel lblHkPrev;
        private KeyCapButton keyNext;
        private KeyCapButton keyPrev;

        // ---- general page --------------------------------------------------
        private ToggleSwitch swAutoStart;
        private SegmentedControl segTheme;
        private KitDropdown cmbLang;
        private KitLabel lblAppTitle;
        private KitLabel lblAppNote;
        private KitLabel lblCfgPath;
        private KitLabel lblAbout;
        private FlatButton btnOpenFolder;

        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem miPause;
        private ToolStripMenuItem miNext;
        private ToolStripMenuItem miPrev;
        private ToolStripMenuItem miManual;
        private ToolStripMenuItem miOpen;
        private ToolStripMenuItem miExit;
        private readonly HotkeyManager hotkeyManager;

        private System.Windows.Forms.Timer rotateTimer;
        private System.Windows.Forms.Timer uiTick;
        private System.Windows.Forms.Timer noticeTimer;

        // When the running timer will fire next. A fixed point in time, so the
        // displayed countdown counts DOWN to it instead of drifting every time
        // the status line repaints.
        private DateTime nextSwitchAt = DateTime.MinValue;
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

        // The last Normal-state rectangle, kept so a maximised window can be
        // checked against the monitor the window actually came from. Screen
        // .FromControl would happily confirm the wrong monitor, because by then
        // the window is sitting on it.
        private Rectangle normalBounds;
        private FormWindowState lastState = FormWindowState.Normal;
        private bool unmaximizeQueued;

        // Set when the process was launched by the Startup shortcut: the
        // window stays in the tray so booting the machine does not drop a
        // dialog in the middle of the screen.
        private readonly bool startHidden;
        private bool allowVisible;

        // Width of the single right-hand control column on the general page
        // (theme segments / language dropdown). Both controls pin to the same
        // right edge with the same width, so their left edges line up.
        private const int LangColW = 200;

        public MainForm() : this(false)
        {
        }

        public MainForm(bool startHidden)
        {
            this.startHidden = startHidden;
            Text = "WallpaperChanger v" + Application.ProductVersion;
            StartPosition = FormStartPosition.CenterScreen;
            // Borderless, but the system frame style is kept alive so DWM
            // still hands over the shadow, Aero snap and native maximise.
            // WM_NCCALCSIZE answers 0, so the client area covers the whole
            // window and the custom title bar owns the top 46px.
            FormBorderStyle = FormBorderStyle.None;
            // The layout is a fixed 680px-wide design; maximising it just
            // stretches empty space and exposes every layout weakness. The
            // one window that benefits from being big - the wallpaper picker
            // - is its own dialog and stays resizable.
            MaximizeBox = false;
            MinimizeBox = true;
            // High-DPI support: declare the 96 DPI design basis and let
            // WinForms scale the whole layout proportionally on any monitor.
            // Order matters: the AutoScaleMode setter resets AutoScaleDimensions,
            // so the design basis must be assigned AFTER the mode.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            // Size, not ClientSize: the window keeps WS_THICKFRAME so DWM still
            // provides snap and shadow, while WM_NCCALCSIZE suppresses the
            // frame that WinForms measures. Its idea of the client is therefore
            // 14px smaller than the area that actually gets painted, and asking
            // for a 980 client produced a 994 window. Sizing the window itself
            // is what makes WindowW mean the pixels on screen.
            Size = new Size(Theme.WindowW, Theme.WindowH);
            MinimumSize = new Size(Theme.WindowMinW, Theme.WindowMinH);
            // No Padding: the shell has to start at (0,0) like the design's, or
            // the whole window sits 6px in from its own edge. Edge resizing does
            // not need a reserved band either - WM_NCHITTEST hands the outer
            // few pixels back as resize borders on its own.
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Theme.FormBack;

            // Suppress all "changed -> save" handlers from the very first
            // control creation (ApplyTexts also touches combo selections).
            loadingUi = true;
            hotkeyManager = new HotkeyManager(this);
            // The owner-drawn controls ask InputMode before painting a focus
            // ring, so it has to start watching before the window takes input.
            InputMode.Install();
            BuildUi();
            BuildTray();

            rotateTimer = new System.Windows.Forms.Timer();
            // Automatic rotation always advances to a fresh wallpaper (never
            // "redo"s a manual previous/next step).
            rotateTimer.Tick += delegate
            {
                nextSwitchAt = DateTime.Now + TimeSpan.FromMinutes(Config.IntervalMinutes);
                AutoRotate();
            };

            // A one-second UI pulse. The "next switch at" readout is a point
            // in time (nextSwitchAt), not "now + interval" - recomputing it
            // from now on every repaint made the countdown freeze at whatever
            // the last repaint happened to be, and any unrelated status
            // change silently pushed the target into the future.
            uiTick = new System.Windows.Forms.Timer();
            uiTick.Interval = 1000;
            uiTick.Tick += delegate { RefreshCountdown(); };
            uiTick.Start();

            // Footer notices are transient: long enough to read a sentence,
            // short enough that a stale "no pictures" cannot greet the user
            // ten minutes later.
            noticeTimer = new System.Windows.Forms.Timer();
            noticeTimer.Interval = 8000;
            noticeTimer.Tick += delegate { ClearNotice(); };

            Config.Load();
            Theme.Set(Config.ThemeMode);
            ApplyTheme();
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
            dirty = false;
            RefreshDirty();
            RefreshStrip();
            SyncRailState();
            // The source rows show a live wallpaper count, so the first scan
            // starts as soon as the list exists rather than when the user
            // happens to open the sources page.
            StartSourceCounts(false);

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
            BuildShell();
            BuildOverviewPage();
            BuildSourcesPage();
            BuildGeneralPage();
            ShowPage(0);
        }

        // ---- shell ---------------------------------------------------------

        // The four chrome regions are laid out by hand against the REAL client
        // rectangle, not docked. With WS_THICKFRAME kept alive (for the DWM
        // shadow and snap) WinForms believes the client is 14px smaller than
        // the area WM_NCCALCSIZE actually hands back, so a docked layout
        // leaves an unpainted black strip along the right and bottom edges.
        private void LayoutChrome()
        {
            // Handle creation itself fires WM_SIZE (WmCreate -> UpdateBounds ->
            // OnResize) while bar/rail/footer/content are still null, so this
            // guard has to cover both conditions, not just the handle.
            if (!IsHandleCreated || bar == null || rail == null || footer == null || content == null) return;
            RECT c;
            GetClientRect(Handle, out c);
            int w = c.Right - c.Left;
            int h = c.Bottom - c.Top;
            int barH = Gfx.S(this, Theme.TitleBarH);
            int footH = Gfx.S(this, Theme.StatusBarH);
            int railW = Gfx.S(this, Theme.RailW);
            bar.SetBounds(0, 0, w, barH);
            rail.SetBounds(0, barH, railW, Math.Max(0, h - barH - footH));
            footer.SetBounds(0, Math.Max(barH, h - footH), w, Math.Min(footH, Math.Max(0, h - barH)));
            content.SetBounds(railW, barH, Math.Max(0, w - railW), Math.Max(0, h - barH - footH));
        }

        private void BuildShell()
        {
            content = new Panel();
            content.BackColor = Theme.FormBack;

            rail = new NavRail();
            rail.Width = Theme.RailW;
            rail.SelectedIndexChanged += delegate { ShowPage(rail.SelectedIndex); };
            rail.PauseClicked += delegate { TogglePause(); };

            footer = new StatusBar();
            footer.Height = Theme.StatusBarH;
            footer.SaveClicked += delegate { SaveFromFooter(); };

            bar = new TitleBar();
            bar.Height = Theme.TitleBarH;
            bar.AppIcon = AppIconImage();
            bar.HasMaxButton = false;
            bar.HelpClicked += delegate { new HelpForm().ShowDialog(this); };

            // Dock order is the reverse of the add order: the content host
            // takes whatever is left once the three edges are claimed.
            Controls.Add(content);
            Controls.Add(rail);
            Controls.Add(footer);
            Controls.Add(bar);

            // The handle may already exist by now (it did once the crash log
            // showed WM_SIZE arriving during WmCreate), so the first chrome
            // layout has to happen here, not only in OnLoad/OnResize.
            LayoutChrome();
        }

        private static Image AppIconImage()
        {
            try
            {
                using (Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    return ico == null ? null : ico.ToBitmap();
                }
            }
            catch
            {
                return null;
            }
        }

        // A page is a card stack whose header doubles as the page title.
        // Passing no keys gives a page that opens straight into its first
        // card, which is what the overview pane does.
        private PageStack NewPage(string titleKey, string noteKey)
        {
            PageStack p = new PageStack();
            p.Visible = false;
            if (titleKey != null)
            {
                p.Head.Title = Loc.T(titleKey);
                p.Head.Note = Loc.T(noteKey);
            }
            content.Controls.Add(p);
            pages.Add(p);
            return p;
        }

        private void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == index;
            if (rail.SelectedIndex != index) rail.SelectedIndex = index;
        }

        private static KitLabel Txt(LabelStyle style)
        {
            return Txt(style, false);
        }

        private static KitLabel Txt(LabelStyle style, bool muted)
        {
            KitLabel l = new KitLabel();
            l.Style = style;
            l.Muted = muted;
            return l;
        }

        // ---- overview ------------------------------------------------------

        private void BuildOverviewPage()
        {
            PageStack page = NewPage(null, null);

            // "正在显示": preview on the left, facts and actions on the right.
            // 178px: the preview's own 210px box would force the card taller,
            // and without the file name / path rows the right column needs
            // only the facts, a rule and the two buttons. Sized to that, so
            // there is no empty band left under the buttons.
            CardPanel hero = page.AddCard(Theme.CardPad * 2 + 142);
            nowPreview = hero.AddChild(new PreviewBox(), 0, 0, 374, 210);
            nowPreview.EmptyText = Loc.T("ov.preview.none");

            const int rx = 396, rw = 288;
            // The wallpaper's own name and path are deliberately NOT shown:
            // the preview beside them answers "what is showing", and a file
            // name and its full path belong in Explorer, not on this card. The
            // hint line that used to sit under the buttons went with them.
            string[] factKeys = { "ov.fact.pool", "ov.fact.total", "ov.fact.mode" };
            for (int i = 0; i < 3; i++)
            {
                factVal[i] = hero.AddChild(Txt(LabelStyle.StatNum), rx + i * 96, 52, 92, 22);
                factVal[i].Text = "0";
                factLab[i] = hero.AddChild(Txt(LabelStyle.Cap, true), rx + i * 96, 74, 92, 16);
                factLab[i].Text = Loc.T(factKeys[i]);
            }

            hero.AddChild(new Rule(), rx, 100, rw, 1);

            // Two equal buttons: "上一张" used to be a fixed 110px while
            // "下一张" took whatever was left, so the pair was visibly uneven.
            int btnW = (rw - Theme.GapCtl) / 2;
            btnPrev = hero.AddChild(new FlatButton(), rx, 118, btnW, Theme.BtnH);
            btnPrev.Kind = BtnKind.Secondary;
            btnPrev.Icon = IconKind.ArrowLeft;
            btnPrev.Click += delegate { PrevWallpaper(); };

            btnNext = hero.AddChild(new FlatButton(), rx + btnW + Theme.GapCtl, 118, btnW, Theme.BtnH);
            btnNext.Kind = BtnKind.Primary;
            btnNext.Icon = IconKind.ArrowRight;
            btnNext.IconTrailing = true;
            btnNext.Click += delegate { NextWallpaper(); };

            // Settings row: the three rotation knobs as editable dropdowns.
            // They used to be a read-only echo plus an "adjust" button that
            // jumped to a dedicated rotate page - one page for three choices
            // nobody needs to stare at. Changing them here writes straight to
            // the config, exactly like every other control in this window.
            stripRow = page.AddCard(74);
            stripRow.Pad = 13;
            stripRow.PadX = 16;
            BuildStripCombo(out cmbStyle, "ov.strip.style");
            cmbStyle.Items = Loc.StyleNames();
            cmbStyle.SelectedIndexChanged += delegate
            {
                if (loadingUi) return;
                ApplyFromUi();
                dirty = true;
                RefreshDirty();
            };

            BuildStripCombo(out cmbInterval, "ov.strip.interval");
            cmbInterval.Items = Loc.IntervalNames();
            cmbInterval.SelectedIndexChanged += delegate
            {
                if (loadingUi) return;
                ApplyFromUi();
                dirty = true;
                RefreshDirty();
                RestartTimer();
            };

            BuildStripCombo(out cmbOrder, "ov.strip.order");
            cmbOrder.Items = new string[] {
                Loc.T("ov.order.random"), Loc.T("ov.order.inorder") };
            cmbOrder.SelectedIndexChanged += delegate
            {
                if (loadingUi) return;
                ApplyFromUi();
                dirty = true;
                RefreshDirty();
            };
            stripRow.LaidOut += delegate { LayoutStrip(); };

            // Switch history. The rows come from the history + forward model,
            // not from a log of this run. The card's height follows the row
            // count (see SizeHistoryCard): three rows is the cap here.
            histCard = page.AddCard(Theme.CardPad * 2 + 47 * 3);
            histPage = page;
            histList = histCard.AddChild(new HistoryList(), 0, 0, 684, 47 * 3);
            histList.RowH = 47;
            histList.CurrentTag = Loc.T("hist.cur");
            histList.UndoableTag = Loc.T("hist.undoable");
            histList.BackTag = Loc.T("hist.back");
            histList.EmptyText = Loc.T("hist.empty");
            histList.ItemClicked += delegate { OnHistoryRowClicked(); };
        }

        // ".k" caption above a dropdown, one column per knob.
        private CardPanel stripRow;
        private readonly List<KitLabel> stripKeys = new List<KitLabel>();
        private readonly List<KitDropdown> stripCombos = new List<KitDropdown>();

        private void BuildStripCombo(out KitDropdown box, string captionKey)
        {
            KitLabel k = stripRow.AddChild(Txt(LabelStyle.Cap, true), 0, 1, 180, 17);
            k.Text = Loc.T(captionKey);
            stripKeys.Add(k);
            box = stripRow.AddChild(new KitDropdown(), 0, 18, 180, Theme.InputH);
            stripCombos.Add(box);
        }

        // Three equal columns separated by equal gaps - the prototype's flex
        // row, with each column holding one caption + one dropdown.
        private void LayoutStrip()
        {
            if (stripRow == null || stripCombos.Count < 3) return;
            Rectangle b = stripRow.Body;
            if (b.Width <= 0) return;

            int gap = Gfx.S(this, 16);
            int colW = Math.Max(Gfx.S(this, 120), (b.Width - gap * 2) / 3);
            int x = b.Left;
            for (int i = 0; i < 3; i++)
            {
                int w = Math.Min(colW, Math.Max(0, b.Right - x));
                stripKeys[i].SetBounds(x, stripKeys[i].Top, w, stripKeys[i].Height);
                stripCombos[i].SetBounds(x, stripCombos[i].Top, w, stripCombos[i].Height);
                x += colW + gap;
            }
        }

        // ---- sources -------------------------------------------------------

        private void BuildSourcesPage()
        {
            PageStack page = NewPage("nav.sources", "src.page.note");

            btnAddFolder = page.Head.AddAction(new FlatButton(), 150);
            btnAddFolder.Kind = BtnKind.Secondary;
            btnAddFolder.Icon = IconKind.Plus;
            btnAddFolder.Click += delegate { AddSourceFolder(); };

            // The list card carries no height of its own worth speaking of: the
            // list reports what it needs and CardPanel.ContentHeight grows the
            // card to match, so adding a source makes the card taller instead
            // of hiding the last row.
            cardSrc = page.AddCard(80);
            cardSrc.Title = Loc.T("src.list.title");

            srcList = cardSrc.AddChild(new SourceList(), 0, 0, 684, 120);
            srcList.Changed += delegate { OnSourcesEdited(); };

            // Everything that is not the list: what the counts mean and the
            // bulk actions.
            CardPanel acts = page.AddCard(120);
            lblSrcListNote = acts.AddChild(Txt(LabelStyle.CardNote, true), 0, 0, 684, 36);
            lblSrcListNote.Wrap = true;
            lblSrcListNote.Text = Loc.T("src.footnote");
            acts.AddChild(new Rule(), 0, 46, 684, 1);

            btnManualPick = acts.AddChild(new FlatButton(), 0, 62, 210, Theme.BtnH);
            btnManualPick.Kind = BtnKind.Secondary;
            btnManualPick.Icon = IconKind.Grid;
            btnManualPick.Click += delegate { OpenManualPicker(); };

            btnAllOn = acts.AddChild(new FlatButton(), 300, 62, 120, Theme.BtnSmallH);
            btnAllOn.Kind = BtnKind.Ghost;
            btnAllOn.Compact = true;
            btnAllOn.Click += delegate { srcList.SetAll(true); OnSourcesEdited(); };

            btnAllOff = acts.AddChild(new FlatButton(), 428, 62, 120, Theme.BtnSmallH);
            btnAllOff.Kind = BtnKind.Ghost;
            btnAllOff.Compact = true;
            btnAllOff.Click += delegate { srcList.SetAll(false); OnSourcesEdited(); };

            btnRecount = acts.AddChild(new FlatButton(), 556, 62, 128, Theme.BtnSmallH);
            btnRecount.Kind = BtnKind.Ghost;
            btnRecount.Compact = true;
            btnRecount.Icon = IconKind.Rotate;
            btnRecount.Click += delegate { StartSourceCounts(true); };
        }

        // ---- general -------------------------------------------------------

        private void BuildGeneralPage()
        {
            PageStack page = NewPage("nav.general", "gen.page.note");

            CardPanel cardStart = page.AddCard(126);
            cardStart.Title = Loc.T("gen.startup.title");
            cardStart.Note = Loc.T("gen.startup.note");

            swAutoStart = cardStart.AddHeaderControl(new ToggleSwitch(), Theme.SwitchW, Theme.SwitchH);
            swAutoStart.CheckedChanged += delegate
            {
                if (loadingUi) return;
                ApplyFromUi();
                dirty = true;
                RefreshDirty();
                AutoStartHelper.SetAutoStart(Config.AutoStart);
            };

            cardStart.AddChild(new Rule(), 0, 0, 684, 1);

            lblAppTitle = cardStart.AddChild(Txt(LabelStyle.BodyBold), 0, 16, 440, 20);
            lblAppNote = cardStart.AddChild(Txt(LabelStyle.CardNote, true), 0, 38, 440, 36);
            lblAppNote.Wrap = true;

            SegmentedControl segTh = new SegmentedControl();
            segTh.Items = new string[] { Loc.T("gen.light"), Loc.T("gen.dark") };
            // Pinned right at the same width and x as the language dropdown
            // below it, so the card's right-hand controls line up in one
            // column instead of the segmented pill ending where the dropdown
            // starts.
            segTheme = cardStart.AddRightChild(segTh, 24, LangColW, 34);
            segTheme.SelectedIndexChanged += delegate
            {
                if (loadingUi) return;
                AppTheme want = segTheme.SelectedIndex == 1 ? AppTheme.Dark : AppTheme.Light;
                if (want == Theme.Current) return;
                SetThemeMode(want);
            };

            CardPanel cardLang = page.AddCard(84);
            cardLang.Title = Loc.T("gen.language.title");
            cardLang.Note = Loc.T("gen.language.note");

            cmbLang = cardLang.AddHeaderControl(new KitDropdown(), LangColW, Theme.InputH);
            cmbLang.Items = Loc.LanguageDisplayNames;
            // Leave "no selection": the real index comes from the saved
            // language in SyncLanguageCombo(). Selecting 0 unconditionally
            // made the box read 中文 even when the UI started up in English.
            cmbLang.SelectedIndexChanged += delegate
            {
                if (loadingUi) return;
                int i = cmbLang.SelectedIndex;
                if (i < 0 || i >= Loc.LanguageCodes.Length) return;
                ChangeLanguage(Loc.LanguageCodes[i]);
            };

            CardPanel cardHk = page.AddCard(184);
            cardHk.Title = Loc.T("rot.hotkeys.title");
            cardHk.Note = Loc.T("rot.hotkeys.note");

            lblHkNextName = cardHk.AddChild(Txt(LabelStyle.BodyBold), 0, 6, 320, 20);
            lblHkNext = cardHk.AddChild(Txt(LabelStyle.Cap, true), 0, 26, 320, 16);
            keyNext = cardHk.AddChild(new KeyCapButton(), 554, 6, 130, Theme.KbdH);
            keyNext.NoneText = Loc.T("main.hotkey.none");
            keyNext.CaptureText = Loc.T("rot.keycap.capture");
            keyNext.Captured += delegate
            {
                Config.Hotkey = keyNext.Value;
                Config.Save();
                ApplyHotkey();
            };

            cardHk.AddChild(new Rule(), 0, 50, 684, 1);

            lblHkPrevName = cardHk.AddChild(Txt(LabelStyle.BodyBold), 0, 60, 320, 20);
            lblHkPrev = cardHk.AddChild(Txt(LabelStyle.Cap, true), 0, 80, 320, 16);
            keyPrev = cardHk.AddChild(new KeyCapButton(), 554, 60, 130, Theme.KbdH);
            keyPrev.NoneText = Loc.T("main.hotkey.none");
            keyPrev.CaptureText = Loc.T("rot.keycap.capture");
            keyPrev.Captured += delegate
            {
                Config.HotkeyPrev = keyPrev.Value;
                Config.Save();
                ApplyHotkey();
            };

            CardPanel cardCfg = page.AddCard(156);
            cardCfg.Title = Loc.T("gen.config.title");

            btnOpenFolder = cardCfg.AddHeaderControl(new FlatButton(), 160, Theme.BtnSmallH);
            btnOpenFolder.Kind = BtnKind.Secondary;
            btnOpenFolder.Compact = true;
            btnOpenFolder.Click += delegate { OpenConfigFolder(); };

            lblCfgPath = cardCfg.AddChild(Txt(LabelStyle.MonoSm, true), 0, 4, 684, 18);
            cardCfg.AddChild(new Rule(), 0, 44, 684, 1);
            lblAbout = cardCfg.AddChild(Txt(LabelStyle.CardNote, true), 0, 58, 684, 18);
        }

        // Re-apply every user-visible string of this form (and the tray) in
        // the active language. Called once after the controls exist and
        // again whenever the user switches the language, so no restart is
        // needed. Combo selections survive the item rebuilds.
        private void ApplyTexts()
        {
            // ---- shell
            rail.Items = new string[] {
                Loc.T("nav.overview"), Loc.T("nav.sources"),
                Loc.T("nav.general") };
            rail.Icons = new IconKind[] {
                IconKind.Grid, IconKind.Folder, IconKind.Gear };
            rail.PauseText = rotateTimer != null && !rotateTimer.Enabled
                ? Loc.T("rail.resume") : Loc.T("rail.pause");
            rail.DotCaption = rotateTimer != null && rotateTimer.Enabled
                ? Loc.T("rail.rotating") : Loc.T("rail.paused");
            // The caption is NOT decided here. ApplyTexts runs before the
            // rotate timer exists, so a local "is the timer enabled" test
            // answers false and stamps the paused caption onto a card that
            // SyncRailState had already marked rotating - the card then read
            // "已暂停" while showing a countdown. SyncRailState owns it.
            SyncRailState();
            bar.TitleText = Loc.T("app.name");
            bar.VersionText = "v" + Application.ProductVersion;
            bar.HelpText = Loc.T("win.help");
            footer.DirtyText = Loc.T("sb.dirty");
            footer.SaveText = Loc.T("main.btn.save");

            // ---- overview
            nowPreview.Pill = Loc.T("ov.now.showing");
            nowPreview.EmptyText = Loc.T("ov.preview.none");
            btnPrev.Text = Loc.T("ov.prev");
            btnNext.Text = Loc.T("ov.next");
            string[] factKeys = { "ov.fact.pool", "ov.fact.total", "ov.fact.mode" };
            for (int i = 0; i < 3; i++) factLab[i].Text = Loc.T(factKeys[i]);
            histList.CurrentTag = Loc.T("hist.cur");
            histList.UndoableTag = Loc.T("hist.undoable");
            histList.BackTag = Loc.T("hist.back");
            histList.EmptyText = Loc.T("hist.empty");
            RebuildHistory();
            RefreshStrip();

            // ---- sources
            btnAddFolder.Text = Loc.T("src.add.folder");
            btnManualPick.Text = Loc.T("src.manual");
            btnAllOn.Text = Loc.T("src.allon");
            btnAllOff.Text = Loc.T("src.alloff");
            btnRecount.Text = Loc.T("src.refresh");
            lblSrcListNote.Text = Loc.T("src.footnote");
            srcList.EmptyText = Loc.T("src.empty");
            srcList.PendingText = Loc.T("src.count.pending");
            srcList.UnavailableText = Loc.T("src.count.unavailable");
            RefreshSourceSummary();

            // ---- overview settings row (language may have just changed)
            ReloadComboItems(cmbStyle, Loc.StyleNames());
            ReloadComboItems(cmbInterval, Loc.IntervalNames());
            ReloadComboItems(cmbOrder, new string[] {
                Loc.T("ov.order.random"), Loc.T("ov.order.inorder") });
            RefreshStripCaptions();
            RefreshStrip();

            // ---- hotkeys
            lblHkNextName.Text = Loc.T("main.btn.next");
            lblHkPrevName.Text = Loc.T("main.btn.prev");
            lblHkNext.Text = Loc.T("rot.hk.note.next");
            lblHkPrev.Text = Loc.T("rot.hk.note.prev");
            keyNext.NoneText = Loc.T("main.hotkey.none");
            keyPrev.NoneText = Loc.T("main.hotkey.none");
            keyNext.CaptureText = Loc.T("rot.keycap.capture");
            keyPrev.CaptureText = Loc.T("rot.keycap.capture");

            // ---- general
            lblAppTitle.Text = Loc.T("gen.appearance");
            lblAppNote.Text = Loc.T("gen.appearance.note");
            SetSegItems(segTheme, new string[] { Loc.T("gen.light"), Loc.T("gen.dark") });
            btnOpenFolder.Text = Loc.T("gen.open.folder");
            lblCfgPath.Text = ConfigPathText();
            lblAbout.Text = Loc.F("gen.about.line", Application.ProductVersion, SupportedFormats());

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

        // The bindings are not shown on the overview any more: they are edited
        // on the general page, which is where a person looks for them, and the
        // hint line under the two buttons was noise on a card that exists to
        // show what is on the screen right now.
        private string KbdHintText()
        {
            return Loc.F("ov.kbd.hint",
                KeyName(Config.Hotkey, 9), KeyName(Config.HotkeyPrev, 8));
        }

        private static string KeyName(int digit, int fallback)
        {
            int d = digit >= 0 ? digit : fallback;
            return "Ctrl+" + d;
        }

        // Replace a SegmentedControl's items while keeping the selection.
        // The control can change row count when its labels change language,
        // so the caller is expected to re-measure afterwards.
        private static void SetSegItems(SegmentedControl seg, string[] items)
        {
            if (seg == null) return;
            int sel = seg.SelectedIndex;
            seg.Items = items;
            if (sel >= 0 && sel < items.Length) seg.SelectedIndex = sel;
        }

        private static string ConfigPathText()
        {
            try
            {
                return Path.Combine(AppPaths.DataDir, "WallpaperChanger.ini");
            }
            catch
            {
                return "";
            }
        }

        private static string SupportedFormats()
        {
            return "jpg / png / jfif / bmp / webp / gif / tiff";
        }

        // Switch the whole UI language at runtime: update Loc, remember it
        // in the config and persist right away (a language choice is an
        // unambiguous one-click decision, no separate "save" needed).
        // Point the language box at the language that is actually in effect.
        // Guarded so the resulting SelectedIndexChanged does not re-enter
        // ChangeLanguage (which would re-save the config on startup).
        private void SyncLanguageCombo()
        {
            if (cmbLang == null) return;
            int want = 0;
            for (int i = 0; i < Loc.LanguageCodes.Length; i++)
            {
                if (string.Equals(Loc.LanguageCodes[i], Loc.Language, StringComparison.OrdinalIgnoreCase))
                {
                    want = i;
                    break;
                }
            }
            if (cmbLang.SelectedIndex == want) return;
            bool prev = loadingUi;
            loadingUi = true;
            try { cmbLang.SelectedIndex = want; }
            finally { loadingUi = prev; }
        }

        private void ChangeLanguage(string lang)
        {
            Loc.SetLanguage(lang);
            Config.Language = lang;
            Config.Save();
            // A notice is a finished sentence in whatever language was active
            // when it appeared; there is no renderer to re-run, so it would
            // keep sitting there in the old language. Dropping it beats a
            // half-translated footer.
            ClearNotice();
            SyncLanguageCombo();
            ApplyTexts();
        }

        // Set the colour scheme and remember it right away - like the
        // language, a theme click is an unambiguous one-click decision, so it
        // does not wait for the save button.
        private void SetThemeMode(AppTheme next)
        {
            Theme.Set(next);
            Config.ThemeMode = next;
            Config.Save();
            ApplyTheme();
            SyncThemeSeg();
        }

        // Point the appearance segments at the theme actually in effect
        // without re-entering the change handler.
        private void SyncThemeSeg()
        {
            if (segTheme == null) return;
            int want = Theme.IsDark ? 1 : 0;
            if (segTheme.SelectedIndex == want) return;
            bool prev = loadingUi;
            loadingUi = true;
            try { segTheme.SelectedIndex = want; }
            finally { loadingUi = prev; }
        }

        // Repaint this window (and the tray menu) from the current palette.
        private void ApplyTheme()
        {
            Theme.ApplyTo(this);
            Theme.ApplyMenuStrip(trayMenu);
            Theme.SetTitleBar(this);
            // The frame line follows the palette, so a theme switch repaints it
            // too instead of leaving a light-theme line on a dark window.
            if (IsHandleCreated) ApplyFrameBorderColor();
            Invalidate(true);
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

        // "Add folder" straight from the sources page. The list on that page is
        // the editor now, so this only appends a row; the footer's save button
        // is what writes it to disk, like every other setting.
        private void AddSourceFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = Loc.T("dialog.pickfolder");
                foreach (string f in srcList.Sources)
                {
                    if (Directory.Exists(f)) { dlg.SelectedPath = f; break; }
                }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string folder = dlg.SelectedPath;
                if (!srcList.AddSource(folder))
                {
                    SetStatus(delegate { return Loc.T("status.folder.dup"); });
                    return;
                }
                OnSourcesEdited();
                StartSourceCounts(false);
                SetStatus(delegate { return Loc.F("status.folder.added", folder); });
            }
        }

        // A row was toggled or removed. The list is the working copy, so there
        // is nothing to read back yet - only something to save.
        private void OnSourcesEdited()
        {
            if (loadingUi) return;
            dirty = true;
            RefreshDirty();
            RefreshSourceSummary();
        }

        // Count the wallpapers in every source that does not have a count yet,
        // one folder at a time on a background thread. force clears the known
        // counts first, so the "recount" button re-reads the folders that were
        // scanned when the window opened.
        private void StartSourceCounts(bool force)
        {
            if (srcList == null) return;
            if (force) srcList.ClearCounts();

            List<string> todo = new List<string>();
            foreach (string f in srcList.Sources)
            {
                if (srcList.IsPending(f)) todo.Add(f);
            }
            RefreshSourceSummary();
            if (todo.Count == 0) return;

            bool recursive = Config.Recursive;
            int gen = ++countGeneration;
            Task.Run(delegate
            {
                foreach (string f in todo)
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
                    SafeUi(delegate
                    {
                        if (gen != countGeneration || srcList == null) return;
                        srcList.SetCount(f, val);
                        RefreshSourceSummary();
                    });
                }
            });
        }

        // Reveal the folder holding the ini and the logs.
        private void OpenConfigFolder()
        {
            try
            {
                string dir = AppPaths.DataDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                SetStatus(delegate { return Loc.F("status.error", ex.Message); });
            }
        }

        // Save from the status bar. The status bar is the only place the save
        // action lives now, so it also owns clearing the dirty flag.
        private void SaveFromFooter()
        {
            bool hadValid = HasValidFolders();
            SaveFromUi();
            dirty = false;
            RefreshDirty();
            SetStatus(delegate { return Loc.T("status.saved"); });
            // A source may have been added or switched back on, and the list on
            // the sources page is only collected at save time - so this is
            // where a new pool starts being used. Counts are re-read for the
            // rows that have never been scanned.
            RestartTimer();
            StartSourceCounts(false);
            RefreshSourceSummary();
            if (!hadValid && HasValidFolders()) NextWallpaper();
            notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", Loc.T("status.saved"), ToolTipIcon.Info);
        }

        // Primary-action mutex: while there is nothing to save, the save
        // button steps back and the page's own primary action (下一张壁纸)
        // stays the visual anchor. Once something is dirty the two swap.
        private void RefreshDirty()
        {
            if (footer != null)
            {
                footer.DirtyText = Loc.T("sb.dirty");
                footer.SaveText = Loc.T("main.btn.save");
                footer.Dirty = dirty;
            }
            if (btnNext != null)
            {
                btnNext.Kind = dirty ? BtnKind.Secondary : BtnKind.Primary;
            }
        }

        // The overview echo of the three rotation settings.
        // Push the current config into the three overview dropdowns. Called
        // on load and after the language changes; the loadingUi guard keeps
        // the SelectedIndexChanged handlers from writing back mid-sync.
        private void RefreshStrip()
        {
            if (cmbStyle == null || cmbInterval == null || cmbOrder == null) return;
            string[] names = Loc.StyleNames();
            int si = Math.Min(Math.Max((int)Config.Style, 0), names.Length - 1);
            if (cmbStyle.SelectedIndex != si) cmbStyle.SelectedIndex = si;

            int ii = IndexOfInterval(Config.IntervalMinutes);
            if (ii < 0) ii = 2;
            if (cmbInterval.SelectedIndex != ii) cmbInterval.SelectedIndex = ii;

            int oi = Config.RandomOrder ? 0 : 1;
            if (cmbOrder.SelectedIndex != oi) cmbOrder.SelectedIndex = oi;
        }

        // Rebuild a dropdown's items for a new language, keeping whichever
        // item was selected (by index) selected.
        private void ReloadComboItems(KitDropdown box, string[] items)
        {
            if (box == null) return;
            int sel = box.SelectedIndex;
            box.Items = items;
            if (items.Length == 0) return;
            if (sel < 0 || sel >= items.Length) sel = 0;
            if (box.SelectedIndex != sel) box.SelectedIndex = sel;
        }

        // The three captions above the dropdowns, re-read in the new language.
        private void RefreshStripCaptions()
        {
            if (stripKeys.Count < 3) return;
            stripKeys[0].Text = Loc.T("ov.strip.style");
            stripKeys[1].Text = Loc.T("ov.strip.interval");
            stripKeys[2].Text = Loc.T("ov.strip.order");
            stripRow?.Relayout();
        }

        // ---- "now showing" card ---------------------------------------------

        private readonly HashSet<string> previewRequested =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string lastPreviewPath;

        private void RefreshNowCard()
        {
            if (nowPreview == null) return;

            string cur = history.Count > 0 ? history[history.Count - 1] : null;
            bool has = !string.IsNullOrEmpty(cur);
            bool manual = Config.ManualPicked.Count > 0;

            factVal[0].Text = (manual ? Config.ManualPicked.Count : lastTotal).ToString();
            factVal[1].Text = lastTotal.ToString();
            factVal[2].Text = manual ? Loc.T("ov.mode.manual") : Loc.T("ov.mode.all");

            if (!has)
            {
                nowPreview.Image = null;
                lastPreviewPath = null;
                return;
            }
            if (string.Equals(cur, lastPreviewPath, StringComparison.OrdinalIgnoreCase)) return;
            lastPreviewPath = cur;
            nowPreview.Image = PreviewThumb(cur);
        }

        // The preview needs a 16:9-ish thumbnail at the box's own size. One
        // is generated on a background thread the first time a wallpaper is
        // shown, then it comes straight from the disk cache.
        private Image PreviewThumb(string path)
        {
            Bitmap b = ThumbCache.Get(path, 374, 210);
            if (b != null) return b;
            if (previewRequested.Add(path))
            {
                Task.Run(delegate
                {
                    try { ThumbCache.Generate(path, 374, 210); }
                    catch { }
                    SafeUi(delegate
                    {
                        lastPreviewPath = null;
                        RefreshNowCard();
                    });
                });
            }
            return null;
        }

        // Live echo on the sources card header: how many sources exist, how
        // many are on, and how many images they hold in total. It reads the
        // list, not the config, because the list is what the user is editing
        // before the save button writes anything.
        private void RefreshSourceSummary()
        {
            if (cardSrc == null || srcList == null) return;
            int total = srcList.SourceCount;
            if (total == 0)
            {
                cardSrc.Note = Loc.T("main.source.summary.none");
                SyncOverviewFacts();
                return;
            }
            int off = 0;
            long sum = 0;
            bool pending = false;
            foreach (string f in srcList.Sources)
            {
                bool on = srcList.IsOn(f);
                if (!on) off++;
                if (srcList.IsPending(f)) { pending = true; continue; }
                int n = srcList.CountOf(f);
                if (n >= 0 && on) sum += n;
            }
            cardSrc.Note = Loc.F("src.summary", total, total - off, off,
                pending ? Loc.T("src.count.pending") : sum.ToString());
            // The overview's own numbers come from the same counts, so they are
            // real as soon as the folders have been scanned instead of staying
            // "0 / 0" until a wallpaper has actually been applied.
            SyncOverviewFacts();
        }

        // "参与轮换" / "扫描到图片" on the now-showing card, derived from the
        // source list rather than from the last rotation attempt. Zero was the
        // honest answer to "how many did the last pick scan" but a dishonest
        // answer to "how many pictures do I have", which is what the card is
        // read as - a folder with 193 images in it showed 0.
        private void SyncOverviewFacts()
        {
            if (srcList == null || factVal[0] == null) return;
            if (Config.ManualPicked.Count > 0)
            {
                factVal[0].Text = Config.ManualPicked.Count.ToString();
                factVal[1].Text = SourceTotals().ToString();
                factVal[2].Text = Loc.T("ov.mode.manual");
                return;
            }
            long enabled = 0, all = 0;
            SourceTotals(out enabled, out all);
            factVal[0].Text = enabled.ToString();
            factVal[1].Text = all.ToString();
            factVal[2].Text = Loc.T("ov.mode.all");
        }

        private long SourceTotals()
        {
            long enabled = 0, all = 0;
            SourceTotals(out enabled, out all);
            return all;
        }

        private void SourceTotals(out long enabled, out long all)
        {
            enabled = 0;
            all = 0;
            if (srcList == null) return;
            foreach (string f in srcList.Sources)
            {
                if (srcList.IsPending(f)) continue;
                int n = srcList.CountOf(f);
                if (n < 0) continue;
                all += n;
                if (srcList.IsOn(f)) enabled += n;
            }
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

        // Which configured source a wallpaper came from, for the history
        // rows. Falls back to a neutral label rather than showing a path.
        private string SourceOf(string file)
        {
            try
            {
                string full = Path.GetFullPath(file);
                string best = null;
                foreach (string f in Config.Folders)
                {
                    string root;
                    try { root = Path.GetFullPath(f); }
                    catch { continue; }
                    if (full.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        if (best == null || root.Length > best.Length) best = f;
                    }
                }
                if (best != null) return SourceName(best);
            }
            catch
            {
            }
            return Loc.T("hist.unknown.src");
        }

        private void LoadSettingsIntoUi()
        {
            HashSet<string> off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in Config.DisabledFolders)
            {
                if (d != null) off.Add(d.Trim());
            }
            srcList.SetSources(new List<string>(Config.Folders), off);
            RefreshSourceSummary();
            SyncLanguageCombo();
            RefreshStrip();
            swAutoStart.Checked = Config.AutoStart;
            keyNext.Value = (Config.Hotkey >= 0 && Config.Hotkey <= 9) ? Config.Hotkey : -1;
            keyPrev.Value = (Config.HotkeyPrev >= 0 && Config.HotkeyPrev <= 9) ? Config.HotkeyPrev : -1;
            SyncThemeSeg();
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
            // The source list is edited in place on its page; collecting it
            // here is what makes the save button the single write point.
            List<string> folders = new List<string>();
            foreach (string f in srcList.Sources) folders.Add(f);
            Config.Folders = folders;
            Config.DisabledFolders = srcList.DisabledList();

            Config.Style = (WallpaperStyle)Math.Max(0, cmbStyle.SelectedIndex);
            Config.IntervalMinutes = IntervalFromIndex(cmbInterval.SelectedIndex);
            Config.RandomOrder = cmbOrder.SelectedIndex == 0;
            Config.AutoStart = swAutoStart.Checked;
            // Hotkeys are written straight to Config by the key caps on
            // commit, so there is nothing to collect for them here.
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
            swAutoStart.Checked = AutoStartHelper.AutoStartExists();
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
                nextSwitchAt = DateTime.Now + TimeSpan.FromMilliseconds(rotateTimer.Interval);
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
                SetStatus(delegate { return Loc.T("status.rotate.paused"); });
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", Loc.T("status.paused"), ToolTipIcon.Info);
            }
            else
            {
                // Resuming starts a fresh full interval - the time spent
                // paused should not count towards the next switch.
                rotateTimer.Interval = Config.IntervalMinutes * 60000;
                nextSwitchAt = DateTime.Now + TimeSpan.FromMilliseconds(rotateTimer.Interval);
                rotateTimer.Start();
                miPause.Text = Loc.T("tray.pause");
                SetStatus(delegate { return Loc.T("status.rotate.resumed"); });
                notifyIcon.ShowBalloonTip(1200, "WallpaperChanger", Loc.T("status.rotate.resumed"), ToolTipIcon.Info);
            }
            SyncRailState();
        }

        // The rail's status card mirrors the rotate timer: dot colour, the
        // caption next to it, the pause button and its own countdown.
        private void SyncRailState()
        {
            if (rail == null || rotateTimer == null) return;
            bool on = rotateTimer.Enabled;
            // Everything about the card is set in this one method: the button's
            // label and its colour. The card has no other content - the old
            // dot + caption + countdown + caption stack said the same thing
            // three ways and could disagree with itself; the two properties
            // below are still kept in step for the footer's sake.
            rail.Rotating = on;
            rail.Countdown = on ? CountdownText() : "";
            rail.DotCaption = on ? Loc.T("rail.rotating") : Loc.T("rail.paused");
            // The label names the state the rotation is in, which is what the
            // user asked to see on the one big button.
            rail.PauseText = on ? Loc.T("rail.rotating") : Loc.T("rail.paused");
            rail.CountdownCaption = on ? Loc.T("rail.next") : Loc.T("rail.paused.hint");
            rail.ButtonKind = on ? BtnKind.Primary : BtnKind.Secondary;
            rail.Invalidate();
        }

        private void ShowWindow()
        {
            allowVisible = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        // A second launch of the exe asks the running copy to surface through
        // a named event; this is what that request ends up calling.
        public void ShowFromTray()
        {
            ShowWindow();
        }

        // Launched from the Startup shortcut: swallow the initial show request
        // and live in the tray instead. The handle is still created so the
        // timers, hotkeys and the show-request listener all work normally.
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !allowVisible)
            {
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
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
            RefreshStatusLine();
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
                SetStatus(delegate { return Loc.T("status.novalidfolder"); });
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
                            string n1 = name; int t1 = total;
                            SetStatus(delegate { return Loc.F("status.current", n1, t1) + ModeTag(); });
                        }
                        else
                        {
                            Log.Write("redo apply failed: " + path);
                            string n2 = name;
                            SetStatus(delegate { return Loc.F("status.applyfail", n2); });
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
                    string n3 = Path.GetFileName(path); int t3 = total;
                    SetStatus(delegate { return Loc.F("status.current", n3, t3) + ModeTag(); });
                }
                else
                {
                    Log.Write("apply failed: " + path);
                    string n4 = Path.GetFileName(path);
                    SetStatus(delegate { return Loc.F("status.applyfail", n4); });
                }
            }
            catch (Exception ex)
            {
                Log.Write("apply error: " + ex.Message);
                string em = ex.Message;
                SetStatus(delegate { return Loc.F("status.error", em); });
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
            RebuildHistory();
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
            RebuildHistory();
        }

        // ---- history timeline ----------------------------------------------

        // The design wants one list containing the whole trail, with the
        // current entry inside it. The model behind it is still history +
        // forward: history runs oldest -> newest and ends at the current
        // wallpaper, and forward (reversed) continues past it. So the
        // timeline is history ++ reverse(forward) and the current position is
        // always history.Count - 1.
        private List<string> Timeline()
        {
            List<string> t = new List<string>(history);
            for (int i = forward.Count - 1; i >= 0; i--) t.Add(forward[i]);
            return t;
        }

        private int histHi = -1;   // timeline index shown in the first row

        private void RebuildHistory()
        {
            if (histList == null) return;
            List<string> t = Timeline();
            int cur = history.Count - 1;

            if (t.Count == 0)
            {
                histHi = -1;
                histList.SetRows(null, null, null, -1);
                SizeHistoryCard(1);   // one row's height for the "nothing yet" line
                return;
            }

            int lo = Math.Max(0, cur - 2);
            int hi = Math.Min(t.Count - 1, cur + 2);
            int rows = hi - lo + 1;
            string[] names = new string[rows];
            string[] metas = new string[rows];
            Image[] thumbs = new Image[rows];
            int dispCur = -1;

            for (int r = 0; r < rows; r++)
            {
                int ti = hi - r;                       // newest first
                string p = t[ti];
                names[r] = Path.GetFileName(p);
                string tag = ti == cur ? Loc.T("hist.cur")
                    : (ti > cur ? Loc.T("hist.undoable") : Loc.T("hist.seen"));
                metas[r] = tag + " · " + SourceOf(p);
                thumbs[r] = ThumbCache.Get(p, 52, 29);
                if (ti == cur) dispCur = r;
            }

            histHi = hi;
            histList.SetRows(names, metas, thumbs, dispCur);
            SizeHistoryCard(rows);
            RequestMissingThumbs(t);
        }

        // The history card follows its row count (see the HistoryList case in
        // CardPanel.ApplyPlacements): a fixed 148px card for three 47px rows
        // sliced the last one off at the card's own bottom edge, and a fixed
        // three-row card for an empty list left 110px of blank surface - the
        // "空白行" a person points at. Three rows is the cap; RebuildHistory
        // never hands the list more than that.
        private void SizeHistoryCard(int rows)
        {
            if (histCard == null || histPage == null) return;
            int show = Math.Max(1, Math.Min(3, rows));
            histList.Height = Gfx.S(this, 47 * show);
            histPage.SetCardHeight(histCard, Theme.CardPad * 2 + 47 * show);
        }

        // Thumbnails are generated on a background thread; when one lands the
        // list is rebuilt once. The set of already-requested paths keeps that
        // from becoming a rebuild loop when a file cannot be decoded.
        private readonly HashSet<string> thumbRequested =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void RequestMissingThumbs(List<string> timeline)
        {
            List<string> missing = new List<string>();
            foreach (string p in timeline)
            {
                if (thumbRequested.Contains(p)) continue;
                if (ThumbCache.Get(p, 52, 29) != null) continue;
                thumbRequested.Add(p);
                missing.Add(p);
            }
            if (missing.Count == 0) return;

            Task.Run(delegate
            {
                foreach (string p in missing)
                {
                    try { ThumbCache.Generate(p, 52, 29); }
                    catch { }
                }
                SafeUi(delegate { RebuildHistory(); });
            });
        }

        private void OnHistoryRowClicked()
        {
            if (histHi < 0) return;
            int row = histList.ClickedIndex;
            if (row < 0) return;
            JumpToTimeline(histHi - row);
        }

        // Walk the timeline to an arbitrary entry in one step. Going back
        // parks everything above the target on the forward stack (so "next"
        // still redo-restores it); going forward consumes that stack.
        private void JumpToTimeline(int target)
        {
            if (busy) return;
            List<string> t = Timeline();
            int cur = history.Count - 1;
            if (target < 0 || target >= t.Count || target == cur) return;
            string path = t[target];

            busy = true;
            Task.Run(delegate
            {
                bool ok = false;
                try { ok = WallpaperEngine.Apply(path, Config.Style); }
                catch { ok = false; }
                string name = Path.GetFileName(path);
                SafeUi(delegate
                {
                    try
                    {
                        if (ok)
                        {
                            if (target < cur)
                            {
                                // Walking back: the nearest departure has to
                                // end up on top of the stack, so push from
                                // the current end downwards.
                                for (int k = cur; k > target; k--) forward.Add(t[k]);
                                history.RemoveRange(target + 1, history.Count - (target + 1));
                            }
                            else
                            {
                                for (int k = cur + 1; k <= target; k++) history.Add(t[k]);
                                int drop = target - cur;
                                if (drop > 0 && forward.Count >= drop)
                                    forward.RemoveRange(forward.Count - drop, drop);
                            }
                            lastApplied = path;
                            RebuildHistory();
                            SetStatus(delegate { return Loc.F("status.current", name, lastTotal) + ModeTag(); });
                        }
                        else
                        {
                            SetStatus(delegate { return Loc.F("status.applyfail", name); });
                        }
                    }
                    finally
                    {
                        busy = false;
                    }
                });
            });
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
                SetStatus(delegate { return Loc.T("status.noprev"); });
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
                            string n5 = name;
                            SetStatus(delegate { return Loc.F("status.current.prev", n5) + ModeTag(); });
                        }
                        else
                        {
                            string n6 = name;
                            SetStatus(delegate { return Loc.F("status.prevfail", n6); });
                        }
                    }
                    finally
                    {
                        busy = false;
                    }
                });
            });
        }

        // A one-shot message. It goes to the footer's notice slot rather than
        // into the status line, so what the user reads under the pages keeps
        // describing the app ("next switch: 22:40") and does not get replaced
        // for the rest of the session by whatever happened once.
        //
        // Severity is inferred from the text rather than passed in: the call
        // sites are spread over two dozen places and every one of them already
        // carries a localised message that says which it is.
        private void SetStatus(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            ShowNotice(line, LooksLikeError(line));
        }

        private void SetStatus(Func<string> render)
        {
            if (render == null) return;
            SetStatus(render() ?? "");
        }

        private static bool LooksLikeError(string s)
        {
            string[] marks = {
                "！", "!", "失败", "错误", "无法", "没有可用", "重复",
                "failed", "error", "cannot", "no pictures", "already",
                "エラー", "できません", "失敗"
            };
            foreach (string mk in marks)
            {
                if (s.IndexOf(mk, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // A message that is not the status: "no pictures in any folder",
        // "that hotkey is taken". It gets its own slot in the footer and
        // clears itself, so the line that carries the current wallpaper is
        // never overwritten by something that happened once.
        private void ShowNotice(string text, bool isError)
        {
            if (footer == null || string.IsNullOrEmpty(text)) return;
            footer.Notice = text;
            footer.NoticeIsError = isError;
            noticeTimer.Stop();
            noticeTimer.Start();
        }

        private void ClearNotice()
        {
            noticeTimer.Stop();
            if (footer == null) return;
            footer.Notice = "";
            // The status line was never replaced, so it only has to be redrawn
            // once the notice stops taking the space next to it.
            RenderStatus();
        }

        private void RenderStatus()
        {
            if (footer == null) return;

            string first = NextSwitchText();

            footer.MainText = first;
            footer.SubText = SubStatusText();
            footer.Healthy = rotateTimer != null && rotateTimer.Enabled;
            footer.Dirty = dirty;
            RefreshNowCard();

            // The rail follows the same state. It used to set `Rotating` here
            // on its own, without the caption that goes with it, so the card
            // could show a countdown under the paused caption.
            SyncRailState();
        }

        // The moment the next automatic switch fires. A 24h interval shows
        // tomorrow's time - that is genuinely when it fires. A bare "21:00"
        // reads as "now" when the clock says 21:00, so a target that is not
        // today carries its day with it.
        private string CountdownText()
        {
            DateTime at = nextSwitchAt == DateTime.MinValue
                ? DateTime.Now + TimeSpan.FromMilliseconds(rotateTimer.Interval)
                : nextSwitchAt;
            return DayPrefix(at) + at.ToString("HH:mm");
        }

        // "明天 " /  "tomorrow " / "明日 " when the target lands on another
        // day, "" while it is still today. Compared on dates, not on a
        // 24-hour delta, so a switch at 23:50 with a 20-minute interval does
        // not get tagged as tomorrow.
        private static string DayPrefix(DateTime at)
        {
            int days = (at.Date - DateTime.Now.Date).Days;
            if (days <= 0) return "";
            if (days == 1) return Loc.T("time.tomorrow") + " ";
            if (days == 2) return Loc.T("time.dayafter") + " ";
            return Loc.F("time.indays", days) + " ";
        }

        // One-second pulse: move the rail's big clock and the status line's
        // "next switch" readout towards the fixed target time.
        private void RefreshCountdown()
        {
            if (rail != null && rotateTimer != null && rotateTimer.Enabled)
            {
                rail.Countdown = CountdownText();
            }
            // Ungated: the footer also has to be able to say "rotation paused"
            // and to move from "22:40" to "tomorrow 22:40" while paused.
            if (footer != null && footer.Notice.Length == 0)
            {
                footer.MainText = NextSwitchText();
            }
        }

        // The second run in the status bar: which pool is being rotated.
        private string SubStatusText()
        {
            if (Config.ManualPicked.Count > 0) return Loc.F("sb.manual", Config.ManualPicked.Count);
            return Loc.T("sb.all");
        }

        // Re-render both lines, so the first one follows the current language.
        private void RefreshStatusLine()
        {
            RenderStatus();
        }

        // The focused key cap swallows this: Ctrl+9 has to be bindable
        // without also switching the wallpaper while it is being bound.
        private bool SuppressHotkeys
        {
            get { return KeyCapButton.AnyCapturing; }
        }

        private string NextSwitchText()
        {
            // rotateTimer is created after BuildUi/BuildTray, and ApplyTexts
            // runs inside BuildTray, so it can still be null here.
            if (rotateTimer != null && rotateTimer.Enabled)
            {
                DateTime at = nextSwitchAt == DateTime.MinValue
                    ? DateTime.Now + TimeSpan.FromMilliseconds(rotateTimer.Interval)
                    : nextSwitchAt;
                // Seconds tick every frame and would make the status bar the
                // loudest thing on screen; the rail's big clock is the one
                // that counts down. Same day prefix as the rail, so a 24h
                // interval does not read as "right now".
                return Loc.F("status.nextswitch", DayPrefix(at) + at.ToString("HH:mm"));
            }
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
            // Closing for real: remember the window size the user settled on
            // so the next launch reopens the same way.
            CaptureWindowSize();
            Config.Save();
            base.OnFormClosing(e);
        }

        // Persist the size in 96-DPI logical units. The raw pixels of a 150%
        // monitor would reopen far too large on a 100% one. A maximised or
        // minimised window is not a size the user chose, so it is ignored and
        // the previously stored one survives.
        private void CaptureWindowSize()
        {
            if (WindowState != FormWindowState.Normal) return;
            int dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            int w = (int)Math.Round(Width * 96.0 / dpi);
            int h = (int)Math.Round(Height * 96.0 / dpi);
            if (w <= 0 || h <= 0) return;
            Config.WindowWidth = w;
            Config.WindowHeight = h;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySavedWindowSize();
            if (bar != null) bar.SyncWindowState(WindowState);
            LayoutChrome();
        }

        // Runs after the handle exists, so the auto-scaled Size is already in
        // physical pixels and DeviceDpi is known. Clamping against the work
        // area matters because the window can be taller than the design and a
        // config copied from a 4K machine would otherwise open off screen.
        private void ApplySavedWindowSize()
        {
            if (Config.WindowWidth <= 0 || Config.WindowHeight <= 0) return;
            double s = (DeviceDpi > 0 ? DeviceDpi : 96) / 96.0;
            int w = Math.Max(Theme.WindowMinW, Config.WindowWidth);
            int h = Math.Max(Theme.WindowMinH, Config.WindowHeight);
            Size wanted = new Size((int)Math.Round(w * s), (int)Math.Round(h * s));
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            wanted.Width = Math.Max(Theme.WindowMinW, Math.Min(wanted.Width, wa.Width));
            wanted.Height = Math.Max(Theme.WindowMinH, Math.Min(wanted.Height, wa.Height));
            if (wanted == Size) return;
            Size = wanted;
            if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
        }

        // The handle is (re)created on show and on DPI changes - (re)register
        // the hotkey each time so it never goes stale.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Log.Write("ui: dpi=" + DeviceDpi + " scale=" + (DeviceDpi / 96f).ToString("0.00")
                + " client=" + ClientSize.Width + "x" + ClientSize.Height);
            Theme.SetTitleBar(this);
            if (hotkeyManager != null) ApplyHotkey();
            try
            {
                // Rounded corners for the borderless frame; older Windows
                // simply ignores this.
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref round, sizeof(int));
            }
            catch
            {
            }
            ApplyFrameBorderColor();
            UpdateMaximizedBounds();
            LayoutChrome();
        }

        // Windows 11 draws a 1px frame line around a borderless window - the
        // black (or accent-coloured) outline that reads as "程序四周的黑边框".
        // DWMWA_BORDER_COLOR suppresses it; painting it in the theme's own
        // background is the variant that also works on the builds where the
        // "no border" sentinel is ignored.
        private void ApplyFrameBorderColor()
        {
            try
            {
                int rgb = Theme.FormBack.R | (Theme.FormBack.G << 8) | (Theme.FormBack.B << 16);
                DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref rgb, sizeof(int));
            }
            catch
            {
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // The middle caption button shows a restore glyph once the window is
            // maximised, which is what every Windows title bar does.
            if (bar != null) bar.SyncWindowState(WindowState);
            // Belt and braces: nothing in the UI can request maximise any more
            // (no button, no WS_MAXIMIZEBOX, no snap), but a programmatic
            // WindowState=Maximized would still go through. Assigning Normal
            // from inside OnResize re-enters the same state the setter is
            // still holding, so queue it: the very next message-loop turn
            // restores the window.
            if (WindowState == FormWindowState.Maximized && !unmaximizeQueued)
            {
                unmaximizeQueued = true;
                BeginInvoke(new Action(delegate
                {
                    unmaximizeQueued = false;
                    if (WindowState == FormWindowState.Maximized)
                    {
                        WindowState = FormWindowState.Normal;
                        LayoutChrome();
                    }
                }));
            }
            // The chrome is laid out against the REAL client rectangle
            // (GetClientRect), which WinForms' ClientSize misreports by the
            // hidden frame on every resize - so re-derive it every time.
            LayoutChrome();

            if (WindowState == FormWindowState.Normal)
            {
                normalBounds = Bounds;
                // A borderless window owns its own maximised rectangle, so it
                // has to be re-derived from the monitor the window is actually
                // on. Left stale, pressing maximise throws the window back onto
                // the monitor it used to be on, which looks exactly like the
                // window disappearing.
                UpdateMaximizedBounds();
            }
            else if (WindowState == FormWindowState.Maximized)
            {
                KeepMaximizedOnScreen();
                // The client area just changed size behind WinForms' back
                // (WM_NCCALCSIZE answers 0), so ask for a full repaint instead
                // of trusting whatever happens to be on the surface.
                Invalidate(true);
            }

            if (lastState != WindowState)
            {
                lastState = WindowState;
                Log.Write("window: state=" + WindowState + " bounds=" + Bounds
                    + " dpi=" + DeviceDpi);
            }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (WindowState != FormWindowState.Normal) return;
            normalBounds = Bounds;
            UpdateMaximizedBounds();
        }

        // With WM_NCCALCSIZE answering 0 the client area equals the window
        // rect, so a maximised window has to be pinned to the work area or
        // the frame inflation would push the content off screen.
        private void UpdateMaximizedBounds()
        {
            Screen s = Screen.FromControl(this);
            MaximizedBounds = s.WorkingArea;
        }

        // Last line of defence for "pressing maximise made the window go away".
        // If the maximised rectangle did not land on the monitor the window came
        // from, put it there: a maximised window parked off screen is
        // indistinguishable from a crash to the person using it.
        private void KeepMaximizedOnScreen()
        {
            Rectangle anchor = normalBounds.Width > 0 ? normalBounds : RestoreBounds;
            if (anchor.Width <= 0 || anchor.Height <= 0) return;
            Rectangle want = Screen.FromRectangle(anchor).WorkingArea;

            RECT r;
            if (!GetWindowRect(Handle, out r)) return;
            Rectangle got = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (got == want) return;

            Log.Write("window: maximised to " + got + ", wanted " + want + " - correcting");
            MaximizedBounds = want;
            SetWindowPos(Handle, IntPtr.Zero, want.Left, want.Top, want.Width, want.Height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOCOPYBITS);
        }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_NOCOPYBITS = 0x0100;
        private const int SWP_FRAMECHANGED = 0x0020;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT r);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
            int x, int y, int cx, int cy, int flags);

        private int ResizeBorder
        {
            get { return (int)Math.Round(6.0 * DeviceDpi / 96.0); }
        }

        // Keep the system frame style alive so DWM still provides the shadow
        // and the resize edges, while the client area takes the whole window
        // (WM_NCCALCSIZE below). WS_MAXIMIZEBOX is cleared explicitly: the
        // window must not maximise, and clearing the bit is what makes the
        // window menu, snap-to-top and any WM_SYSCOMMAND path agree with the
        // missing title-bar button. OnResize still un-maximises as a backstop.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= WS_THICKFRAME | WS_MINIMIZEBOX;
                cp.Style &= ~WS_MAXIMIZEBOX;
                return cp;
            }
        }

        // System-wide hotkeys arrive as WM_HOTKEY regardless of focus.
        protected override void WndProc(ref Message m)
        {
            // The borderless frame needs these answered before anything else
            // looks at the message.
            HandleNcMessages(ref m);
            if (m.Msg == WM_NCCALCSIZE || m.Msg == WM_NCHITTEST) return;

            base.WndProc(ref m);
            if (hotkeyManager == null) return;
            // While a key cap is waiting for a combination, Ctrl+digit must
            // not also fire the wallpaper switch it is being bound away from.
            if (SuppressHotkeys) return;
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

        // ---- borderless frame ----------------------------------------------

        // An inactive borderless window asks for its frame to be redrawn when
        // it is activated or deactivated, and with the title bar gone the
        // repaint is what the user sees as a strip along the edges. Both
        // WM_NCACTIVATE and WM_NCPAINT are answered in HandleNcMessages below.
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;
        // Activation and non-client painting. WS_THICKFRAME is kept for DWM's
        // shadow, and that frame brings a non-client area which Windows paints
        // itself whenever the window becomes active or INACTIVE. With
        // WM_NCCALCSIZE answering 0 the client covers the whole window, so the
        // frame Windows paints on activation changes lands on top of our own
        // pixels - the strip that appears as soon as the window loses focus
        // ("点击一下桌面空白处就会出现白边", also after opening Help).
        private const int WM_NCACTIVATE = 0x0086;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_SETTEXT = 0x000C;
        private const int WM_ACTIVATE = 0x0006;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;

        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUND = 2;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, uint attr,
            ref int value, uint size);

        // Answer 0 so the client area covers the entire window; then hand the
        // outer few pixels back as resize borders, because the child controls
        // cover the whole client area and would otherwise eat the drag.
        private void HandleNcMessages(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            // Let the form run its own activation bookkeeping, then claim the
            // message so the default handler cannot paint a non-client frame
            // over the client area. Returning 1 means "already handled".
            if (m.Msg == WM_NCACTIVATE)
            {
                base.WndProc(ref m);
                m.Result = new IntPtr(1);
                return;
            }
            // Nothing may be painted in the non-client area: every pixel of
            // this window belongs to a child control.
            if (m.Msg == WM_NCPAINT)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result != HTCLIENT) return;
                if (WindowState == FormWindowState.Maximized) return;

                int lp = unchecked((int)(long)m.LParam);
                Point p = PointToClient(new Point(unchecked((short)(lp & 0xFFFF)),
                    unchecked((short)((lp >> 16) & 0xFFFF))));
                int b = ResizeBorder;
                bool left = p.X <= b;
                bool right = p.X >= ClientSize.Width - b;
                bool top = p.Y <= b;
                bool bottom = p.Y >= ClientSize.Height - b;
                int hit = 0;
                if (top && left) hit = HTTOPLEFT;
                else if (top && right) hit = HTTOPRIGHT;
                else if (bottom && left) hit = HTBOTTOMLEFT;
                else if (bottom && right) hit = HTBOTTOMRIGHT;
                else if (left) hit = HTLEFT;
                else if (right) hit = HTRIGHT;
                else if (top) hit = HTTOP;
                else if (bottom) hit = HTBOTTOM;
                if (hit != 0) m.Result = (IntPtr)hit;
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


