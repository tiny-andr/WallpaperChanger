using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Two panes instead of one long RichTextBox: the sections of Loc's help
    // text down the left, the text of the selected one on the right. A reader
    // who already knows which part they want no longer scrolls past the rest.
    //
    // The text still comes from Loc.HelpContent(), which is a flat list with
    // Kind == 1 marking a heading, so the section split happens here.
    public class HelpForm : Form
    {
        private const int TocW = 200;
        private const int Pad = 24;
        private const int BodyTop = 96;

        private readonly List<FlatButton> tabs = new List<FlatButton>();
        private readonly List<int> sectionLine = new List<int>();

        private Panel toc;
        private Panel body;
        private HelpView view;
        private KitLabel lblTitle;
        private KitLabel lblSub;
        private KitLabel lblToc;
        private KitLabel lblFoot;
        private FlatButton btnClose;
        private bool ready;
        private int active = -1;

        public HelpForm()
        {
            Text = Loc.T("help.title") + " - WallpaperChanger v" + Application.ProductVersion;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            // Same DPI rule as MainForm: pin the 96-DPI design basis AFTER the
            // mode, because the AutoScaleMode setter resets AutoScaleDimensions
            // to the current device DPI (which shrank every window to 2/3 of
            // its designed size on this machine).
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(760, 540);
            MinimumSize = new Size(620, 420);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Theme.FormBack;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildChrome();
            BuildSections();
            Theme.ApplyTo(this);
            ready = true;
            LayoutChrome();
            SelectSection(0);
        }

        private void BuildChrome()
        {
            lblTitle = LabelOf(LabelStyle.PageTitle, false);
            lblTitle.Text = Loc.T("help.title");
            Controls.Add(lblTitle);

            lblSub = LabelOf(LabelStyle.Sub, true);
            lblSub.Text = Loc.T("help.sub");
            Controls.Add(lblSub);

            lblFoot = LabelOf(LabelStyle.Cap, true);
            lblFoot.Text = "WallpaperChanger " + Application.ProductVersion;
            Controls.Add(lblFoot);

            lblToc = LabelOf(LabelStyle.Cap, true);
            lblToc.Text = Loc.T("help.toc");
            Controls.Add(lblToc);

            toc = new Panel();
            toc.BackColor = Theme.FormBack;
            Controls.Add(toc);

            body = new Panel();
            body.BackColor = Theme.FormBack;
            body.AutoScroll = true;
            view = new HelpView();
            body.Controls.Add(view);
            Controls.Add(body);

            btnClose = new FlatButton();
            btnClose.Kind = BtnKind.Primary;
            btnClose.Text = Loc.T("help.gotit");
            btnClose.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btnClose);
            AcceptButton = btnClose;
            CancelButton = btnClose;
        }

        private static KitLabel LabelOf(LabelStyle style, bool muted)
        {
            KitLabel l = new KitLabel();
            l.Style = style;
            l.Muted = muted;
            l.BackColor = Color.Transparent;
            return l;
        }

        // Split the flat help list on its headings and give each section a tab.
        private void BuildSections()
        {
            Loc.HelpLine[] all = Loc.HelpContent();
            List<Loc.HelpLine> lines = new List<Loc.HelpLine>();
            int tabH = Gfx.S(this, 32);
            int gap = Gfx.S(this, 6);

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Kind == 1)
                {
                    sectionLine.Add(lines.Count);
                    FlatButton b = new FlatButton();
                    b.Kind = BtnKind.Ghost;
                    b.AlignLeft = true;
                    b.Text = all[i].Text;
                    int idx = tabs.Count;
                    b.Click += delegate { SelectSection(idx); };
                    b.SetBounds(0, idx * (tabH + gap), Gfx.S(this, TocW), tabH);
                    tabs.Add(b);
                    toc.Controls.Add(b);
                }
                else if (all[i].Text.Trim().Length > 0)
                {
                    lines.Add(all[i]);
                }
                else
                {
                    // A blank source line is a section break: keep it as a gap.
                    lines.Add(new Loc.HelpLine(2, ""));
                }
            }
            view.SetLines(lines);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ready) LayoutChrome();
        }

        private void LayoutChrome()
        {
            int pad = Gfx.S(this, Pad);
            int w = ClientSize.Width;
            int hgt = ClientSize.Height;

            lblTitle.SetBounds(pad, Gfx.S(this, 18), Math.Max(80, w - pad * 2 - Gfx.S(this, 170)),
                Gfx.S(this, 30));
            lblSub.SetBounds(pad, Gfx.S(this, 50), Math.Max(80, w - pad * 2 - Gfx.S(this, 170)),
                Gfx.S(this, 20));
            lblFoot.SetBounds(w - pad - Gfx.S(this, 170), Gfx.S(this, 22),
                Gfx.S(this, 170), Gfx.S(this, 20));
            lblFoot.Align = ContentAlignment.MiddleRight;

            int footY = hgt - pad - Gfx.S(this, 34);
            int bodyBottom = Math.Max(Gfx.S(this, BodyTop) + 40, footY - Gfx.S(this, 18));

            lblToc.SetBounds(pad, Gfx.S(this, BodyTop) - Gfx.S(this, 22), Gfx.S(this, TocW),
                Gfx.S(this, 18));
            toc.SetBounds(pad, Gfx.S(this, BodyTop), Gfx.S(this, TocW),
                Math.Max(40, bodyBottom - Gfx.S(this, BodyTop)));

            int bodyX = pad + Gfx.S(this, TocW) + Gfx.S(this, 20);
            body.SetBounds(bodyX, Gfx.S(this, BodyTop), Math.Max(120, w - pad - bodyX),
                Math.Max(80, bodyBottom - Gfx.S(this, BodyTop)));

            btnClose.SetBounds(w - pad - Gfx.S(this, 110), footY, Gfx.S(this, 110), Gfx.S(this, 34));

            // Reserve the scrollbar's width up front rather than let it eat
            // into the text and trigger a second wrap pass - which would change
            // the height again and flicker.
            int inner = Math.Max(200, body.ClientSize.Width -
                SystemInformation.VerticalScrollBarWidth - Gfx.S(this, 6));
            view.InnerWidth = inner;
            view.SetBounds(0, 0, inner, view.MeasuredHeight);
            body.AutoScrollMinSize = new Size(0, view.MeasuredHeight);
            view.SetBounds(0, 0, inner, view.MeasuredHeight);
        }

        // Highlight the tab, scroll the body to the section it points at.
        private void SelectSection(int index)
        {
            if (index < 0 || index >= tabs.Count) return;
            active = index;
            for (int i = 0; i < tabs.Count; i++)
            {
                // The selected tab keeps a fill of its own; a hover-only ghost
                // would lose it the moment the pointer leaves the tab.
                tabs[i].Kind = i == index ? BtnKind.Secondary : BtnKind.Ghost;
            }
            body.AutoScrollPosition = new Point(0, view.LineTop(sectionLine[index]));
            view.Invalidate();
        }

        // Paints the whole section flow in one control. A label per line would
        // need the same wrap measurement anyway, and this way the headings and
        // their spacing stay in one place.
        internal class HelpView : Control, IThemed
        {
            private readonly List<Loc.HelpLine> lines = new List<Loc.HelpLine>();
            private readonly List<int> tops = new List<int>();
            private readonly List<int> heights = new List<int>();
            private int inner = 600;

            public HelpView()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.FormBack;
            }

            public void ApplyTheme()
            {
                BackColor = Theme.FormBack;
                ForeColor = Theme.Fore;
                Invalidate();
            }

            public int MeasuredHeight
            {
                get
                {
                    int bottom = 0;
                    foreach (int t in tops) bottom = Math.Max(bottom, t);
                    return bottom + Gfx.S(this, 24);
                }
            }

            public int LineTop(int line)
            {
                if (line < 0 || line >= tops.Count) return 0;
                return tops[line];
            }

            public int InnerWidth
            {
                get { return inner; }
                set
                {
                    int v = Math.Max(120, value);
                    if (v == inner) return;
                    inner = v;
                    Measure();
                    Invalidate();
                }
            }

            public void SetLines(List<Loc.HelpLine> src)
            {
                lines.Clear();
                lines.AddRange(src);
                Measure();
                Invalidate();
            }

            private Font HeadFont { get { return Theme.UiFont(Theme.FsCardTitle, FontStyle.Bold, DeviceDpi); } }
            private Font BodyFont { get { return Theme.UiFont(this, Theme.FsSub); } }

            private void Measure()
            {
                tops.Clear();
                heights.Clear();
                int y = Gfx.S(this, 4);
                for (int i = 0; i < lines.Count; i++)
                {
                    bool head = lines[i].Kind == 1;
                    bool gapOnly = lines[i].Kind == 2;
                    Font f = head ? HeadFont : BodyFont;
                    int h = gapOnly ? Gfx.S(this, 12)
                        : TextRenderer.MeasureText(lines[i].Text, f, new Size(inner, 0),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                    if (head) y += Gfx.S(this, i == 0 ? 0 : 10);
                    tops.Add(y);
                    heights.Add(h);
                    y += h + (head ? Gfx.S(this, 6) : Gfx.S(this, 2));
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent != null ? Parent.BackColor : Theme.FormBack);

                for (int i = 0; i < lines.Count; i++)
                {
                    if (i >= tops.Count) break;
                    if (lines[i].Kind == 2) continue;
                    bool head = lines[i].Kind == 1;
                    Gfx.Text(g, lines[i].Text, head ? HeadFont : BodyFont,
                        head ? Theme.Fore : Theme.MutedStrong,
                        new Rectangle(0, tops[i], inner, heights[i]),
                        Gfx.LeftTop);
                }
            }
        }
    }
}
