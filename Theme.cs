using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    public static class Theme
    {
        public const string RoleMuted = "wc:muted";        // secondary text
        public const string RoleAccent = "wc:accent";      // filled primary button
        public const string RoleAccentText = "wc:accenttext"; // coloured label text
        public const string RoleIcon = "wc:icon";          // fully owner-drawn button

        public static AppTheme Current { get; private set; } = AppTheme.Light;
        public static bool IsDark { get { return Current == AppTheme.Dark; } }

        public static Color FormBack { get; private set; }
        public static Color Fore { get; private set; }
        public static Color ForeMuted { get; private set; }
        public static Color Warn { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentText { get; private set; }
        public static Color AccentFore { get; private set; }
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

        static Theme()
        {
            Set(AppTheme.Light);
        }

        public static void Set(AppTheme t)
        {
            Current = t;
            if (t == AppTheme.Dark)
            {
                FormBack = Color.FromArgb(32, 32, 32);
                Fore = Color.FromArgb(226, 226, 226);
                ForeMuted = Color.FromArgb(150, 150, 150);
                Warn = Color.FromArgb(255, 138, 138);
                Accent = Color.FromArgb(0, 120, 212);
                AccentText = Color.FromArgb(94, 178, 255);
                AccentFore = Color.White;
                InputBack = Color.FromArgb(48, 48, 48);
                InputFore = Color.FromArgb(232, 232, 232);
                GridBack = Color.FromArgb(42, 42, 42);
                GridLine = Color.FromArgb(72, 72, 72);
                Border = Color.FromArgb(96, 96, 96);
                ButtonBack = Color.FromArgb(58, 58, 58);
                ButtonFore = Color.FromArgb(232, 232, 232);
                ButtonHover = Color.FromArgb(78, 78, 78);
                MenuBack = Color.FromArgb(40, 40, 40);
                MenuFore = Color.FromArgb(226, 226, 226);
                MenuHover = Color.FromArgb(64, 64, 64);
                MenuSeparator = Color.FromArgb(92, 92, 92);
                TilePlaceholder = Color.FromArgb(54, 54, 54);
                TileLabelBack = Color.FromArgb(34, 34, 34);
                TileLabelFore = Color.FromArgb(198, 198, 198);
                TileBorder = Color.FromArgb(88, 88, 88);
                TileCheckBack = Color.FromArgb(64, 64, 64);
                TileCheckBorder = Color.FromArgb(154, 154, 154);
                TileCheckTick = Color.White;
            }
            else
            {
                FormBack = SystemColors.Control;
                Fore = SystemColors.ControlText;
                ForeMuted = Color.FromArgb(96, 96, 96);
                Warn = Color.FromArgb(176, 0, 0);
                Accent = Color.FromArgb(24, 95, 165);
                AccentText = Color.FromArgb(0, 90, 158);
                AccentFore = Color.White;
                InputBack = SystemColors.Window;
                InputFore = SystemColors.WindowText;
                GridBack = SystemColors.Window;
                GridLine = Color.FromArgb(223, 223, 223);
                Border = Color.FromArgb(173, 173, 173);
                ButtonBack = SystemColors.Control;
                ButtonFore = SystemColors.ControlText;
                ButtonHover = SystemColors.Control;
                MenuBack = SystemColors.Menu;
                MenuFore = SystemColors.MenuText;
                MenuHover = SystemColors.Highlight;
                MenuSeparator = SystemColors.ControlDark;
                TilePlaceholder = Color.FromArgb(240, 240, 240);
                TileLabelBack = SystemColors.Control;
                TileLabelFore = Color.FromArgb(70, 70, 70);
                TileBorder = Color.FromArgb(176, 176, 176);
                TileCheckBack = Color.White;
                TileCheckBorder = Color.FromArgb(120, 120, 120);
                TileCheckTick = Color.White;
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
            // Icon buttons paint themselves; they only need the surrounding
            // surface colour, and must keep their owner-draw flags intact.
            if ((c.Tag as string) == RoleIcon)
            {
                c.BackColor = IsDark ? ButtonBack : FormBack;
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
                c.BackColor = IsDark ? Color.FromArgb(28, 28, 28) : SystemColors.Control;
                return;
            }
            if (c is Form)
            {
                c.BackColor = FormBack;
                c.ForeColor = Fore;
                SetTitleBar((Form)c);
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
            if (tag == RoleAccentText) return AccentText;
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
                b.FlatAppearance.MouseOverBackColor = IsDark
                    ? Color.FromArgb(28, 142, 232) : Color.FromArgb(32, 112, 186);
                b.FlatAppearance.MouseDownBackColor = IsDark
                    ? Color.FromArgb(0, 96, 176) : Color.FromArgb(18, 78, 140);
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
            using (SolidBrush b = new SolidBrush(Color.FromArgb(48, 48, 48)))
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

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable())
            {
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

    // Small sun / moon button for the top-right corner of the main window.
    // The glyph shows where a click leads: a moon while the app is light
    // (click for dark), a sun while it is dark (click for light).
    internal sealed class ThemeToggleButton : Button
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
            Tag = Theme.RoleIcon;
            Text = "";
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
            Color surface = dark ? Theme.ButtonBack : Theme.FormBack;
            if (hover) surface = dark ? Theme.ButtonHover : Color.FromArgb(212, 212, 212);

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
