using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Shared GDI+ helpers for the self-painted controls.
    //
    // Every metric here is a *design* value at 96 DPI; S() scales it to the
    // control's current DPI so 125% / 150% monitors stay pixel-correct.
    internal static class Gfx
    {
        public static float Scale(Control c)
        {
            int dpi = (c == null || c.DeviceDpi <= 0) ? 96 : c.DeviceDpi;
            return dpi / 96f;
        }

        public static int S(Control c, float v)
        {
            return (int)Math.Round(v * Scale(c));
        }

        public static Rectangle SR(Control c, int x, int y, int w, int h)
        {
            return new Rectangle(S(c, x), S(c, y), S(c, w), S(c, h));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            float d = Math.Min(rad * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            if (c.A == 0) return;
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillPath(b, p);
            }
        }

        // Inset by half the pen width so a 1px border lands on whole pixels
        // instead of straddling two and looking blurred.
        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            if (w <= 0f || c.A == 0) return;
            RectangleF rr = new RectangleF(r.X + w / 2f, r.Y + w / 2f,
                Math.Max(0f, r.Width - w), Math.Max(0f, r.Height - w));
            using (GraphicsPath p = Round(rr, Math.Max(0f, rad - w / 2f)))
            using (Pen pen = new Pen(c, w))
            {
                pen.Alignment = PenAlignment.Center;
                g.DrawPath(pen, p);
            }
        }

        public static void Line(Graphics g, float x1, float y1, float x2, float y2, Color c, float w)
        {
            using (Pen p = new Pen(c, w))
            {
                g.DrawLine(p, x1, y1, x2, y2);
            }
        }

        // TextRenderer (GDI) rather than DrawString: it applies system font
        // linking, so Latin glyphs from Segoe UI Variable and CJK glyphs
        // from Microsoft YaHei render in one string.
        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r,
            TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public const TextFormatFlags LeftMid =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix;

        public const TextFormatFlags LeftTop =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix |
            TextFormatFlags.WordBreak;

        public const TextFormatFlags CenterMid =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        public const TextFormatFlags RightMid =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix;

        public static TextFormatFlags Ellipsis(TextFormatFlags f)
        {
            return f | TextFormatFlags.EndEllipsis;
        }

        // Honest focus ring: a rounded outline just outside the control.
        public static void FocusRing(Graphics g, RectangleF r, float rad, Control c)
        {
            using (Pen p = new Pen(Theme.FocusRing, 2f))
            {
                p.Alignment = PenAlignment.Center;
                using (GraphicsPath path = Round(new RectangleF(
                    r.X - 1.5f, r.Y - 1.5f, r.Width + 3f, r.Height + 3f), rad + 1.5f))
                {
                    g.DrawPath(p, path);
                }
            }
        }
    }

    internal enum IconKind
    {
        Grid, Folder, Rotate, Gear, Help, Plus, ArrowLeft, ArrowRight,
        Check, Close, Minimize, Maximize, Restore, Search, FolderOpen, Trash, Pause, Play
    }

    // All icons are drawn as vector strokes so they stay crisp at any DPI
    // and pick up the theme colour. Box is the icon's square bounds.
    internal static class IconPainter
    {
        public static void Draw(Graphics g, IconKind kind, RectangleF box, Color c, float stroke)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(box.Width, box.Height);
            float ox = box.X + (box.Width - s) / 2f;
            float oy = box.Y + (box.Height - s) / 2f;
            float u = s / 24f;                 // design grid is 24x24
            using (Pen p = new Pen(c, stroke))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case IconKind.Grid:
                        DrawRect(g, p, ox + 3 * u, oy + 3 * u, 7.5f * u, 7.5f * u, 1.6f * u);
                        DrawRect(g, p, ox + 13.5f * u, oy + 3 * u, 7.5f * u, 7.5f * u, 1.6f * u);
                        DrawRect(g, p, ox + 3 * u, oy + 13.5f * u, 7.5f * u, 7.5f * u, 1.6f * u);
                        DrawRect(g, p, ox + 13.5f * u, oy + 13.5f * u, 7.5f * u, 7.5f * u, 1.6f * u);
                        break;
                    case IconKind.Folder:
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 3 * u, oy + 19 * u),
                            new PointF(ox + 3 * u, oy + 6 * u),
                            new PointF(ox + 10 * u, oy + 6 * u),
                            new PointF(ox + 12 * u, oy + 9 * u),
                            new PointF(ox + 21 * u, oy + 9 * u),
                            new PointF(ox + 21 * u, oy + 19 * u),
                            new PointF(ox + 3 * u, oy + 19 * u) });
                        break;
                    case IconKind.FolderOpen:
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 3 * u, oy + 19 * u),
                            new PointF(ox + 3 * u, oy + 6 * u),
                            new PointF(ox + 10 * u, oy + 6 * u),
                            new PointF(ox + 12 * u, oy + 9 * u),
                            new PointF(ox + 19 * u, oy + 9 * u),
                            new PointF(ox + 19 * u, oy + 12 * u) });
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 6 * u, oy + 19 * u),
                            new PointF(ox + 21 * u, oy + 19 * u),
                            new PointF(ox + 23 * u, oy + 11 * u),
                            new PointF(ox + 9 * u, oy + 11 * u),
                            new PointF(ox + 6 * u, oy + 19 * u) });
                        break;
                    case IconKind.Rotate:
                        g.DrawArc(p, ox + 4 * u, oy + 4 * u, 16 * u, 16 * u, 70f, 250f);
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 19.5f * u, oy + 3 * u),
                            new PointF(ox + 20.5f * u, oy + 8.5f * u),
                            new PointF(ox + 15 * u, oy + 8 * u) });
                        break;
                    case IconKind.Gear:
                        g.DrawEllipse(p, ox + 9 * u, oy + 9 * u, 6 * u, 6 * u);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0;
                            float cx = ox + 12 * u, cy = oy + 12 * u;
                            float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                            g.DrawLine(p, cx + dx * 8f * u, cy + dy * 8f * u,
                                cx + dx * 10.6f * u, cy + dy * 10.6f * u);
                        }
                        g.DrawArc(p, ox + 4 * u, oy + 4 * u, 16 * u, 16 * u, 0f, 360f);
                        break;
                    case IconKind.Help:
                        g.DrawEllipse(p, ox + 3 * u, oy + 3 * u, 18 * u, 18 * u);
                        g.DrawArc(p, ox + 9 * u, oy + 6.5f * u, 6.5f * u, 6.5f * u, 180f, -230f);
                        g.DrawLine(p, ox + 12f * u, oy + 13f * u, ox + 12f * u, oy + 15.5f * u);
                        g.DrawLine(p, ox + 12f * u, oy + 18f * u, ox + 12f * u, 18.01f * u);
                        break;
                    case IconKind.Plus:
                        g.DrawLine(p, ox + 12 * u, oy + 5 * u, ox + 12 * u, oy + 19 * u);
                        g.DrawLine(p, ox + 5 * u, oy + 12 * u, ox + 19 * u, oy + 12 * u);
                        break;
                    case IconKind.ArrowLeft:
                        g.DrawLine(p, ox + 19 * u, oy + 12 * u, ox + 6 * u, oy + 12 * u);
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 11.5f * u, oy + 6 * u),
                            new PointF(ox + 5.5f * u, oy + 12 * u),
                            new PointF(ox + 11.5f * u, oy + 18 * u) });
                        break;
                    case IconKind.ArrowRight:
                        g.DrawLine(p, ox + 5 * u, oy + 12 * u, ox + 18 * u, oy + 12 * u);
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 12.5f * u, oy + 6 * u),
                            new PointF(ox + 18.5f * u, oy + 12 * u),
                            new PointF(ox + 12.5f * u, oy + 18 * u) });
                        break;
                    case IconKind.Check:
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 5 * u, oy + 12.5f * u),
                            new PointF(ox + 10 * u, oy + 17.5f * u),
                            new PointF(ox + 19 * u, oy + 6.5f * u) });
                        break;
                    case IconKind.Close:
                        g.DrawLine(p, ox + 6 * u, oy + 6 * u, ox + 18 * u, oy + 18 * u);
                        g.DrawLine(p, ox + 18 * u, oy + 6 * u, ox + 6 * u, oy + 18 * u);
                        break;
                    case IconKind.Minimize:
                        g.DrawLine(p, ox + 6 * u, oy + 12 * u, ox + 18 * u, oy + 12 * u);
                        break;
                    case IconKind.Maximize:
                        DrawRect(g, p, ox + 6 * u, oy + 6 * u, 12 * u, 12 * u, 1.6f * u);
                        break;
                    case IconKind.Restore:
                        DrawRect(g, p, ox + 5 * u, oy + 8 * u, 11 * u, 11 * u, 1.6f * u);
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 8 * u, oy + 8 * u),
                            new PointF(ox + 8 * u, oy + 5 * u),
                            new PointF(ox + 19 * u, oy + 5 * u),
                            new PointF(ox + 19 * u, oy + 16 * u),
                            new PointF(ox + 16 * u, oy + 16 * u) });
                        break;
                    case IconKind.Search:
                        g.DrawEllipse(p, ox + 4 * u, oy + 4 * u, 11 * u, 11 * u);
                        g.DrawLine(p, ox + 14.5f * u, oy + 14.5f * u, ox + 20 * u, oy + 20 * u);
                        break;
                    case IconKind.Trash:
                        g.DrawLine(p, ox + 4 * u, oy + 7 * u, ox + 20 * u, oy + 7 * u);
                        g.DrawLines(p, new PointF[] {
                            new PointF(ox + 6.5f * u, oy + 7 * u),
                            new PointF(ox + 7.5f * u, oy + 20 * u),
                            new PointF(ox + 16.5f * u, oy + 20 * u),
                            new PointF(ox + 17.5f * u, oy + 7 * u) });
                        g.DrawLine(p, ox + 10 * u, oy + 4 * u, ox + 14 * u, oy + 4 * u);
                        break;
                    case IconKind.Pause:
                        g.DrawLine(p, ox + 9.5f * u, oy + 6 * u, ox + 9.5f * u, oy + 18 * u);
                        g.DrawLine(p, ox + 14.5f * u, oy + 6 * u, ox + 14.5f * u, oy + 18 * u);
                        break;
                    case IconKind.Play:
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            g.FillPolygon(b, new PointF[] {
                                new PointF(ox + 8 * u, oy + 5 * u),
                                new PointF(ox + 19 * u, oy + 12 * u),
                                new PointF(ox + 8 * u, oy + 19 * u) });
                        }
                        break;
                }
            }
            g.Restore(st);
        }

        private static void DrawRect(Graphics g, Pen p, float x, float y, float w, float h, float r)
        {
            using (GraphicsPath path = Gfx.Round(new RectangleF(x, y, w, h), r))
            {
                g.DrawPath(p, path);
            }
        }
    }

    // ---- card surface ---------------------------------------------------

    internal class CardPanel : Panel, IThemed
    {
        private string title = "";
        private string note = "";
        private int pad = Theme.CardPad;

        public string Title { get { return title; } set { title = value ?? ""; Invalidate(); } }
        public string Note { get { return note; } set { note = value ?? ""; Invalidate(); } }
        public int Pad { get { return pad; } set { pad = value; Invalidate(); } }

        public bool HasHeader { get { return title.Length > 0; } }

        public int PadPx { get { return Gfx.S(this, pad); } }

        // Header height in device pixels (0 when the card has no header).
        public int HeaderPx
        {
            get
            {
                if (!HasHeader) return 0;
                int h = Gfx.S(this, 22);
                if (note.Length > 0) h += NotePx;
                return h + Gfx.S(this, 8);
            }
        }

        // The note wraps, up to two lines, the way "card-note" does in the
        // design. Measuring it here keeps HeaderPx and the painted text in
        // agreement - a hard-coded one-line height truncated the longer
        // notes with an ellipsis in the middle of a sentence.
        private int NotePx
        {
            get
            {
                int availW = Width - PadPx * 2;
                if (note.Length == 0 || availW <= 0) return 0;
                int lineH = Gfx.S(this, 18);
                Font f = Theme.UiFont(Theme.FsCardNote, FontStyle.Regular, DeviceDpi);
                Size s = TextRenderer.MeasureText(note, f, new Size(availW, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int lines = (int)Math.Ceiling(s.Height / (double)lineH);
                if (lines < 1) lines = 1;
                return Math.Min(lines, 2) * lineH;
            }
        }

        // Area left for child controls once the header and padding are out.
        public Rectangle Body
        {
            get
            {
                int p = PadPx;
                int top = p + HeaderPx;
                return new Rectangle(p, top, Math.Max(0, Width - p * 2),
                    Math.Max(0, Height - top - p));
            }
        }

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        // Place a child inside the card body using design coordinates that
        // are relative to Body. Placements are remembered and re-applied on
        // every resize, so callers can add children before the card has been
        // sized.
        public T AddChild<T>(T c, int x, int y, int w, int h) where T : Control
        {
            placements.Add(new Placement(c, x, y, w, h));
            Controls.Add(c);
            ApplyPlacements();
            return c;
        }

        // A fixed-size control pinned to the right edge of the card body, for
        // rows that read as "text on the left, control on the right". A plain
        // placement is absolute: it stretches when its right edge lands on the
        // design body width, which squeezes a fixed-width control into an
        // unreadable sliver once the window is narrower than the design. The
        // pinned control keeps its width and slides left instead.
        public T AddRightChild<T>(T c, int y, int w, int h) where T : Control
        {
            Placement p = new Placement(c, 0, y, w, h);
            p.Right = true;
            placements.Add(p);
            Controls.Add(c);
            ApplyPlacements();
            return c;
        }

        // A control pinned to the top-right of the card header, e.g. the
        // "add folder" button or an "adjust" link.
        public T AddHeaderControl<T>(T c, int w, int h) where T : Control
        {
            HeaderAction = c;
            headerPlacement = new Placement(c, 0, 0, w, h);
            Controls.Add(c);
            ApplyPlacements();
            return c;
        }

        private struct Placement
        {
            public Control C;
            public int X, Y, W, H;
            public bool Right;
            public Placement(Control c, int x, int y, int w, int h)
            {
                C = c; X = x; Y = y; W = w; H = h; Right = false;
            }
        }

        private readonly List<Placement> placements = new List<Placement>();
        private Placement headerPlacement;
        private Control headerAction;

        public Control HeaderAction
        {
            get { return headerAction; }
            set { headerAction = value; ApplyPlacements(); }
        }

        public void ApplyPlacements()
        {
            Rectangle b = Body;
            // A placement whose right edge lands on the design body width is
            // meant to run to the card's edge, so it stretches with the card.
            // Without this the content stayed 684 wide on a wider window and
            // left a growing gap on the right.
            int designW = DesignBodyWidth;
            int gap = Gfx.S(this, 12);

            // A right-pinned control keeps its own width and slides left as
            // the card narrows. Text placements sharing its row have to stop
            // short of it, or the two overlap once the body is narrower than
            // the design's 684.
            Placement pin = default(Placement);
            bool hasPin = false;
            foreach (Placement p in placements)
            {
                if (!p.Right || p.C == null) continue;
                if (!hasPin || p.X + p.W < pin.X + pin.W) { pin = p; hasPin = true; }
            }
            int pinX = hasPin ? b.Right - Gfx.S(this, pin.W) : int.MaxValue;
            int pinTop = hasPin ? b.Y + Gfx.S(this, pin.Y) : 0;
            int pinBottom = hasPin ? pinTop + Gfx.S(this, pin.H) : 0;

            foreach (Placement p in placements)
            {
                if (p.C == null) continue;
                int w = Gfx.S(this, p.W);
                int x = b.X + Gfx.S(this, p.X);

                if (p.Right)
                {
                    x = b.Right - w;
                }
                else
                {
                    if (designW > 0 && p.X + p.W >= designW)
                        w = Math.Max(0, b.Width - Gfx.S(this, p.X));

                    // Only the row the pinned control sits on is clipped: a
                    // full-width rule above it stays full width.
                    int top = b.Y + Gfx.S(this, p.Y);
                    int bottom = top + Gfx.S(this, p.H);
                    bool sameRow = hasPin && top < pinBottom && pinTop < bottom;
                    if (sameRow && x + w > pinX - gap) w = Math.Max(0, pinX - gap - x);

                    // Nothing may spill outside the card. The design lays some
                    // children out against a 684px body, so on a narrower card
                    // they would otherwise be painted past the rounded edge.
                    w = Math.Min(w, Math.Max(0, b.Right - x));
                }

                // A segmented control whose items wrap needs its extra rows to
                // be visible; the design only ever gives it one row's height.
                int h = Gfx.S(this, p.H);
                SegmentedControl seg = p.C as SegmentedControl;
                if (seg != null) h = Math.Max(h, seg.MeasureHeight(w));
                // A source list is one row taller every time a folder is added,
                // so its own answer beats the height it was placed with.
                SourceList list = p.C as SourceList;
                if (list != null) h = Math.Max(h, Gfx.S(this, list.PreferredHeight));

                p.C.SetBounds(x, b.Y + Gfx.S(this, p.Y), w, h);
            }
            if (headerPlacement.C != null)
            {
                int p = PadPx;
                int w = Gfx.S(this, headerPlacement.W);
                int h = Gfx.S(this, headerPlacement.H);
                headerPlacement.C.SetBounds(Math.Max(p, Width - p - w),
                    Math.Max(0, (Gfx.S(this, 22) + Gfx.S(this, 8) - h) / 2 + p / 2),
                    w, h);
            }
        }

        // Height this card needs for nothing to be cut off. It is the design
        // height in the normal case, and grows when a wrapped segmented
        // control needs a second row that the design did not budget for.
        public int ContentHeight
        {
            get
            {
                int bottom = Body.Top;
                foreach (Placement p in placements)
                {
                    if (p.C == null || !p.C.Visible) continue;
                    bottom = Math.Max(bottom, p.C.Bottom + PadPx);
                }
                return bottom;
            }
        }

        // The body width the caller designed against - the widest X+W any
        // placement uses. Recorded on first layout, when the design values
        // are the only ones available.
        private int DesignBodyWidth
        {
            get
            {
                int max = 0;
                foreach (Placement p in placements)
                {
                    if (p.X + p.W > max) max = p.X + p.W;
                }
                return max;
            }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Surface;
            ForeColor = Theme.Fore;
            Invalidate();
        }

        // For a child that changed its own size after a layout pass - a list
        // that gained a row, say. Re-runs the placements and asks the page to
        // grow the card, because the height this card needs is only knowable
        // once the children know theirs.
        public void Relayout()
        {
            ApplyPlacements();
            Control p = Parent;
            while (p != null)
            {
                PageStack ps = p as PageStack;
                if (ps != null) { ps.RelayoutNow(); break; }
                p = p.Parent;
            }
            Invalidate(true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyPlacements();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int p = PadPx;
            float rad = Gfx.S(this, Theme.RadCard);

            g.Clear(Parent != null ? Parent.BackColor : Theme.FormBack);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Gfx.FillRound(g, r, rad, Theme.Surface);
            Gfx.StrokeRound(g, r, rad, Theme.Border, 1f);

            if (HasHeader)
            {
                int y = p;
                Gfx.Text(g, title, Theme.UiFont(Theme.FsCardTitle, FontStyle.Bold, DeviceDpi),
                    Theme.Fore,
                    new Rectangle(p, y, Width - p * 2, Gfx.S(this, 22)),
                    Gfx.Ellipsis(Gfx.LeftMid));
                y += Gfx.S(this, 22);
                if (note.Length > 0)
                {
                    Gfx.Text(g, note, Theme.UiFont(this, Theme.FsCardNote), Theme.ForeMuted,
                        new Rectangle(p, y, Width - p * 2, NotePx), Gfx.LeftTop);
                }
            }
            base.OnPaint(e);
        }
    }

    // ---- buttons --------------------------------------------------------

    internal enum BtnKind { Primary, Secondary, Ghost, DangerGhost }

    internal class FlatButton : Button, IThemed
    {
        private bool hover;
        private bool down;
        private BtnKind kind = BtnKind.Secondary;
        private IconKind? icon;
        private bool compact;
        private bool iconTrailing;
        private bool alignLeft;

        public BtnKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        public IconKind? Icon
        {
            get { return icon; }
            set { icon = value; Invalidate(); }
        }

        public bool Compact
        {
            get { return compact; }
            set { compact = value; Invalidate(); }
        }

        // Draw the glyph after the label instead of before it.
        public bool IconTrailing
        {
            get { return iconTrailing; }
            set { iconTrailing = value; Invalidate(); }
        }

        // Left-aligned label instead of centred, for buttons used as a list
        // (the help window's section tabs).
        public bool AlignLeft
        {
            get { return alignLeft; }
            set { alignLeft = value; Invalidate(); }
        }

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Theme.FormBack;
            ForeColor = Theme.Fore;
            TabStop = true;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            ForeColor = Theme.Fore;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; base.OnMouseUp(e); Invalidate(); }

        // Keep the base class from painting anything: we own the surface.
        protected override void OnPaintBackground(PaintEventArgs pevent) { }

        private void Palette(out Color back, out Color fore, out Color border)
        {
            bool hot = hover || down;
            switch (kind)
            {
                case BtnKind.Primary:
                    back = down ? Theme.SolidActive : (hover ? Theme.SolidHover : Theme.Solid);
                    fore = Theme.AccentFore;
                    border = back;
                    break;
                case BtnKind.Ghost:
                    // .btn-ghost is muted until hovered, then fg on a hover fill.
                    back = hot ? Theme.Hover : Color.Transparent;
                    fore = hot ? Theme.Fore : Theme.ForeMuted;
                    border = Color.Transparent;
                    break;
                case BtnKind.DangerGhost:
                    back = hot ? Theme.WarnSoft : Color.Transparent;
                    fore = hot ? Theme.Warn : Theme.ForeMuted;
                    border = Color.Transparent;
                    break;
                default:
                    // .btn-secondary is transparent over the card it sits on.
                    back = hot ? Theme.Hover : Color.Transparent;
                    fore = Theme.Fore;
                    border = hover ? Theme.ForeMuted : Theme.Border;
                    break;
            }
            if (!Enabled)
            {
                if (kind == BtnKind.Primary) { back = Theme.DisabledBorder; fore = Theme.DisabledText; }
                else { fore = Theme.DisabledText; border = Theme.DisabledBorder; }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.FormBack);

            Color back, fore, border;
            Palette(out back, out fore, out border);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = Gfx.S(this, compact ? Theme.RadBtnSm : Theme.RadBtn);
            if (back.A > 0) Gfx.FillRound(g, r, rad, back);
            if (border.A > 0 && kind != BtnKind.Primary) Gfx.StrokeRound(g, r, rad, border, 1f);

            Font f = Theme.UiFont(this, compact ? Theme.FsSub : Theme.FsBodySm);
            int iconW = icon.HasValue ? Gfx.S(this, 16) : 0;
            int gap = icon.HasValue ? Gfx.S(this, 6) : 0;
            Size textSize = TextRenderer.MeasureText(Text, f);
            int total = iconW + gap + textSize.Width;
            int x = alignLeft ? Gfx.S(this, 14) : (Width - total) / 2;
            int textW = alignLeft
                ? Math.Max(0, Width - x - Gfx.S(this, 10))
                : Math.Max(0, textSize.Width + 4);
            if (icon.HasValue && iconTrailing)
            {
                // "下一张壁纸 →": label first, arrow after it.
                Gfx.Text(g, Text, f, fore,
                    new Rectangle(x, 0, textW, Height),
                    alignLeft ? Gfx.Ellipsis(Gfx.LeftMid) : Gfx.LeftMid);
                x += gap + textSize.Width;
                RectangleF ib = new RectangleF(x, (Height - Gfx.S(this, 16)) / 2f,
                    Gfx.S(this, 16), Gfx.S(this, 16));
                IconPainter.Draw(g, icon.Value, ib, fore, Gfx.Scale(this) * 1.6f);
                if (Focused && Enabled) Gfx.FocusRing(g, r, rad, this);
                return;
            }
            if (icon.HasValue)
            {
                RectangleF ib = new RectangleF(x, (Height - Gfx.S(this, 16)) / 2f,
                    Gfx.S(this, 16), Gfx.S(this, 16));
                IconPainter.Draw(g, icon.Value, ib, fore, Gfx.Scale(this) * 1.6f);
                x += iconW + gap;
                textW = alignLeft ? Math.Max(0, Width - x - Gfx.S(this, 10)) : textW;
            }
            Gfx.Text(g, Text, f, fore,
                new Rectangle(x, 0, alignLeft ? textW : Math.Max(0, Width - x), Height),
                alignLeft ? Gfx.Ellipsis(Gfx.LeftMid) : Gfx.LeftMid);

            if (Focused && Enabled) Gfx.FocusRing(g, r, rad, this);
        }
    }

    // ---- segmented control ----------------------------------------------

    // Items wrap onto extra rows when the control is too narrow for one.
    internal class SegmentedControl : Control, IThemed
    {
        private string[] items = new string[0];
        private int selected;
        private int hotIndex = -1;
        private int rows = 1;
        private readonly System.Collections.Generic.List<Rectangle> itemRects =
            new System.Collections.Generic.List<Rectangle>();

        public event EventHandler SelectedIndexChanged;

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; LayoutItems(); Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = items.Length == 0 ? 0 : Math.Max(0, Math.Min(items.Length - 1, value));
                if (v == selected) return;
                selected = v;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.SegItemH + 8;
        }

        public void ApplyTheme()
        {
            Invalidate();
        }

        // Height needed for the current items at the current width.
        public int MeasureHeight(int width)
        {
            LayoutItems(width);
            return rows * Gfx.S(this, Theme.SegItemH) + (rows - 1) * Gfx.S(this, 2) + Gfx.S(this, 6);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutItems();
        }

        private void LayoutItems() { LayoutItems(Width); }

        private void LayoutItems(int width)
        {
            itemRects.Clear();
            int inner = Gfx.S(this, 3);
            int gap = Gfx.S(this, 2);
            int itemH = Gfx.S(this, Theme.SegItemH);
            int avail = Math.Max(0, width - inner * 2);
            if (items.Length == 0 || avail <= 0) { rows = Math.Max(1, rows); return; }

            Size[] sizes = new Size[items.Length];
            Font f = Theme.UiFont(this, Theme.FsSub);
            int total = 0;
            int maxW = 0;
            for (int i = 0; i < items.Length; i++)
            {
                Size s = TextRenderer.MeasureText(items[i], f);
                sizes[i] = new Size(s.Width + Gfx.S(this, 22), itemH);
                total += sizes[i].Width;
                maxW = Math.Max(maxW, sizes[i].Width);
            }
            total += gap * (items.Length - 1);

            if (total <= avail)
            {
                rows = 1;
                int x = inner;
                int y = inner;
                for (int i = 0; i < items.Length; i++)
                {
                    itemRects.Add(new Rectangle(x, y, sizes[i].Width, itemH));
                    x += sizes[i].Width + gap;
                }
                return;
            }

            // Wrap into rows, then stretch each row's items to fill the width
            // so the container reads as a tidy grid.
            System.Collections.Generic.List<System.Collections.Generic.List<int>> lines =
                new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
            System.Collections.Generic.List<int> cur = new System.Collections.Generic.List<int>();
            int used = 0;
            for (int i = 0; i < items.Length; i++)
            {
                int need = sizes[i].Width + (cur.Count > 0 ? gap : 0);
                if (cur.Count > 0 && used + need > avail)
                {
                    lines.Add(cur);
                    cur = new System.Collections.Generic.List<int>();
                    used = 0;
                    need = sizes[i].Width;
                }
                cur.Add(i);
                used += need;
            }
            if (cur.Count > 0) lines.Add(cur);
            rows = Math.Max(1, lines.Count);

            int y2 = inner;
            foreach (System.Collections.Generic.List<int> ln in lines)
            {
                int count = ln.Count;
                int baseW = 0;
                foreach (int idx in ln) baseW += sizes[idx].Width;
                int extra = avail - baseW - gap * (count - 1);
                int add = count > 0 ? extra / count : 0;
                int rem = count > 0 ? extra % count : 0;
                int x2 = inner;
                for (int k = 0; k < count; k++)
                {
                    int w = sizes[ln[k]].Width + add + (k < rem ? 1 : 0);
                    itemRects.Add(new Rectangle(x2, y2, w, itemH));
                    x2 += w + gap;
                }
                y2 += itemH + gap;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = Hit(e.Location);
            if (idx != hotIndex) { hotIndex = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hotIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = Hit(e.Location);
            if (idx >= 0)
            {
                Focus();
                SelectedIndex = idx;
            }
        }

        private int Hit(Point p)
        {
            for (int i = 0; i < itemRects.Count; i++)
            {
                if (itemRects[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left && selected > 0) SelectedIndex = selected - 1;
            else if (e.KeyCode == Keys.Right && selected < items.Length - 1) SelectedIndex = selected + 1;
            else return;
            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.FormBack);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = Gfx.S(this, Theme.RadSeg);
            Gfx.FillRound(g, r, rad, Theme.Subtle);
            Gfx.StrokeRound(g, r, rad, Theme.Border, 1f);

            Font f = Theme.UiFont(this, Theme.FsSub);
            Font fBold = Theme.UiFont(Theme.FsSub, FontStyle.Bold, DeviceDpi);
            float itemRad = Gfx.S(this, Theme.RadSegItem);

            for (int i = 0; i < itemRects.Count && i < items.Length; i++)
            {
                Rectangle rc = itemRects[i];
                bool sel = i == selected;
                if (sel)
                {
                    RectangleF rr = new RectangleF(rc.X, rc.Y, rc.Width, rc.Height);
                    Gfx.FillRound(g, rr, itemRad, Theme.SegOn);
                    Gfx.StrokeRound(g, rr, itemRad, Theme.Border, 1f);
                }
                // No hover fill: the design only lifts the label colour
                // (.seg button:hover{color:var(--fg)}); the track stays clear
                // so the active block remains the single focal point.
                // The idle label uses MutedStrong, not ForeMuted: the track
                // is --subtle, where ForeMuted only reaches 4.27:1 (light) /
                // 4.47:1 (dark). MutedStrong clears 7.43:1 / 6.42:1.
                Gfx.Text(g, items[i], sel ? fBold : f,
                    (sel || i == hotIndex) ? Theme.Fore : Theme.MutedStrong,
                    rc, Gfx.Ellipsis(Gfx.CenterMid));
            }

            if (Focused && Enabled) Gfx.FocusRing(g, r, rad, this);
        }
    }

    // ---- toggle switch --------------------------------------------------

    // ---- on/off switch ---------------------------------------------------

    // The switch is drawn in two places (the standalone control and inside a
    // source row), so the rendering lives here once. Anything else and the two
    // drift apart the first time a colour changes.
    internal static class SwitchDraw
    {
        public static void Draw(Graphics g, Control c, RectangleF box, bool on,
            bool hover, bool enabled, bool focused)
        {
            Gfx.FillRound(g, box, box.Height / 2f, enabled
                ? (on ? (hover ? Theme.SolidHover : Theme.Solid) : (hover ? Theme.SwitchHover : Theme.Border))
                : Theme.DisabledBorder);

            int knob = Gfx.S(c, Theme.SwitchKnob);
            float kx = on ? (box.Right - knob - Gfx.S(c, 3)) : box.X + Gfx.S(c, 3);
            float ky = box.Y + (box.Height - knob) / 2f;
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
            {
                g.FillEllipse(shadow, kx, ky + 1f, knob, knob);
            }
            using (SolidBrush b = new SolidBrush(enabled ? Color.White : Color.FromArgb(200, 255, 255, 255)))
            {
                g.FillEllipse(b, kx, ky, knob, knob);
            }
            if (focused && enabled) Gfx.FocusRing(g, box, box.Height / 2f, c);
        }
    }

    internal class ToggleSwitch : Control, IThemed
    {
        private bool isOn;
        private bool hover;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isOn; }
            set
            {
                if (isOn == value) return;
                isOn = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.SwitchW, Theme.SwitchH);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public void ApplyTheme() { Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            Focus();
            Checked = !Checked;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && Enabled)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            SwitchDraw.Draw(g, this, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
                isOn, hover, Enabled, Focused);
        }
    }

    // ---- left nav rail --------------------------------------------------

    internal class NavRail : Control, IThemed
    {
        private string[] items = new string[0];
        private IconKind[] icons = new IconKind[0];
        private int selected;
        private int hotIndex = -1;
        private Rectangle cardRect;
        private readonly System.Collections.Generic.List<Rectangle> itemRects =
            new System.Collections.Generic.List<Rectangle>();

        private readonly FlatButton pauseBtn;
        private bool rotating = true;

        public event EventHandler SelectedIndexChanged;
        public event EventHandler PauseClicked;

        public string DotCaption = "";      // "rotating" / "paused"
        public string Countdown = "";       // "12:34"
        public string CountdownCaption = "";// "until next change"

        public NavRail()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = Theme.RailW;

            pauseBtn = new FlatButton();
            pauseBtn.Kind = BtnKind.Secondary;
            pauseBtn.Compact = true;
            pauseBtn.Height = Theme.BtnSmallH;
            pauseBtn.Click += delegate { EventHandler h = PauseClicked; if (h != null) h(this, EventArgs.Empty); };
            Controls.Add(pauseBtn);
        }

        public bool Rotating
        {
            get { return rotating; }
            set { rotating = value; Invalidate(); }
        }

        public string PauseText
        {
            get { return pauseBtn.Text; }
            set { pauseBtn.Text = value ?? ""; Invalidate(); }
        }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; LayoutChildren(); Invalidate(); }
        }

        public IconKind[] Icons
        {
            get { return icons; }
            set { icons = value ?? new IconKind[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = items.Length == 0 ? 0 : Math.Max(0, Math.Min(items.Length - 1, value));
                if (v == selected) return;
                selected = v;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            ForeColor = Theme.Fore;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutChildren();
        }

        public void LayoutChildren()
        {
            // OnSizeChanged can fire while the constructor is still setting
            // the initial Width, before the pause button exists.
            if (pauseBtn == null) return;
            int top = Gfx.S(this, 12);
            int itemH = Gfx.S(this, Theme.RailItemH);
            int gap = Gfx.S(this, 2);
            itemRects.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                itemRects.Add(new Rectangle(Gfx.S(this, 12), top + i * (itemH + gap),
                    Math.Max(0, Width - Gfx.S(this, 24)), itemH));
            }

            int cardH = Gfx.S(this, 118);
            cardRect = new Rectangle(Gfx.S(this, 12), Math.Max(0, Height - Gfx.S(this, 12) - cardH),
                Math.Max(0, Width - Gfx.S(this, 24)), cardH);

            int bx = cardRect.X + Gfx.S(this, 12);
            int bw = Math.Max(0, cardRect.Width - Gfx.S(this, 24));
            pauseBtn.SetBounds(bx, cardRect.Bottom - Gfx.S(this, 12) - Gfx.S(this, Theme.BtnSmallH),
                bw, Gfx.S(this, Theme.BtnSmallH));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = Hit(e.Location);
            if (idx != hotIndex) { hotIndex = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hotIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = Hit(e.Location);
            if (idx >= 0) SelectedIndex = idx;
        }

        private int Hit(Point p)
        {
            for (int i = 0; i < itemRects.Count; i++)
            {
                if (itemRects[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.FormBack);

            Font f = Theme.UiFont(this, Theme.FsBody);
            Font fBold = Theme.UiFont(Theme.FsBody, FontStyle.Bold, DeviceDpi);
            float rad = Gfx.S(this, Theme.RadRailItem);

            for (int i = 0; i < itemRects.Count; i++)
            {
                Rectangle rc = itemRects[i];
                bool sel = i == selected;
                if (sel)
                {
                    Gfx.FillRound(g, rc, rad, Theme.Selected);
                    RectangleF bar = new RectangleF(rc.X - Gfx.S(this, 12), rc.Y + Gfx.S(this, 10),
                        Gfx.S(this, 3), rc.Height - Gfx.S(this, 20));
                    Gfx.FillRound(g, bar, Gfx.S(this, 1.5f), Theme.AccentText);
                }
                else if (i == hotIndex)
                {
                    Gfx.FillRound(g, rc, rad, Theme.Hover);
                }

                // .rail-item:hover swaps the fill only - the label colour is
                // deliberately left alone (handoff 2.2).
                Color ink = sel ? Theme.Fore : Theme.ForeMuted;
                int iconS = Gfx.S(this, 17);
                if (i < icons.Length)
                {
                    IconPainter.Draw(g, icons[i],
                        new RectangleF(rc.X + Gfx.S(this, 10), rc.Y + (rc.Height - iconS) / 2f, iconS, iconS),
                        ink, Math.Max(1f, Gfx.Scale(this) * 1.5f));
                }
                Gfx.Text(g, i < items.Length ? items[i] : "", sel ? fBold : f, ink,
                    new Rectangle(rc.X + Gfx.S(this, 10) + iconS + Gfx.S(this, 10), rc.Y,
                        Math.Max(0, rc.Width - iconS - Gfx.S(this, 30)), rc.Height),
                    Gfx.Ellipsis(Gfx.LeftMid));
            }

            // status card: .rail-status sits on --surface at radius 10
            Gfx.FillRound(g, cardRect, Gfx.S(this, Theme.RadRailCard), Theme.Surface);
            Gfx.StrokeRound(g, cardRect, Gfx.S(this, Theme.RadRailCard), Theme.Border, 1f);

            int px = cardRect.X + Gfx.S(this, 12);
            int py = cardRect.Y + Gfx.S(this, 12);

            int dot = Gfx.S(this, 7);
            using (SolidBrush b = new SolidBrush(rotating ? Theme.Ok : Theme.ForeMuted))
            {
                g.FillEllipse(b, px, py + Gfx.S(this, 6), dot, dot);
            }
            Gfx.Text(g, DotCaption, Theme.UiFont(this, Theme.FsSub), Theme.Fore,
                new Rectangle(px + dot + Gfx.S(this, 7), py, cardRect.Width - dot - Gfx.S(this, 31),
                    Gfx.S(this, 18)), Gfx.Ellipsis(Gfx.LeftMid));

            py += Gfx.S(this, 22);
            Gfx.Text(g, Countdown, Theme.MonoFont(this, Theme.FsRailCount), Theme.Fore,
                new Rectangle(px, py, cardRect.Width - Gfx.S(this, 24), Gfx.S(this, 28)), Gfx.LeftMid);

            py += Gfx.S(this, 27);
            Gfx.Text(g, CountdownCaption, Theme.UiFont(this, Theme.FsCap), Theme.ForeMuted,
                new Rectangle(px, py, cardRect.Width - Gfx.S(this, 24), Gfx.S(this, 17)),
                Gfx.Ellipsis(Gfx.LeftMid));

            // separator on the right edge
            Gfx.Line(g, Width - 0.5f, 0, Width - 0.5f, Height, Theme.Border, 1f);
        }
    }

    // ---- bottom status bar ----------------------------------------------

    internal class StatusBar : Control, IThemed
    {
        private readonly FlatButton saveBtn;
        private string mainText = "";
        private string subText = "";
        private string dirtyText = "";
        private bool dirty;
        private bool healthy = true;

        public event EventHandler SaveClicked;

        public StatusBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.StatusBarH;

            saveBtn = new FlatButton();
            saveBtn.Kind = BtnKind.Secondary;
            saveBtn.Height = Theme.BtnH;
            saveBtn.Click += delegate { EventHandler h = SaveClicked; if (h != null) h(this, EventArgs.Empty); };
            Controls.Add(saveBtn);
        }

        public string MainText
        {
            get { return mainText; }
            set { mainText = value ?? ""; Invalidate(); }
        }

        public string SubText
        {
            get { return subText; }
            set { subText = value ?? ""; Invalidate(); }
        }

        public string DirtyText
        {
            get { return dirtyText; }
            set { dirtyText = value ?? ""; LayoutChildren(); Invalidate(); }
        }

        public string SaveText
        {
            get { return saveBtn.Text; }
            set { saveBtn.Text = value ?? ""; LayoutChildren(); Invalidate(); }
        }

        public bool Healthy
        {
            get { return healthy; }
            set { healthy = value; Invalidate(); }
        }

        public bool Dirty
        {
            get { return dirty; }
            set
            {
                if (dirty == value) return;
                dirty = value;
                // Primary-button mutex: while there is nothing to save the
                // button steps back and the page's own primary action stays
                // the visual anchor.
                saveBtn.Kind = dirty ? BtnKind.Primary : BtnKind.Secondary;
                LayoutChildren();
                Invalidate();
            }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            ForeColor = Theme.Fore;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutChildren();
        }

        public void LayoutChildren()
        {
            if (saveBtn == null) return;
            int padRight = Gfx.S(this, 20);
            int bw = TextRenderer.MeasureText(saveBtn.Text,
                Theme.UiFont(this, Theme.FsBodySm)).Width + Gfx.S(this, 28);
            bw = Math.Max(bw, Gfx.S(this, 92));
            saveBtn.SetBounds(Width - padRight - bw, (Height - Gfx.S(this, Theme.BtnH)) / 2,
                bw, Gfx.S(this, Theme.BtnH));
        }

        // Width the dirty chip wants, in device px (0 when clean).
        private int ChipWidth()
        {
            if (!dirty || dirtyText.Length == 0) return 0;
            Font f = Theme.UiFont(this, Theme.FsCap);
            return TextRenderer.MeasureText(dirtyText, f).Width + Gfx.S(this, 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.FormBack);
            Gfx.Line(g, 0, 0.5f, Width, 0.5f, Theme.Border, 1f);

            int padLeft = Gfx.S(this, 20);
            int cy = Height / 2;
            int dot = Gfx.S(this, 8);
            using (SolidBrush b = new SolidBrush(healthy ? Theme.Ok : Theme.Warn))
            {
                g.FillEllipse(b, padLeft, cy - dot / 2f, dot, dot);
            }
            int x = padLeft + dot + Gfx.S(this, 9);

            Font fm = Theme.UiFont(this, Theme.FsSub);
            Size ms = TextRenderer.MeasureText(mainText, fm);
            Gfx.Text(g, mainText, fm, Theme.Fore,
                new Rectangle(x, 0, Math.Max(0, Width - x), Height),
                Gfx.Ellipsis(Gfx.LeftMid));
            x += ms.Width + Gfx.S(this, 14);

            if (subText.Length > 0)
            {
                // .sb-sub is a plain muted mono run - no pill behind it.
                Font fs = Theme.MonoFont(this, Theme.FsMonoSm);
                Size ss = TextRenderer.MeasureText(subText, fs);
                int sw = Math.Min(ss.Width + Gfx.S(this, 4), Math.Max(0, Width - x - Gfx.S(this, 20)));
                if (sw > Gfx.S(this, 30))
                {
                    Gfx.Text(g, subText, fs, Theme.ForeMuted,
                        new Rectangle(x, 0, sw, Height), Gfx.Ellipsis(Gfx.LeftMid));
                }
            }

            int chipW = ChipWidth();
            if (chipW > 0)
            {
                // .dirty-chip is an accent dot plus muted text, not a chip
                // with its own fill and border.
                Font fc = Theme.UiFont(this, Theme.FsCap);
                int cx = saveBtn.Left - Gfx.S(this, 14) - chipW;
                if (cx > x)
                {
                    int cd = Gfx.S(this, 6);
                    int cyy = cy - Gfx.S(this, 12);
                    using (SolidBrush b = new SolidBrush(Theme.AccentText))
                    {
                        g.FillEllipse(b, cx, cyy + (Gfx.S(this, 24) - cd) / 2f, cd, cd);
                    }
                    Gfx.Text(g, dirtyText, fc, Theme.ForeMuted,
                        new Rectangle(cx + cd + Gfx.S(this, 7), cyy,
                            Math.Max(0, chipW - cd - Gfx.S(this, 7)), Gfx.S(this, 24)),
                        Gfx.Ellipsis(Gfx.LeftMid));
                }
            }
        }
    }

    // ---- title bar ------------------------------------------------------

    internal sealed class WindowBtn : Control, IThemed
    {
        public IconKind Kind;
        public bool IsClose;
        private bool hover;

        public WindowBtn(IconKind kind, bool isClose)
        {
            Kind = kind;
            IsClose = isClose;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.WinBtnSize, Theme.WinBtnSize);
            TabStop = false;
            Cursor = Cursors.Default;
        }

        public void ApplyTheme() { Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.FormBack);
            if (hover)
            {
                Color back = IsClose ? Theme.CloseHover : Theme.Hover;
                using (SolidBrush b = new SolidBrush(back))
                {
                    g.FillRectangle(b, ClientRectangle);
                }
            }
            Color ink = (hover && IsClose) ? Color.White : (hover ? Theme.Fore : Theme.ForeMuted);
            float size = Gfx.S(this, 15);
            RectangleF box = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            IconPainter.Draw(g, Kind, box, ink, Math.Max(1f, Gfx.Scale(this) * 1.3f));
        }
    }

    // 46px custom title bar. Dragging is delegated to the system with
    // WM_NCLBUTTONDOWN/HTCAPTION, which keeps Aero snap, double-click
    // maximise and the window menu working for free.
    internal sealed class TitleBar : Control, IThemed
    {
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONUP = 0x00A5;
        private const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly WindowBtn minBtn;
        private readonly WindowBtn maxBtn;
        private readonly WindowBtn closeBtn;
        private readonly FlatButton helpBtn;
        private string title = "";
        private string version = "";
        private Image appIcon;

        public event EventHandler HelpClicked;

        public TitleBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.TitleBarH;

            closeBtn = new WindowBtn(IconKind.Close, true);
            maxBtn = new WindowBtn(IconKind.Maximize, false);
            minBtn = new WindowBtn(IconKind.Minimize, false);
            helpBtn = new FlatButton();
            helpBtn.Kind = BtnKind.Ghost;
            helpBtn.Compact = true;
            helpBtn.Height = Theme.BtnSmallH;
            helpBtn.Click += delegate { EventHandler h = HelpClicked; if (h != null) h(this, EventArgs.Empty); };

            Controls.Add(helpBtn);
            Controls.Add(minBtn);
            Controls.Add(maxBtn);
            Controls.Add(closeBtn);

            minBtn.Click += delegate { Form f = FindForm(); if (f != null) f.WindowState = FormWindowState.Minimized; };
            maxBtn.Click += delegate { ToggleMaximize(); };
            closeBtn.Click += delegate { Form f = FindForm(); if (f != null) f.Close(); };
        }

        public string TitleText
        {
            get { return title; }
            set { title = value ?? ""; Invalidate(); }
        }

        public string VersionText
        {
            get { return version; }
            set { version = value ?? ""; Invalidate(); }
        }

        public string HelpText
        {
            get { return helpBtn.Text; }
            set { helpBtn.Text = value ?? ""; LayoutChildren(); Invalidate(); }
        }

        public Image AppIcon
        {
            get { return appIcon; }
            set { appIcon = value; Invalidate(); }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            ForeColor = Theme.Fore;
            Invalidate();
        }

        private void ToggleMaximize()
        {
            Form f = FindForm();
            if (f == null) return;
            bool maximizable = f.MaximizeBox;
            if (!maximizable) return;
            f.WindowState = f.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        // Reflect the real window state on the middle button.
        public void SyncWindowState(FormWindowState state)
        {
            maxBtn.Kind = state == FormWindowState.Maximized
                ? IconKind.Restore : IconKind.Maximize;
            maxBtn.Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            // OnSizeChanged can fire while the constructor is still setting
            // the initial Height, before the buttons exist.
            if (closeBtn == null || maxBtn == null || minBtn == null || helpBtn == null) return;
            int bs = Gfx.S(this, Theme.WinBtnSize);
            int h = Gfx.S(this, Theme.WinBtnSize);
            closeBtn.SetBounds(Width - bs, 0, bs, h);
            maxBtn.SetBounds(Width - bs * 2, 0, bs, h);
            minBtn.SetBounds(Width - bs * 3, 0, bs, h);

            Font f = Theme.UiFont(this, Theme.FsSub);
            int tw = string.IsNullOrEmpty(helpBtn.Text) ? 0
                : TextRenderer.MeasureText(helpBtn.Text, f).Width + Gfx.S(this, 20);
            helpBtn.SetBounds(Math.Max(0, Width - bs * 3 - Gfx.S(this, 10) - tw),
                (Height - Gfx.S(this, Theme.BtnSmallH)) / 2, tw, Gfx.S(this, Theme.BtnSmallH));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCLBUTTONDOWN || m.Msg == WM_NCRBUTTONUP)
            {
                // Let the system run its caption loop.
                ReleaseCapture();
                SendMessage(Handle, m.Msg, (IntPtr)HTCAPTION, IntPtr.Zero);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.FormBack);

            int x = Gfx.S(this, 14);
            int iconSize = Gfx.S(this, 19);
            if (appIcon != null)
            {
                g.DrawImage(appIcon, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize));
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Theme.AccentText))
                {
                    g.FillEllipse(b, x, (Height - iconSize) / 2f, iconSize, iconSize);
                }
            }
            x += iconSize + Gfx.S(this, 10);

            Font ft = Theme.UiFont(Theme.FsCardTitle, FontStyle.Bold, DeviceDpi);
            Size ts = TextRenderer.MeasureText(title, ft);
            Gfx.Text(g, title, ft, Theme.Fore,
                new Rectangle(x, 0, Math.Max(0, ts.Width + 4), Height), Gfx.LeftMid);
            x += ts.Width + Gfx.S(this, 10);

            if (version.Length > 0)
            {
                // .tb-sub: a plain mono 11 muted run next to the title.
                Font fm = Theme.MonoFont(this, Theme.FsMono);
                Size vs = TextRenderer.MeasureText(version, fm);
                Gfx.Text(g, version, fm, Theme.ForeMuted,
                    new Rectangle(x, 0, Math.Max(0, vs.Width + 4), Height), Gfx.LeftMid);
            }

            Gfx.Line(g, 0, Height - 0.5f, Width, Height - 0.5f, Theme.Border, 1f);
        }
    }

    // ---- page container -------------------------------------------------

    // Stacks cards down a page using the design's page padding and card gap.
    // Cards are added by design height and laid out on every resize, so a
    // caller can build a page before the page has ever been sized.
    internal class PageStack : Panel, IThemed
    {
        private readonly List<CardPanel> cards = new List<CardPanel>();
        private readonly List<int> heights = new List<int>();
        private PageHead head;

        public PageStack()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Fill;
            BackColor = Theme.FormBack;
            AutoScroll = true;
        }

        // Optional page header ("h2.ph" + "p.ph-sub").
        public PageHead Head
        {
            get
            {
                if (head == null)
                {
                    head = new PageHead();
                    head.SetBounds(0, 0, 10, 10);
                    Controls.Add(head);
                    Relayout();
                }
                return head;
            }
        }

        public CardPanel AddCard(int height)
        {
            CardPanel c = new CardPanel();
            cards.Add(c);
            heights.Add(height);
            Controls.Add(c);
            Relayout();
            return c;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            ForeColor = Theme.Fore;
            Invalidate(true);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Relayout();
        }

        // Public because a card may have to grow after its own children
        // resized themselves (see CardPanel.Relayout).
        public void RelayoutNow()
        {
            Relayout();
        }

        private void Relayout()
        {
            int padX = Gfx.S(this, Theme.PadX);
            int top = Gfx.S(this, Theme.PadTop);
            int gap = Gfx.S(this, Theme.GapCard);
            int w = Math.Max(0, ClientSize.Width - padX * 2);
            int y = top;

            if (head != null)
            {
                int hh = head.PreferredHeight;
                head.SetBounds(padX, y, w, hh);
                y += hh + Gfx.S(this, 4);
            }

            for (int i = 0; i < cards.Count; i++)
            {
                int h = Gfx.S(this, heights[i]);
                // Size at the design height first: that resize is what applies
                // the card's placements, and the wrapped height of anything
                // inside is only known once they have run.
                cards[i].SetBounds(padX, y, w, h);
                h = Math.Max(h, cards[i].ContentHeight);
                cards[i].SetBounds(padX, y, w, h);
                y += h + gap;
            }

            AutoScrollMinSize = new Size(0, y + Gfx.S(this, Theme.PadBottom));
        }
    }

    // Page title + one-line description, per the design's "pane-head".
    internal class PageHead : Control, IThemed
    {
        private string title = "";
        private string note = "";
        private Control action;
        private int actionW;

        public PageHead()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FormBack;
        }

        public string Title
        {
            get { return title; }
            set { title = value ?? ""; Invalidate(); }
        }

        public string Note
        {
            get { return note; }
            set { note = value ?? ""; Invalidate(); }
        }

        public int PreferredHeight
        {
            get
            {
                int h = Gfx.S(this, 30);
                if (note.Length > 0) h += Gfx.S(this, 22);
                return h + Gfx.S(this, 16);
            }
        }

        // A right-aligned control on the header line (e.g. "add folder").
        public T AddAction<T>(T c, int w) where T : Control
        {
            action = c;
            actionW = w;
            Controls.Add(c);
            return c;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            ForeColor = Theme.Fore;
            Invalidate(true);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (action != null)
            {
                int w = Gfx.S(this, actionW);
                int h = Gfx.S(this, Theme.BtnH);
                action.SetBounds(Math.Max(0, Width - w), Gfx.S(this, 2), w, h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.FormBack);

            int y = Gfx.S(this, 2);
            Font ft = Theme.UiFont(Theme.FsPageTitle, FontStyle.Bold, DeviceDpi);
            Size ts = TextRenderer.MeasureText(title, ft);
            Gfx.Text(g, title, ft, Theme.Fore,
                new Rectangle(0, y, Math.Max(0, Width), ts.Height + Gfx.S(this, 6)), Gfx.LeftTop);
            y += ts.Height + Gfx.S(this, 6);

            if (note.Length > 0)
            {
                Gfx.Text(g, note, Theme.UiFont(this, Theme.FsSub), Theme.ForeMuted,
                    new Rectangle(0, y, Math.Max(0, Width), Gfx.S(this, 20)), Gfx.LeftTop);
            }
        }
    }

    // ---- style chooser (3 x 2 grid) -------------------------------------

    // The six WallpaperStyle values, drawn as miniature layout diagrams.
    // Replaces the old combo box: the point of the grid is that the option
    // shows what it does instead of naming it.
    internal class StyleOptionGrid : Control, IThemed
    {
        private string[] items = new string[0];
        private int selected;
        private int hotIndex = -1;
        private int columns = 3;
        private int cellH = 96;
        private readonly List<Rectangle> cells = new List<Rectangle>();

        public event EventHandler SelectedIndexChanged;

        public StyleOptionGrid()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; LayoutCells(); Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = value < 0 ? 0 : value;
                if (v == selected) return;
                selected = v;
                Invalidate();
            }
        }

        public int Columns
        {
            get { return columns; }
            set { columns = Math.Max(1, value); LayoutCells(); Invalidate(); }
        }

        public int CellH
        {
            get { return cellH; }
            set { cellH = Math.Max(1, value); LayoutCells(); Invalidate(); }
        }

        // Height the caller should reserve for the current width.
        public static int HeightFor(int columns, int rows, int cellH, Control c)
        {
            int gap = Gfx.S(c, 10);
            return rows * Gfx.S(c, cellH) + (rows - 1) * gap;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Surface;
            ForeColor = Theme.Fore;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutCells();
        }

        private void LayoutCells()
        {
            cells.Clear();
            if (items.Length == 0) return;
            int gap = Gfx.S(this, 10);
            int cw = (Width - gap * (columns - 1)) / columns;
            int ch = Gfx.S(this, cellH);
            for (int i = 0; i < items.Length; i++)
            {
                int r = i / columns;
                int col = i % columns;
                cells.Add(new Rectangle(col * (cw + gap), r * (ch + gap), cw, ch));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i == hotIndex) return;
            hotIndex = i;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hotIndex < 0) return;
            hotIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (i < 0 || i == selected) return;
            selected = i;
            Invalidate();
            EventHandler h = SelectedIndexChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            if (cells.Count == 0) return;

            Font fl = Theme.UiFont(this, Theme.FsSub);
            float rad = Gfx.S(this, 9);
            for (int i = 0; i < cells.Count && i < items.Length; i++)
            {
                Rectangle rc = cells[i];
                bool on = i == selected;
                bool hot = i == hotIndex;
                RectangleF rr = new RectangleF(rc.X + 0.5f, rc.Y + 0.5f, rc.Width - 1f, rc.Height - 1f);

                Gfx.FillRound(g, rr, rad, on ? Theme.AccentSoft : (hot ? Theme.Hover : Theme.Surface));
                Gfx.StrokeRound(g, rr, rad, on ? Theme.AccentText : Theme.Border, 1f);

                // Preview: a screen rectangle with the image laid out inside.
                int pad = Gfx.S(this, 11);
                Rectangle pv = new Rectangle(rc.X + pad, rc.Y + pad,
                    Math.Max(1, rc.Width - pad * 2), Math.Max(1, rc.Height - pad * 2 - Gfx.S(this, 26)));
                DrawPreview(g, (WallpaperStyle)i, pv);

                // Label on the left, the tick appearing only when chosen.
                Rectangle lab = new Rectangle(rc.X + pad, pv.Bottom + Gfx.S(this, 6),
                    Math.Max(1, rc.Width - pad * 2), Gfx.S(this, 18));
                Gfx.Text(g, items[i], fl, Theme.Fore, lab, Gfx.LeftMid);
                if (on)
                {
                    IconPainter.Draw(g, IconKind.Check,
                        new RectangleF(lab.Right - Gfx.S(this, 15), lab.Y + (lab.Height - Gfx.S(this, 15)) / 2f,
                            Gfx.S(this, 15), Gfx.S(this, 15)),
                        Theme.AccentText, Math.Max(1f, Gfx.S(this, 2)));
                }
            }
        }

        // A 16:9-ish box standing in for the monitor, with the image book
        // drawn where the style would put it.
        private void DrawPreview(Graphics g, WallpaperStyle style, Rectangle box)
        {
            if (box.Width < 8 || box.Height < 8) return;
            float rad = Gfx.S(this, 5);
            RectangleF outer = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1f, box.Height - 1f);
            Gfx.StrokeRound(g, outer, rad, Theme.Border, 1f);

            float inner = Gfx.S(this, 3);
            float iw = box.Width - inner * 2, ih = box.Height - inner * 2;
            float ax = box.X + inner, ay = box.Y + inner;
            Color ink = Theme.AccentText;
            float w = iw, h = ih, x = ax, y = ay;

            switch (style)
            {
                case WallpaperStyle.Fill:
                    // overflows the frame on one axis
                    w = iw * 1.34f; h = ih; x = ax - (w - iw) / 2f;
                    break;
                case WallpaperStyle.Fit:
                    w = ih * 1.78f; if (w > iw) { w = iw; h = iw / 1.78f; }
                    h = Math.Min(ih, w / 1.78f); w = h * 1.78f;
                    x = ax + (iw - w) / 2f; y = ay + (ih - h) / 2f;
                    break;
                case WallpaperStyle.Stretch:
                    break;
                case WallpaperStyle.Tile:
                    w = iw / 3f; h = ih / 3f;
                    break;
                case WallpaperStyle.Center:
                    w = iw * 0.5f; h = ih * 0.5f;
                    x = ax + (iw - w) / 2f; y = ay + (ih - h) / 2f;
                    break;
                case WallpaperStyle.Span:
                    w = iw * 2f; h = ih;
                    break;
            }

            using (SolidBrush b = new SolidBrush(ink))
            {
                if (style == WallpaperStyle.Tile)
                {
                    for (int ty = 0; ty < 3; ty++)
                    {
                        for (int tx = 0; tx < 3; tx++)
                        {
                            g.FillRectangle(b, ax + tx * w, ay + ty * h, Math.Max(1f, w - 1f), Math.Max(1f, h - 1f));
                        }
                    }
                }
                else
                {
                    GraphicsState st = g.Save();
                    RectangleF clip = new RectangleF(ax, ay, iw, ih);
                    g.SetClip(clip);
                    g.FillRectangle(b, x, y, Math.Max(1f, w), Math.Max(1f, h));
                    g.Restore(st);
                }
            }
        }
    }

    // ---- switch history timeline ---------------------------------------

    // One row per visited wallpaper, newest first. The model behind it is
    // history + forward, which is why the rows are not a simple list: the
    // current entry sits in the middle, entries above it are redo targets
    // ("restorable") and entries below are what has already been seen.
    internal class HistoryList : Control, IThemed
    {
        private string[] names = new string[0];
        private string[] metas = new string[0];
        private int currentIndex = -1;
        private string currentTag = "";
        private string undoableTag = "";
        private string backTag = "";
        private string emptyText = "";
        private Image[] thumbs = new Image[0];
        private int hotIndex = -1;
        private int rowH = 47;
        private readonly List<Rectangle> rows = new List<Rectangle>();

        public event EventHandler ItemClicked;

        public HistoryList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        public int RowH
        {
            get { return rowH; }
            set { rowH = Math.Max(1, value); Relayout(); Invalidate(); }
        }

        public string CurrentTag
        {
            get { return currentTag; }
            set { currentTag = value ?? ""; Invalidate(); }
        }

        public string UndoableTag
        {
            get { return undoableTag; }
            set { undoableTag = value ?? ""; Invalidate(); }
        }

        public string BackTag
        {
            get { return backTag; }
            set { backTag = value ?? ""; Invalidate(); }
        }

        public string EmptyText
        {
            get { return emptyText; }
            set { emptyText = value ?? ""; Invalidate(); }
        }

        // Entries in display order (newest first). currentIndex is a position
        // in this same array; rows after it are redo targets.
        public void SetRows(string[] displayNames, string[] displayMetas, Image[] displayThumbs, int current)
        {
            names = displayNames ?? new string[0];
            metas = displayMetas ?? new string[0];
            thumbs = displayThumbs ?? new Image[0];
            currentIndex = current;
            hotIndex = -1;
            Relayout();
            Invalidate();
        }

        // Height needed to show every row, capped by the caller's card.
        public int PreferredHeight
        {
            get { return names.Length * Gfx.S(this, rowH); }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Surface;
            ForeColor = Theme.Fore;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Relayout();
        }

        private void Relayout()
        {
            rows.Clear();
            int h = Gfx.S(this, rowH);
            for (int i = 0; i < names.Length; i++)
            {
                rows.Add(new Rectangle(0, i * h, Width, h));
            }
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i == hotIndex) return;
            hotIndex = i;
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
            if (hotIndex < 0) return;
            hotIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (i < 0) return;
            pressedIndex = i;
        }

        private int pressedIndex = -1;

        // Row index passed with the last ItemClicked (a display index, not a
        // timeline index - the host owns that mapping).
        public int ClickedIndex { get; private set; }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = HitTest(e.Location);
            if (i < 0 || i != pressedIndex) { pressedIndex = -1; return; }
            pressedIndex = -1;
            ClickedIndex = i;
            EventHandler h = ItemClicked;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            if (names.Length == 0)
            {
                if (emptyText.Length > 0)
                {
                    Gfx.Text(g, emptyText, Theme.UiFont(this, Theme.FsSub), Theme.ForeMuted,
                        new Rectangle(Gfx.S(this, 10), 0, Math.Max(0, Width - Gfx.S(this, 20)),
                            Gfx.S(this, rowH)), Gfx.LeftMid);
                }
                return;
            }

            Font fn = Theme.UiFont(this, Theme.FsBodySm);
            Font fnBold = Theme.UiFont(Theme.FsBodySm, FontStyle.Bold, DeviceDpi);
            Font fm = Theme.MonoFont(this, Theme.FsMono);
            Font ff = Theme.UiFont(this, Theme.FsCap);
            float rad = Gfx.S(this, 8);

            for (int i = 0; i < rows.Count && i < names.Length; i++)
            {
                Rectangle rc = rows[i];
                bool cur = i == currentIndex;
                bool future = currentIndex >= 0 && i < currentIndex;
                bool hot = i == hotIndex;

                if (cur)
                {
                    Gfx.FillRound(g, rc, rad, Theme.Selected);
                }
                else if (hot)
                {
                    Gfx.FillRound(g, rc, rad, Theme.Hover);
                }

                int padX = Gfx.S(this, 10);
                int tw = Gfx.S(this, 52), th = Gfx.S(this, 29);
                Rectangle tb = new Rectangle(rc.X + padX, rc.Y + (rc.Height - th) / 2, tw, th);
                if (i < thumbs.Length && thumbs[i] != null)
                {
                    GraphicsState st = g.Save();
                    g.SetClip(tb);
                    if (future) g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(thumbs[i], tb);
                    g.Restore(st);
                    if (future)
                    {
                        // .hist-row.future img{opacity:.45} - dim towards the card.
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(140, Theme.Surface)))
                        {
                            g.FillRectangle(b, tb);
                        }
                    }
                    Gfx.StrokeRound(g, new RectangleF(tb.X + 0.5f, tb.Y + 0.5f, tb.Width - 1f, tb.Height - 1f),
                        Gfx.S(this, 5), Theme.Border, 1f);
                }

                int textX = tb.Right + Gfx.S(this, 12);
                int flagW = 0;
                string flag = cur ? "" : (future ? backTag : "");
                if (flag.Length > 0)
                {
                    flagW = TextRenderer.MeasureText(flag, ff).Width + Gfx.S(this, 10);
                }
                int textW = Math.Max(0, rc.Right - padX - textX - flagW);

                Font useName = cur ? fnBold : fn;
                int lineH = Gfx.S(this, 18);
                int blockTop = rc.Y + (rc.Height - lineH * 2) / 2;

                // Pending rows are "restorable": their name drops to the
                // secondary colour, which is exactly the case MutedStrong
                // exists for. Seen/current rows keep full-strength fg.
                Color nameInk = future ? Theme.MutedStrong : Theme.Fore;
                Gfx.Text(g, names[i], useName, nameInk,
                    new Rectangle(textX, blockTop, textW, lineH), Gfx.Ellipsis(Gfx.LeftMid));

                string meta = i < metas.Length ? metas[i] : "";
                if (meta.Length > 0)
                {
                    // .h-meta sits on --hover / --selected, where ForeMuted
                    // measures 4.05:1 / 2.98:1 (light). MutedStrong holds
                    // 7.05:1 / 5.18:1 across every state.
                    Gfx.Text(g, meta, fm, Theme.MutedStrong,
                        new Rectangle(textX, blockTop + lineH, textW, lineH), Gfx.Ellipsis(Gfx.LeftMid));
                }

                if (flag.Length > 0)
                {
                    Gfx.Text(g, flag, ff, Theme.MutedStrong,
                        new Rectangle(rc.Right - padX - flagW, rc.Y, flagW, rc.Height), Gfx.Ellipsis(Gfx.RightMid));
                }
            }
        }
    }

    // ---- preview thumbnail ----------------------------------------------

    // The design's ".shot": a rounded 16:9 thumbnail of the current
    // wallpaper, cropped to fill (object-fit:cover), with an optional pill
    // floating in the top-left corner.
    internal class PreviewBox : Control, IThemed
    {
        private Image image;
        private string pill = "";
        private string emptyText = "";

        public PreviewBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        public Image Image
        {
            get { return image; }
            set { image = value; Invalidate(); }
        }

        public string Pill
        {
            get { return pill; }
            set { pill = value ?? ""; Invalidate(); }
        }

        public string EmptyText
        {
            get { return emptyText; }
            set { emptyText = value ?? ""; Invalidate(); }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Surface;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            float rad = Gfx.S(this, 9);
            RectangleF rr = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Gfx.FillRound(g, rr, rad, Theme.Subtle);

            if (image != null && Width > 2 && Height > 2)
            {
                // Cover, not fit: the frame keeps its 16:9 shape and the
                // image is trimmed on the loose axis.
                float boxAspect = Width / (float)Height;
                float imgAspect = image.Width / (float)image.Height;
                int srcW, srcH;
                if (imgAspect > boxAspect)
                {
                    srcH = image.Height;
                    srcW = (int)Math.Round(image.Height * boxAspect);
                }
                else
                {
                    srcW = image.Width;
                    srcH = (int)Math.Round(image.Width / boxAspect);
                }
                srcW = Math.Max(1, Math.Min(srcW, image.Width));
                srcH = Math.Max(1, Math.Min(srcH, image.Height));
                Rectangle src = new Rectangle(
                    (image.Width - srcW) / 2, (image.Height - srcH) / 2, srcW, srcH);

                GraphicsState st = g.Save();
                using (GraphicsPath clip = Gfx.Round(rr, rad))
                {
                    g.SetClip(clip);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(image, new Rectangle(0, 0, Width, Height), src, GraphicsUnit.Pixel);
                }
                g.Restore(st);
            }
            else if (emptyText.Length > 0)
            {
                Gfx.Text(g, emptyText, Theme.UiFont(this, Theme.FsSub), Theme.ForeMuted,
                    new Rectangle(0, 0, Width, Height), Gfx.CenterMid);
            }

            Gfx.StrokeRound(g, rr, rad, Theme.Border, 1f);

            if (pill.Length > 0 && Width > Gfx.S(this, 40))
            {
                Font fp = Theme.UiFont(Theme.FsCap, FontStyle.Bold, DeviceDpi);
                int dot = Gfx.S(this, 6);
                int padX = Gfx.S(this, 9);
                int w = TextRenderer.MeasureText(pill, fp).Width + dot + padX * 2 + Gfx.S(this, 6);
                int h = Gfx.S(this, 21);
                RectangleF pr = new RectangleF(Gfx.S(this, 10), Gfx.S(this, 10), w, h);
                Gfx.FillRound(g, pr, h / 2f, Theme.NowPill);
                using (SolidBrush b = new SolidBrush(Color.White))
                {
                    g.FillEllipse(b, pr.X + padX, pr.Y + (h - dot) / 2f, dot, dot);
                }
                Gfx.Text(g, pill, fp, Color.White,
                    new Rectangle((int)pr.X + padX + dot + Gfx.S(this, 6), (int)pr.Y,
                        w, (int)h), Gfx.LeftMid);
            }
        }
    }

    // ---- key cap (hotkey capture) ---------------------------------------

    // The design's ".keycap": a mono key box that takes a click and then
    // waits for a key combination. Only Ctrl+digit is meaningful to this
    // program (that is what HotkeyManager can register), so capture accepts
    // exactly that, plus Delete for "none" and Escape to back out.
    internal sealed class KeyCapButton : Control, IThemed
    {
        private bool hover;
        private bool capturing;
        private int value = -1;          // -1 none, 0-9 = Ctrl+digit
        private string noneText = "";
        private string captureText = "";

        // Raised when the user commits (including "none"). The host reads
        // Value and persists it.
        public event EventHandler Captured;

        public KeyCapButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = true;
        }

        public int Value
        {
            get { return value; }
            set { this.value = value; Invalidate(); }
        }

        public string NoneText
        {
            get { return noneText; }
            set { noneText = value ?? ""; Invalidate(); }
        }

        public string CaptureText
        {
            get { return captureText; }
            set { captureText = value ?? ""; Invalidate(); }
        }

        public bool Capturing
        {
            get { return capturing; }
        }

        // The host gates its global-hotkey handling on this while a capture
        // is in flight: otherwise pressing Ctrl+9 to bind it would also
        // switch the wallpaper.
        public static bool AnyCapturing;

        public void BeginCapture()
        {
            if (capturing) return;
            capturing = true;
            AnyCapturing = true;
            Focus();
            Invalidate();
        }

        public void CancelCapture()
        {
            if (!capturing) return;
            capturing = false;
            AnyCapturing = false;
            Invalidate();
        }

        public void ApplyTheme()
        {
            ForeColor = Theme.Fore;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            BeginCapture();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            CancelCapture();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (capturing) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!capturing) return;

            if (e.KeyCode == Keys.Escape)
            {
                CancelCapture();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                value = -1;
                CancelCapture();
                EventHandler h0 = Captured;
                if (h0 != null) h0(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            // Ctrl+digit only - that is the whole space HotkeyManager can
            // register, so anything else is rejected rather than stored and
            // silently failing at RegisterHotKey time.
            if (e.Control && e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
            {
                value = e.KeyCode - Keys.D0;
                CancelCapture();
                EventHandler h = Captured;
                if (h != null) h(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
            {
                value = e.KeyCode - Keys.NumPad0;
                CancelCapture();
                EventHandler h = Captured;
                if (h != null) h(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            float rad = Gfx.S(this, Theme.RadBtn);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

            Color back = capturing ? Theme.AccentSoft : (hover ? Theme.Hover : Theme.Surface);
            Color border = capturing ? Theme.AccentText : Theme.Border;
            Color fore = capturing ? Theme.AccentText : Theme.Fore;
            Gfx.FillRound(g, r, rad, back);
            Gfx.StrokeRound(g, r, rad, border, 1f);

            string label = capturing
                ? (captureText.Length > 0 ? captureText : "...")
                : (value >= 0 ? "Ctrl+" + value.ToString() : noneText);

            Font f = Theme.MonoFont(this, Theme.FsBodySm);
            Gfx.Text(g, label, f, fore,
                new Rectangle(0, 0, Width, Height), Gfx.Ellipsis(Gfx.CenterMid));

            if (Focused && !capturing) Gfx.FocusRing(g, r, rad, this);
        }
    }

    // ---- labels ---------------------------------------------------------

    // A Label that paints itself, so its font is resolved from the control's
    // *current* DPI every frame. A plain Label takes a Font instance once, at
    // construction, when the control is still at the 96 DPI default - on a
    // 150% monitor it then renders at the wrong size until something rescales
    // it. Everything else in this kit draws text through Theme.UiFont(control).
    internal enum LabelStyle
    {
        PageTitle, PreviewName, CardTitle, CardNote, Body, BodyBold, Sub, Cap,
        Mono, MonoSm, StatNum
    }

    internal class KitLabel : Control, IThemed
    {
        private LabelStyle style = LabelStyle.Body;
        private bool muted;
        private bool wrap;
        private bool ellipsis = true;
        private Color? ink;
        private ContentAlignment align = ContentAlignment.MiddleLeft;

        public KitLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public LabelStyle Style
        {
            get { return style; }
            set { style = value; Invalidate(); }
        }

        public bool Muted
        {
            get { return muted; }
            set { muted = value; Invalidate(); }
        }

        // Wrap instead of clipping, for multi-line explanations.
        public bool Wrap
        {
            get { return wrap; }
            set { wrap = value; Invalidate(); }
        }

        public bool Ellipsis
        {
            get { return ellipsis; }
            set { ellipsis = value; Invalidate(); }
        }

        public ContentAlignment Align
        {
            get { return align; }
            set { align = value; Invalidate(); }
        }

        // Explicit ink, for the cases where the caller knows better than the
        // style (accent, warn, ...).
        public Color? Ink
        {
            get { return ink; }
            set { ink = value; Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        public void ApplyTheme()
        {
            ForeColor = muted ? Theme.MutedStrong : Theme.Fore;
            Invalidate();
        }

        private Font ResolveFont()
        {
            switch (style)
            {
                case LabelStyle.PageTitle:
                    return Theme.UiFont(Theme.FsPageTitle, FontStyle.Bold, DeviceDpi);
                case LabelStyle.PreviewName:
                    return Theme.UiFont(Theme.FsPreviewName, FontStyle.Bold, DeviceDpi);
                case LabelStyle.CardTitle:
                    return Theme.UiFont(Theme.FsCardTitle, FontStyle.Bold, DeviceDpi);
                case LabelStyle.CardNote:
                    return Theme.UiFont(this, Theme.FsCardNote);
                case LabelStyle.BodyBold:
                    return Theme.UiFont(Theme.FsBodySm, FontStyle.Bold, DeviceDpi);
                case LabelStyle.Sub:
                    return Theme.UiFont(this, Theme.FsSub);
                case LabelStyle.Cap:
                    return Theme.UiFont(this, Theme.FsCap);
                case LabelStyle.Mono:
                    return Theme.MonoFont(this, Theme.FsMono);
                case LabelStyle.MonoSm:
                    return Theme.MonoFont(this, Theme.FsMonoSm);
                case LabelStyle.StatNum:
                    return Theme.MonoFont(this, Theme.FsStatNum);
                default:
                    return Theme.UiFont(this, Theme.FsBodySm);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Transparent: let the card behind show through.
            if (Parent != null) { g.Clear(Parent.BackColor); }

            Color c = ink ?? (muted ? Theme.MutedStrong : Theme.Fore);
            Font f = ResolveFont();
            Size need = wrap
                ? TextRenderer.MeasureText(Text, f, new Size(Math.Max(1, Width), 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix)
                : TextRenderer.MeasureText(Text, f);

            Rectangle box = new Rectangle(0, 0, Width, Height);
            if (!wrap)
            {
                box.Height = Math.Min(Height, need.Height);
                if (align == ContentAlignment.MiddleLeft || align == ContentAlignment.MiddleCenter ||
                    align == ContentAlignment.MiddleRight)
                {
                    box.Y = Math.Max(0, (Height - box.Height) / 2);
                }
            }
            if (wrap && need.Height > Height)
            {
                box.Height = Height;
            }

            TextFormatFlags flags;
            switch (align)
            {
                case ContentAlignment.MiddleCenter:
                    flags = Gfx.CenterMid; break;
                case ContentAlignment.MiddleRight:
                case ContentAlignment.TopRight:
                    flags = Gfx.RightMid; break;
                default:
                    flags = wrap ? Gfx.LeftTop : Gfx.LeftMid; break;
            }
            if (!wrap && ellipsis) flags = Gfx.Ellipsis(flags);
            Gfx.Text(g, Text, f, c, box, flags);
        }
    }

    // ---- hairline separator ---------------------------------------------

    // The design's ".sep": a full-width hairline in the border colour.
    internal sealed class Rule : Control, IThemed
    {
        public Rule()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 1;
        }

        public void ApplyTheme()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            Gfx.Line(e.Graphics, 0, 0.5f, Width, 0.5f, Theme.Border, 1f);
        }
    }

    // ---- wallpaper source list ------------------------------------------

    // One row per source folder: a switch, the folder name, its path, how many
    // wallpapers the last scan found, and a remove button. Owner-drawn for the
    // same reason as the other lists here - a ListView cannot follow the
    // palette or the DPI.
    //
    // The control holds no policy. The caller hands it a folder list and a set
    // of switched-off folders and gets Changed back when a row is clicked;
    // whether that means "write the config now" or "mark the window dirty" is
    // the window's decision. Counts arrive later, one source at a time, from
    // whatever background scan the caller runs.
    internal class SourceList : Control, IThemed
    {
        private readonly List<string> folders = new List<string>();
        private readonly HashSet<string> off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> counts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int hotRow = -1;
        private int hotKill = -1;

        public event EventHandler Changed;

        public int RowH = 48;

        public string EmptyText = "";
        public string PendingText = "";
        public string UnavailableText = "";

        public SourceList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        public void ApplyTheme()
        {
            Invalidate();
        }

        public int SourceCount { get { return folders.Count; } }

        public int PreferredHeight
        {
            get { return folders.Count == 0 ? 56 : folders.Count * RowH; }
        }

        public void SetSources(List<string> list, HashSet<string> disabled)
        {
            folders.Clear();
            foreach (string f in list)
            {
                if (f != null && f.Trim().Length > 0) folders.Add(f.Trim());
            }
            off.Clear();
            foreach (string d in disabled)
            {
                if (d != null) off.Add(d.Trim());
            }
            PruneCounts();
            hotRow = -1;
            hotKill = -1;
            SyncHeight();
        }

        public bool IsOn(string folder)
        {
            return !off.Contains(folder);
        }

        public IList<string> Sources { get { return folders; } }

        // Off folders in list order, which is also the order they are written
        // to the config in.
        public List<string> DisabledList()
        {
            List<string> list = new List<string>();
            foreach (string f in folders)
            {
                if (off.Contains(f)) list.Add(f);
            }
            return list;
        }

        // False when the folder is already in the list - the caller uses that
        // to say "already a source" instead of adding a duplicate.
        public bool AddSource(string folder)
        {
            if (folder == null || folder.Trim().Length == 0) return false;
            string f = folder.Trim();
            foreach (string existing in folders)
            {
                if (string.Equals(existing, f, StringComparison.OrdinalIgnoreCase)) return false;
            }
            folders.Add(f);
            off.Remove(f);
            SyncHeight();
            return true;
        }

        public void SetAll(bool on)
        {
            off.Clear();
            if (!on)
            {
                foreach (string f in folders) off.Add(f);
            }
            Invalidate();
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= folders.Count) return;
            string f = folders[index];
            folders.RemoveAt(index);
            off.Remove(f);
            counts.Remove(f);
            hotRow = -1;
            hotKill = -1;
            SyncHeight();
        }

        // ---- counts ---------------------------------------------------------

        public void ClearCounts()
        {
            counts.Clear();
            Invalidate();
        }

        public bool IsPending(string folder)
        {
            return !counts.ContainsKey(folder);
        }

        public int CountOf(string folder)
        {
            int n;
            return counts.TryGetValue(folder, out n) ? n : 0;
        }

        // n < 0 means the folder is gone or unreadable, which is worth saying
        // out loud in the row rather than showing a misleading 0.
        public void SetCount(string folder, int n)
        {
            counts[folder] = n;
            Invalidate();
        }

        private void PruneCounts()
        {
            if (counts.Count == 0) return;
            List<string> kill = new List<string>();
            foreach (string k in counts.Keys)
            {
                bool live = false;
                foreach (string f in folders)
                {
                    if (string.Equals(f, k, StringComparison.OrdinalIgnoreCase)) { live = true; break; }
                }
                if (!live) kill.Add(k);
            }
            foreach (string k in kill) counts.Remove(k);
        }

        private void SyncHeight()
        {
            int want = Gfx.S(this, PreferredHeight);
            if (Height != want) Height = want;
            Invalidate();
            Control p = Parent;
            while (p != null)
            {
                CardPanel card = p as CardPanel;
                if (card != null) { card.Relayout(); return; }
                PageStack ps = p as PageStack;
                if (ps != null) { ps.RelayoutNow(); return; }
                p = p.Parent;
            }
        }

        // ---- hit testing ----------------------------------------------------

        private int Rows
        {
            get { return folders.Count; }
        }

        private int RowAt(int y)
        {
            int rowH = Gfx.S(this, RowH);
            if (rowH <= 0) return -1;
            int i = y / rowH;
            return (i >= 0 && i < Rows) ? i : -1;
        }

        private int KillW { get { return Gfx.S(this, 28); } }

        private bool KillHit(Rectangle row, int x)
        {
            return x >= row.Right - KillW - Gfx.S(this, 2) && x <= row.Right;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int r = RowAt(e.Y);
            int k = -1;
            if (r >= 0)
            {
                int top = r * Gfx.S(this, RowH);
                Rectangle row = new Rectangle(0, top, Width, Gfx.S(this, RowH));
                if (KillHit(row, e.X)) k = r;
            }
            Cursor = k >= 0 ? Cursors.Hand : Cursors.Default;
            if (r != hotRow || k != hotKill)
            {
                hotRow = r;
                hotKill = k;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hotRow < 0 && hotKill < 0) return;
            hotRow = -1;
            hotKill = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            int r = RowAt(e.Y);
            if (r < 0) return;
            int top = r * Gfx.S(this, RowH);
            Rectangle row = new Rectangle(0, top, Width, Gfx.S(this, RowH));

            if (KillHit(row, e.X))
            {
                RemoveAt(r);
            }
            else
            {
                string f = folders[r];
                if (off.Contains(f)) off.Remove(f);
                else off.Add(f);
                Invalidate();
            }
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---- painting -------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);

            Font fName = Theme.UiFont(Theme.FsBodySm, FontStyle.Bold, DeviceDpi);
            Font fPath = Theme.MonoFont(this, Theme.FsMonoSm);
            Font fCap = Theme.UiFont(this, Theme.FsCap);

            if (folders.Count == 0)
            {
                Gfx.Text(g, EmptyText, fCap, Theme.MutedStrong,
                    new Rectangle(0, 0, Width, Height), Gfx.LeftMid);
                return;
            }

            int rowH = Gfx.S(this, RowH);
            int swW = Gfx.S(this, Theme.SwitchW);
            int swH = Gfx.S(this, Theme.SwitchH);
            int killW = KillW;
            int textX = swW + Gfx.S(this, 14);
            int countW = Gfx.S(this, 92);
            int countX = Width - killW - Gfx.S(this, 4) - countW;

            for (int i = 0; i < folders.Count; i++)
            {
                int top = i * rowH;
                Rectangle row = new Rectangle(0, top, Width, rowH);
                bool on = IsOn(folders[i]);

                if (i == hotRow)
                {
                    Gfx.FillRound(g, new RectangleF(row.X, top + Gfx.S(this, 2),
                            row.Width, rowH - Gfx.S(this, 4)),
                        Gfx.S(this, Theme.RadBtnSm), Theme.Hover);
                }

                SwitchDraw.Draw(g, this,
                    new RectangleF(row.X + 0.5f, top + (rowH - swH) / 2f + 0.5f,
                        swW - 1f, swH - 1f),
                    on, i == hotRow, true, false);

                int nameW = Math.Max(0, countX - Gfx.S(this, 10) - textX);
                Gfx.Text(g, SourceNames.Display(folders[i]), fName,
                    on ? Theme.Fore : Theme.MutedStrong,
                    new Rectangle(textX, top + Gfx.S(this, 6), nameW, Gfx.S(this, 19)),
                    Gfx.Ellipsis(Gfx.LeftMid));
                Gfx.Text(g, folders[i], fPath, Theme.MutedStrong,
                    new Rectangle(textX, top + Gfx.S(this, 26), nameW, Gfx.S(this, 16)),
                    Gfx.Ellipsis(Gfx.LeftMid));

                int n = CountOf(folders[i]);
                string ct = IsPending(folders[i])
                    ? PendingText
                    : (n < 0 ? UnavailableText : n.ToString());
                Color cc = on ? (n < 0 && !IsPending(folders[i]) ? Theme.Warn : Theme.MutedStrong)
                              : Theme.MutedStrong;
                Gfx.Text(g, ct, fCap, cc,
                    new Rectangle(countX, top, countW, rowH), Gfx.RightMid);

                Color kc = i == hotKill ? Theme.Warn : Theme.MutedStrong;
                Rectangle kbox = new Rectangle(row.Right - killW - Gfx.S(this, 2),
                    top + (rowH - killW) / 2, killW, killW);
                if (i == hotKill)
                {
                    Gfx.FillRound(g, kbox, Gfx.S(this, Theme.RadBtnSm), Theme.Subtle);
                }
                IconPainter.Draw(g, IconKind.Trash,
                    new RectangleF(kbox.X + Gfx.S(this, 7), kbox.Y + Gfx.S(this, 7),
                        kbox.Width - Gfx.S(this, 14), kbox.Height - Gfx.S(this, 14)),
                    kc, 1.6f);

                if (i < folders.Count - 1)
                {
                    Gfx.Line(g, textX, top + rowH - 0.5f, Width, top + rowH - 0.5f,
                        Theme.Border, 1f);
                }
            }
        }
    }
}
