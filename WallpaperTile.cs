using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // One 16:9 preview tile in the manual picker grid. Top: cover-cropped
    // thumbnail. Bottom: one-line file name. Click anywhere toggles the
    // selection; a checkbox glyph in the top-left corner and an accent
    // border show the current state. The form owns the Thumbnail image and
    // pushes it in via SetThumbnail once async decoding finishes.
    internal class WallpaperTile : Control
    {
        private static readonly Color Accent = Color.FromArgb(24, 95, 165);
        private static readonly Color BorderIdle = Color.FromArgb(176, 176, 176);
        private static readonly Color PlaceholderBg = Color.FromArgb(240, 240, 240);

        private readonly string filePath;
        private readonly int imageHeight;
        private readonly int labelHeight;
        private Image thumb;      // disposed by this tile (or by the form on close)
        private bool hovered;
        private bool selected;

        public string FilePath { get { return filePath; } }

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected != value)
                {
                    selected = value;
                    Invalidate();
                }
            }
        }

        public WallpaperTile(string path, int width, int imageH, int labelH)
        {
            filePath = path;
            imageHeight = imageH;
            labelHeight = labelH;
            Width = width;
            Height = imageH + labelH;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        // Takes ownership of img; the previously stored thumbnail is disposed.
        public void SetThumbnail(Image img)
        {
            if (img == null) return;
            if (IsDisposed || Disposing)
            {
                img.Dispose();
                return;
            }
            Image old = thumb;
            thumb = img;
            if (old != null) old.Dispose();
            Invalidate();
        }

        // Flip the selection state first, then raise Click so subscribed
        // handlers observe the NEW state. (The picker's data layer mirrors
        // tile.Selected into its picked set on Click; if the flip ran after
        // base.OnClick, handlers would read the stale value and drop picks.)
        protected override void OnClick(EventArgs e)
        {
            Selected = !Selected;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle imgRect = new Rectangle(0, 0, Width, imageHeight);
            if (thumb != null)
            {
                g.DrawImage(thumb, imgRect);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(PlaceholderBg))
                {
                    g.FillRectangle(b, imgRect);
                }
                TextRenderer.DrawText(g, "...", Font, imgRect, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (selected)
            {
                // subtle tint over a picked image
                using (SolidBrush b = new SolidBrush(Color.FromArgb(26, Accent)))
                {
                    g.FillRectangle(b, imgRect);
                }
            }

            DrawCheckBox(g, selected);

            // file name strip
            if (labelHeight > 0)
            {
                Rectangle labelRect = new Rectangle(0, imageHeight, Width, labelHeight);
                using (SolidBrush b = new SolidBrush(BackColor))
                {
                    g.FillRectangle(b, labelRect);
                }
                TextRenderer.DrawText(g, Path.GetFileName(filePath), Font,
                    new Rectangle(3, imageHeight, Width - 6, labelHeight - 2),
                    Color.FromArgb(70, 70, 70),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix);
            }

            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 5))
            using (Pen pen = new Pen(selected || hovered ? Accent : BorderIdle,
                                     (selected || hovered) ? 2f : 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        private void DrawCheckBox(Graphics g, bool on)
        {
            const int box = 18;
            int x = 6;
            int y = 6;
            using (SolidBrush fill = new SolidBrush(on ? Accent : Color.White))
            {
                g.FillRectangle(fill, x, y, box, box);
            }
            using (Pen border = new Pen(on ? Accent : Color.FromArgb(120, 120, 120), 1.5f))
            {
                g.DrawRectangle(border, x + 1, y + 1, box - 3, box - 3);
            }
            if (on)
            {
                using (Pen tick = new Pen(Color.White, 2f))
                {
                    float cx = x;
                    float cy = y;
                    g.DrawLine(tick, cx + 4.5f, cy + 9.5f, cx + 7.5f, cy + 12.5f);
                    g.DrawLine(tick, cx + 7.5f, cy + 12.5f, cx + 13.5f, cy + 5f);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (thumb != null)
                {
                    thumb.Dispose();
                    thumb = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
