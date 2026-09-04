using System;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Modal help dialog. Keeps usage notes, supported formats and the
    // "when are new wallpapers picked up" rules out of the main window,
    // shown on demand from the 帮助 button. Version is read from the
    // assembly so it always matches the built exe.
    public class HelpForm : Form
    {
        private readonly RichTextBox rtb;

        public HelpForm()
        {
            Text = Loc.T("help.title") + " - WallpaperChanger v" + Application.ProductVersion;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(470, 460);
            Font = new Font("Microsoft YaHei UI", 9F);

            rtb = new RichTextBox();
            rtb.SetBounds(12, 12, 446, 402);
            rtb.ReadOnly = true;
            rtb.BorderStyle = BorderStyle.FixedSingle;
            rtb.BackColor = Color.White;
            rtb.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtb.DetectUrls = false;
            // Make selection highlight vanish the moment the user clicks anywhere
            // else (defensive — also useful when the dialog first opens with a
            // lingering selection from the BuildContent pass).
            rtb.HideSelection = true;
            // The AcceptButton below steals focus from rtb on Enter, but the
            // dialog itself still lands focus on rtb on first show — and a
            // focused RichTextBox with a non-empty selection paints a blue
            // band over the start of the text. Drop focus explicitly so it
            // cannot accidentally remain "selected" when the user reads it.
            rtb.TabStop = false;
            Controls.Add(rtb);

            Button btnClose = new Button();
            btnClose.Text = Loc.T("help.gotit");
            btnClose.SetBounds(382, 422, 76, 28);
            btnClose.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btnClose);
            AcceptButton = btnClose;

            BuildContent();

            // After the dialog is fully shown, make sure no text is pre-selected
            // and the caret is on the Close button — not on the RichTextBox,
            // because a focused RichTextBox draws a blue selection band even
            // when SelectionLength is 0 in some DPI/font combinations.
            Shown += delegate
            {
                rtb.SelectionLength = 0;
                rtb.SelectionStart = 0;
                btnClose.Focus();
            };
        }

        private void BuildContent()
        {
            Font titleFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            Font headFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            Font bodyFont = new Font("Microsoft YaHei UI", 9F);
            Color titleColor = Color.FromArgb(0, 90, 158);
            Color headColor = Color.FromArgb(0, 60, 120);
            Color bodyColor = Color.Black;

            Emit("WallpaperChanger " + Application.ProductVersion + "  " + Loc.T("help.title"),
                titleFont, titleColor, 0);
            foreach (Loc.HelpLine line in Loc.HelpContent())
            {
                if (line.Kind == 1) Emit(line.Text, headFont, headColor, 0);
                else Emit(line.Text, bodyFont, bodyColor, 0);
            }

            rtb.SelectionStart = 0;
            rtb.SelectionLength = 0;
        }

        private void Emit(string text, Font font, Color color, int indent)
        {
            int start = rtb.TextLength;
            rtb.AppendText(text + "\r\n");
            rtb.Select(start, text.Length);
            rtb.SelectionFont = font;
            rtb.SelectionColor = color;
            rtb.SelectionIndent = indent;
        }
    }
}
