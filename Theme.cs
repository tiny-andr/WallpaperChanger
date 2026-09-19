using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperChanger
{
    public enum AppTheme { Light, Dark }

    // WinForms ships no dark mode, so the palette is applied by hand.
    //
    // Light  = "system default". Controls keep their native rendering and
    //          SystemColors, so the app looks exactly like it always did.
    // Dark   = forced palette. Native button / menu rendering cannot be
    //          tinted through BackColor, so in dark mode buttons switch to
    //          FlatStyle.Flat and menus get a custom renderer.
    //
    // A control can opt into a semantic colour by setting its Tag to one of
    // the Role* constants below; everything else gets the plain foreground.
    // Controls that implement IThemed paint themselves and get an
    // ApplyTheme() callback instead.
    //
    // Token values are NOT copied from the design handoff's sRGB table --
    // that table is wrong (accent off by 22/255, ok off by 28/255). They
    // were measured by reading the actually-rendered sRGB bytes out of the
    // prototype in Chromium. See design/redesign-plan.md section 2.
    public static class Theme
    {
        public const string RoleMuted = "wc:muted";        // secondary text on bg / surface
        public const string RoleMutedStrong = "wc:mutedstrong"; // secondary text on a stained plane
        public const string RoleAccent = "wc:accent";      // filled primary button
        public const string RoleAccentText = "wc:accenttext"; // coloured label text
        public const string RoleIcon = "wc:icon";          // fully owner-drawn button
        public const string RoleCard = "wc:card";          // surface card
        public const string RoleWarn = "wc:warn";          // danger / unavailable
        public const string RoleOk = "wc:ok";              // healthy / rotating

        // ---- geometry (logical px at 96 DPI, scale by DpiScale) ----------

        // The design places every child on a fixed 684px body inside a card,
        // so the window width is derived from that number rather than picked:
        //   684 + 2*CardPad(36) + 2*PadX(48) = 768      card + page margins
        //   768 + RailW(212) + 2*6(preview inset) = 992
        //   992 + 17(vertical scrollbar)          = 1009
        // 1016 leaves a few px of DPI-rounding slack on top of that. The old
        // 980 was 29px short of its own content, which squeezed the third
        // fact column and the "adjust" button down to zero width at the
        // minimum size. The layout is fixed-width, so the minimum is the
        // width the design actually needs.
        public const int WindowW = 1016;
        public const int WindowH = 668;
        public const int WindowMinW = 1016;
        public const int WindowMinH = 620;

        public const int TitleBarH = 46;
        public const int RailW = 212;
        public const int StatusBarH = 50;

        public const int PadTop = 22;
        public const int PadX = 24;
        public const int PadBottom = 26;
        public const int GapCard = 16;
        public const int CardPad = 18;
        public const int GapCtl = 10;

        public const int RadWindow = 12;
        public const int RadCard = 11;
        public const int RadRailCard = 10;
        public const int RadBtn = 8;
        public const int RadBtnSm = 7;
        public const int RadInput = 8;
        public const int RadSeg = 9;
        public const int RadSegItem = 6;
        public const int RadRailItem = 8;
        public const int RadMenu = 7;
        public const int RadTile = 9;
        public const int RadBadge = 6;
        public const int RadKbd = 5;

        public const int BtnH = 36;
        public const int BtnSmallH = 30;
        public const int InputH = 36;
        public const int SwitchW = 40;
        public const int SwitchH = 22;
        public const int SwitchKnob = 16;
        public const int RailItemH = 44;
        public const int SegItemH = 28;
        public const int KbdH = 34;
        public const int KbdMinW = 104;
        public const int BadgeSize = 19;
        public const int HelpTocItemH = 36;
        public const int MenuItemH = 36;
        public const int MenuW = 246;
        public const int WinBtnSize = 46;
        public const int HelpTocW = 190;

        // ---- font sizes (points at 96 DPI) -------------------------------

        public const float FsPreviewName = 20f;
        public const float FsPageTitle = 19f;
        public const float FsRailCount = 19f;
        public const float FsCardTitle = 13.5f;
        public const float FsBody = 13.5f;
        public const float FsBodySm = 13f;
        public const float FsSub = 12.5f;
        public const float FsCardNote = 12f;
        public const float FsCap = 11.5f;
        public const float FsMono = 11f;
        public const float FsMonoSm = 11.5f;
        public const float FsTileName = 10.5f;
        public const float FsStatNum = 16f;
        public const float FsHelpH3 = 16f;

        // ---- palette -----------------------------------------------------

        public static AppTheme Current { get; private set; } = AppTheme.Light;
        public static bool IsDark { get { return Current == AppTheme.Dark; } }

        public static Color FormBack { get; private set; }   // bg
        public static Color Surface { get; private set; }    // surface
        public static Color Fore { get; private set; }       // fg
        public static Color ForeMuted { get; private set; }  // muted

        // Secondary text that sits on a *stained* plane (Subtle / Hover /
        // Selected). ForeMuted only clears 4.5:1 against FormBack and
        // Surface; on the stained planes it drops to 4.27 / 4.05 / 2.98
        // (light) and 4.47 / 4.96 / 4.03 (dark). Derived as
        // oklch(fg 36% + muted 64%) so one formula serves both themes.
        public static Color MutedStrong { get; private set; }
        public static Color Warn { get; private set; }
        public static Color Ok { get; private set; }
        public static Color Accent { get; private set; }      // solid (filled button)
        public static Color AccentText { get; private set; }  // accent (text / icons)
        public static Color AccentFore { get; private set; }
        public static Color Solid { get; private set; }
        public static Color SolidHover { get; private set; }
        public static Color SolidActive { get; private set; }
        public static Color Subtle { get; private set; }
        public static Color Hover { get; private set; }
        public static Color AccentSoft { get; private set; }
        public static Color AccentRing { get; private set; }
        public static Color DisabledText { get; private set; }
        public static Color DisabledBorder { get; private set; }
        public static Color CloseHover { get; private set; }
        public static Color FocusRing { get; private set; }

        public static Color InputBack { get; private set; }
        public static Color InputFore { get; private set; }
        public static Color GridBack { get; private set; }
        public static Color GridLine { get; private set; }
        public static Color Border { get; private set; }
        public static Color ButtonBack { get; private set; }
        public static Color ButtonFore { get; private set; }
        public static Color ButtonHover { get; private set; }
        public static Color MenuBack { get; private set; }
        public static Color MenuFore { get; private set; }
        public static Color MenuHover { get; private set; }
        public static Color MenuSeparator { get; private set; }
        public static Color TilePlaceholder { get; private set; }
        public static Color TileLabelBack { get; private set; }
        public static Color TileLabelFore { get; private set; }
        public static Color TileBorder { get; private set; }
        public static Color TileCheckBack { get; private set; }
        public static Color TileCheckBorder { get; private set; }
        public static Color TileCheckTick { get; private set; }

        // Persistent selected state: nav rail item, list row, checked tile.
        // Derived as oklch(bg 80% + fg 20%) in both themes, which puts it
        // one step further from the surface than Hover - the two palettes
        // layer in opposite directions, so this cannot be a single literal.
        public static Color Selected { get; private set; }

        // Active block of a SegmentedControl. The light theme reuses the
        // surface (white) on a subtle track; the dark theme needs a *lighter*
        // block (oklch surface 76% + fg 24%) because surface is darker than
        // subtle there. Reusing one formula for both makes the selection read
        // as sunken rather than raised.
        public static Color SegOn { get; private set; }

        // Track fill of a ToggleSwitch once hovered, and the soft wash a
        // danger button takes while hovered. Both come straight from the
        // prototype (.switch:hover / .btn-danger:hover).
        public static Color SwitchHover { get; private set; }
        public static Color WarnSoft { get; private set; }

        // Translucent layers (alpha carried in the Color).
        public static Color ThumbGradient { get; private set; } // filename scrim base
        public static Color CheckScrim { get; private set; }    // unchecked badge base
        public static Color NowPill { get; private set; }       // "now showing" pill
        public static Color Glass { get; private set; }         // tray menu glass

        static Theme()
        {
            Set(AppTheme.Light);
        }

        private static Color Rgb(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static void Set(AppTheme t)
        {
            Current = t;
            if (t == AppTheme.Dark)
            {
                FormBack = Rgb(0x0D, 0x10, 0x13);
                Surface = Rgb(0x15, 0x19, 0x1D);
                Fore = Rgb(0xE6, 0xE8, 0xEA);
                ForeMuted = Rgb(0x8D, 0x93, 0x99);
                MutedStrong = Rgb(0xAC, 0xB1, 0xB6);
                Warn = Rgb(0xF9, 0x77, 0x70);
                Ok = Rgb(0x47, 0xB8, 0x77);
                AccentText = Rgb(0x46, 0x93, 0xF1);
                // Dark solid is mixed 86% (not the prototype's 88%): at 88%
                // white label text lands on 4.30:1, just under the 4.5:1 AA
                // line. 86% gives 4.55:1 and is visually identical.
                Solid = Rgb(0x38, 0x77, 0xC5);
                SolidHover = Rgb(0x30, 0x68, 0xAD);
                SolidActive = Rgb(0x28, 0x59, 0x95);
                Accent = Solid;
                AccentFore = Color.White;
                // Subtle is mixed from the *surface* in the dark theme
                // (surface 88% + fg 12%). Mixing it from bg, the way the
                // light theme does, leaves it only 2/255 away from Surface -
                // i.e. invisible on the very fills it is meant to tint.
                Subtle = Rgb(0x29, 0x2D, 0x31);
                Hover = Rgb(0x22, 0x25, 0x28);
                Selected = Rgb(0x31, 0x34, 0x37);
                SegOn = Rgb(0x40, 0x44, 0x48);
                SwitchHover = Rgb(0x5D, 0x61, 0x65);
                WarnSoft = Rgb(0x30, 0x24, 0x27);
                AccentSoft = Rgb(0x1B, 0x27, 0x36);
                AccentRing = Rgb(0x28, 0x4A, 0x71);
                DisabledText = Rgb(0x6B, 0x71, 0x76);
                DisabledBorder = Rgb(0x23, 0x28, 0x2D);
                CloseHover = Rgb(0xC2, 0x17, 0x25);
                FocusRing = Rgb(0x28, 0x4A, 0x71);

                InputBack = Surface;
                InputFore = Fore;
                GridBack = Surface;
                GridLine = Rgb(0x2A, 0x2E, 0x33);
                Border = Rgb(0x2A, 0x2E, 0x33);
                ButtonBack = Surface;
                ButtonFore = Fore;
                ButtonHover = Hover;
                MenuBack = Surface;
                MenuFore = Fore;
                MenuHover = Subtle;
                MenuSeparator = Border;
                TilePlaceholder = Rgb(0x1B, 0x1F, 0x24);
                TileLabelBack = Surface;
                TileLabelFore = Fore;
                TileBorder = Border;
                TileCheckBack = Color.FromArgb(107, 0x02, 0x06, 0x0D);
                TileCheckBorder = Color.FromArgb(232, 255, 255, 255);
                TileCheckTick = Color.White;

                ThumbGradient = Color.FromArgb(209, 0x01, 0x03, 0x09);
                CheckScrim = Color.FromArgb(107, 0x02, 0x06, 0x0D);
                NowPill = Color.FromArgb(158, 0x02, 0x06, 0x0D);
                Glass = Color.FromArgb(158, 0x08, 0x0E, 0x16);
            }
            else
            {
                FormBack = Rgb(0xFA, 0xFC, 0xFD);
                Surface = Color.White;
                Fore = Rgb(0x0E, 0x12, 0x17);
                ForeMuted = Rgb(0x6A, 0x6F, 0x76);
                MutedStrong = Rgb(0x46, 0x4B, 0x51);
                Warn = Rgb(0xBE, 0x22, 0x2A);
                Ok = Rgb(0x00, 0x8B, 0x4E);
                AccentText = Rgb(0x17, 0x79, 0xE1);
                Solid = Rgb(0x11, 0x65, 0xBE);
                SolidHover = Rgb(0x0D, 0x55, 0xA1);
                SolidActive = Rgb(0x09, 0x48, 0x8B);
                Accent = Solid;
                AccentFore = Color.White;
                Subtle = Rgb(0xEA, 0xEC, 0xED);
                Hover = Rgb(0xE5, 0xE6, 0xE8);
                Selected = Rgb(0xC4, 0xC7, 0xC9);
                SegOn = Color.White;
                SwitchHover = Rgb(0x9B, 0x9E, 0xA2);
                WarnSoft = Rgb(0xF7, 0xE4, 0xE5);
                AccentSoft = Rgb(0xE3, 0xEE, 0xFB);
                AccentRing = Rgb(0xA2, 0xC9, 0xF3);
                DisabledText = Rgb(0x93, 0x97, 0x9C);
                DisabledBorder = Rgb(0xEA, 0xEC, 0xEE);
                CloseHover = Rgb(0xC2, 0x17, 0x25);
                FocusRing = Rgb(0xA2, 0xC9, 0xF3);

                InputBack = Surface;
                InputFore = Fore;
                GridBack = Surface;
                GridLine = Rgb(0xE2, 0xE5, 0xE8);
                Border = Rgb(0xE2, 0xE5, 0xE8);
                ButtonBack = Surface;
                ButtonFore = Fore;
                ButtonHover = Hover;
                MenuBack = Surface;
                MenuFore = Fore;
                MenuHover = Subtle;
                MenuSeparator = Border;
                TilePlaceholder = Rgb(0xF1, 0xF3, 0xF5);
                TileLabelBack = Surface;
                TileLabelFore = Fore;
                TileBorder = Border;
                TileCheckBack = Color.FromArgb(107, 0x02, 0x06, 0x0D);
                TileCheckBorder = Color.FromArgb(232, 255, 255, 255);
                TileCheckTick = Color.White;

                ThumbGradient = Color.FromArgb(209, 0x01, 0x03, 0x09);
                CheckScrim = Color.FromArgb(107, 0x02, 0x06, 0x0D);
                NowPill = Color.FromArgb(158, 0x02, 0x06, 0x0D);
                Glass = Color.FromArgb(158, 0x08, 0x0E, 0x16);
            }
        }

        public static AppTheme FromConfig(string v)
        {
            return string.Equals(v, "dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark : AppTheme.Light;
        }

        public static string ToConfig(AppTheme t)
        {
            return t == AppTheme.Dark ? "dark" : "light";
        }

        // ---- fonts -------------------------------------------------------

        private static string uiFamily;
        private static string monoFamily;
        private static readonly Dictionary<string, Font> FontCache =
            new Dictionary<string, Font>();

        // Segoe UI Variable ships with Windows 11; Consolas is the safe
        // stand-in for Cascadia Mono. Both keep CJK coverage via GDI font
        // linking when drawn with TextRenderer.
        private static string ResolveFamily(string[] want, string fallback)
        {
            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    List<string> have = new List<string>();
                    foreach (FontFamily f in col.Families) have.Add(f.Name);
                    foreach (string w in want)
                    {
                        foreach (string h in have)
                        {
                            if (string.Equals(h, w, StringComparison.OrdinalIgnoreCase))
                                return h;
                        }
                    }
                }
            }
            catch { }
            return fallback;
        }

        private static string UiFamily
        {
            get
            {
                if (uiFamily == null)
                {
                    uiFamily = ResolveFamily(new string[] {
                        "Segoe UI Variable Text", "Segoe UI Variable Small",
                        "Segoe UI", "Microsoft YaHei UI" }, "Microsoft YaHei UI");
                }
                return uiFamily;
            }
        }

        private static string MonoFamily
        {
            get
            {
                if (monoFamily == null)
                {
                    monoFamily = ResolveFamily(new string[] {
                        "Cascadia Mono", "Consolas", "Courier New" }, "Consolas");
                }
                return monoFamily;
            }
        }

        // pt is the 96-DPI design point size. Pass a control (or its
        // DeviceDpi) so the result is correct on 125% / 150% monitors.
        public static Font UiFont(Control c, float pt)
        {
            return UiFont(pt, FontStyle.Regular, c == null ? 96 : c.DeviceDpi);
        }

        public static Font UiFont(float pt, FontStyle style, int dpi)
        {
            float px = Math.Max(1f, pt * dpi / 96f);
            string key = "u" + px.ToString("0.##") + "|" + (int)style;
            lock (FontCache)
            {
                Font f;
                if (FontCache.TryGetValue(key, out f)) return f;
                f = new Font(UiFamily, px, style, GraphicsUnit.Pixel);
                FontCache[key] = f;
                return f;
            }
        }

        public static Font MonoFont(Control c, float pt)
        {
            return MonoFont(pt, c == null ? 96 : c.DeviceDpi);
        }

        public static Font MonoFont(float pt, int dpi)
        {
            float px = Math.Max(1f, pt * dpi / 96f);
            string key = "m" + px.ToString("0.##");
            lock (FontCache)
            {
                Font f;
                if (FontCache.TryGetValue(key, out f)) return f;
                f = new Font(MonoFamily, px, FontStyle.Regular, GraphicsUnit.Pixel);
                FontCache[key] = f;
                return f;
            }
        }

        public static void ClearFontCache()
        {
            lock (FontCache)
            {
                foreach (Font f in FontCache.Values) { try { f.Dispose(); } catch { } }
                FontCache.Clear();
            }
        }

        // ---- applying the theme ------------------------------------------

        // Paint a whole window: the form itself, every child control
        // recursively, and the immersive title bar.
        public static void ApplyTo(Control root)
        {
            if (root == null) return;
            ApplyOne(root);
            foreach (Control c in root.Controls) ApplyTo(c);
        }

        private static void ApplyOne(Control c)
        {
            // Self-painting controls own their colours entirely.
            IThemed themed = c as IThemed;
            if (themed != null) { themed.ApplyTheme(); return; }

            // Icon buttons paint themselves; they only need the surrounding
            // surface colour, and must keep their owner-draw flags intact.
            if ((c.Tag as string) == RoleIcon)
            {
                c.BackColor = FormBack;
                return;
            }
            if ((c.Tag as string) == RoleCard)
            {
                c.BackColor = Surface;
                c.ForeColor = Fore;
                return;
            }
            if (c is Button) { ApplyButton((Button)c); return; }
            if (c is ComboBox)
            {
                c.BackColor = InputBack;
                c.ForeColor = InputFore;
                return;
            }
            if (c is TextBox)
            {
                c.BackColor = InputBack;
                c.ForeColor = InputFore;
                return;
            }
            if (c is RichTextBox)
            {
                c.BackColor = InputBack;
                c.ForeColor = InputFore;
                return;
            }
            if (c is ListView)
            {
                ListView lv = (ListView)c;
                lv.BackColor = GridBack;
                lv.ForeColor = IsDark ? Fore : SystemColors.WindowText;
                if (IsDark)
                {
                    // The column header is system-painted and would stay
                    // light on a dark grid, so take it over.
                    lv.OwnerDraw = true;
                    lv.DrawColumnHeader -= DrawDarkHeader;
                    lv.DrawColumnHeader += DrawDarkHeader;
                    lv.DrawItem -= DrawDarkItem;
                    lv.DrawItem += DrawDarkItem;
                }
                else
                {
                    lv.OwnerDraw = false;
                    lv.DrawColumnHeader -= DrawDarkHeader;
                    lv.DrawItem -= DrawDarkItem;
                }
                return;
            }
            if (c is PickerCanvas)
            {
                c.BackColor = FormBack;
                return;
            }
            if (c is Form)
            {
                c.BackColor = FormBack;
                c.ForeColor = Fore;
                Form f = (Form)c;
                // Owner-drawn windows have no system frame to tint.
                if (f.FormBorderStyle != FormBorderStyle.None) SetTitleBar(f);
                return;
            }
            if (c is GroupBox)
            {
                c.BackColor = FormBack;
                c.ForeColor = Fore;
                return;
            }

            c.BackColor = FormBack;
            c.ForeColor = RoleColor(c);
        }

        private static Color RoleColor(Control c)
        {
            string tag = c.Tag as string;
            if (tag == RoleMuted) return ForeMuted;
            if (tag == RoleMutedStrong) return MutedStrong;
            if (tag == RoleAccentText) return AccentText;
            if (tag == RoleWarn) return Warn;
            if (tag == RoleOk) return Ok;
            return Fore;
        }

        private static void ApplyButton(Button b)
        {
            string tag = b.Tag as string;
            bool accent = tag == RoleAccent;

            if (accent)
            {
                // Primary action: filled with the accent colour in both
                // themes, so it stays the visual anchor of the dialog.
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Accent;
                b.ForeColor = AccentFore;
                b.FlatAppearance.MouseOverBackColor = SolidHover;
                b.FlatAppearance.MouseDownBackColor = SolidActive;
                b.UseVisualStyleBackColor = false;
                return;
            }

            if (IsDark)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = Accent;
                b.BackColor = ButtonBack;
                b.ForeColor = ButtonFore;
                b.UseVisualStyleBackColor = false;
            }
            else
            {
                // Back to the untouched native look.
                b.FlatStyle = FlatStyle.Standard;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Color.Empty;
                b.BackColor = SystemColors.Control;
                b.ForeColor = SystemColors.ControlText;
                b.UseVisualStyleBackColor = true;
            }
        }

        private static void DrawDarkHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Subtle))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }
            using (Pen p = new Pen(GridLine))
            {
                e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 3,
                    e.Bounds.Right - 1, e.Bounds.Bottom - 3);
            }
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font,
                new Rectangle(e.Bounds.Left + 4, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height),
                Fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        // Owner-drawn rows must also paint the normal row text, otherwise
        // switching the header over would blank out the list.
        private static void DrawDarkItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        // Right-click / tray menus are not in Controls, so they are themed
        // separately. Light mode keeps the native renderer entirely.
        public static void ApplyMenuStrip(ToolStrip menu)
        {
            if (menu == null) return;
            if (IsDark)
            {
                menu.Renderer = new DarkMenuRenderer();
                menu.BackColor = MenuBack;
                menu.ForeColor = MenuFore;
            }
            else
            {
                menu.Renderer = null;
                menu.BackColor = SystemColors.Menu;
                menu.ForeColor = SystemColors.MenuText;
            }
            foreach (ToolStripItem it in menu.Items)
            {
                it.ForeColor = MenuFore;
                it.BackColor = MenuBack;
            }
        }

        internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = MenuFore;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (Pen p = new Pen(MenuSeparator))
                {
                    e.Graphics.DrawLine(p, 4, e.Item.Height / 2, e.Item.Width - 4, e.Item.Height / 2);
                }
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return MenuBack; } }
            public override Color MenuBorder { get { return MenuSeparator; } }
            public override Color MenuItemPressedGradientBegin { get { return MenuHover; } }
            public override Color MenuItemPressedGradientEnd { get { return MenuHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return MenuHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return MenuHover; } }
            public override Color MenuItemSelected { get { return MenuHover; } }
            public override Color MenuItemBorder { get { return MenuHover; } }
            public override Color ImageMarginGradientBegin { get { return MenuBack; } }
            public override Color ImageMarginGradientMiddle { get { return MenuBack; } }
            public override Color ImageMarginGradientEnd { get { return MenuBack; } }
            public override Color SeparatorDark { get { return MenuSeparator; } }
            public override Color SeparatorLight { get { return MenuSeparator; } }
        }

        // Make the native title bar follow the theme (Windows 10 1903+ /
        // Windows 11). Silently ignored on older builds.
        private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, uint attr,
            ref int value, uint size);

        public static void SetTitleBar(Form f)
        {
            if (f == null || f.IsDisposed || !f.IsHandleCreated) return;
            try
            {
                int dark = IsDark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref dark, sizeof(uint));
            }
            catch
            {
                // older Windows without this attribute: the title bar simply
                // keeps its system colour.
            }
        }
    }

    // Implemented by the self-painted controls in UiKit.cs. Theme.ApplyTo
    // calls ApplyTheme() and leaves the rest of the colouring to them.
    internal interface IThemed
    {
        void ApplyTheme();
    }

    // Small sun / moon button for the top-right corner of the main window.
    // The glyph shows where a click leads: a moon while the app is light
    // (click for dark), a sun while it is dark (click for light).
    internal sealed class ThemeToggleButton : Button, IThemed
    {
        private bool hover;

        public ThemeToggleButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer, true);
            SetBounds(0, 0, 26, 26);
            TabStop = false;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Text = "";
        }

        public void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            bool dark = Theme.IsDark;
            Color surface = Theme.FormBack;
            if (hover) surface = Theme.Hover;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(surface))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            if (dark) DrawSun(g);
            else DrawMoon(g, surface);
        }

        private void DrawSun(Graphics g)
        {
            int cx = Width / 2;
            int cy = Height / 2;
            using (Pen p = new Pen(Color.FromArgb(255, 196, 78), 1.6f))
            {
                g.DrawEllipse(p, cx - 4.5f, cy - 4.5f, 9f, 9f);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0;
                    float c = (float)Math.Cos(a);
                    float s = (float)Math.Sin(a);
                    g.DrawLine(p, cx + c * 7.2f, cy + s * 7.2f,
                        cx + c * 10f, cy + s * 10f);
                }
            }
        }

        // A disc with a surface-coloured disc bitten out of it.
        private void DrawMoon(Graphics g, Color surface)
        {
            int cx = Width / 2;
            int cy = Height / 2;
            using (SolidBrush ink = new SolidBrush(Color.FromArgb(72, 72, 96)))
            using (SolidBrush cut = new SolidBrush(surface))
            {
                g.FillEllipse(ink, cx - 6f, cy - 6.5f, 12f, 12f);
                g.FillEllipse(cut, cx - 1f, cy - 10f, 12f, 12f);
            }
        }
    }
}
