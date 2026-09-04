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
            Text = "帮助 - WallpaperChanger v" + Application.ProductVersion;
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
            btnClose.Text = "知道了";
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

            Emit("WallpaperChanger " + Application.ProductVersion + "  使用帮助", titleFont, titleColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 壁纸源管理", headFont, headColor, 0);
            Emit("  • 只能点击\"添加...\"按钮选择文件夹加入列表，不允许手动输入路径。", bodyFont, bodyColor, 0);
            Emit("  • 选中列表中的一项后点\"删除选中\"即可移除该壁纸源。", bodyFont, bodyColor, 0);
            Emit("  • \"清空全部\"一键移除所有壁纸源。", bodyFont, bodyColor, 0);
            Emit("  • 可添加多个文件夹，程序会合并扫描所有来源的图片。", bodyFont, bodyColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 保存与后台运行", headFont, headColor, 0);
            Emit("  • 修改任意设置后，请点击\"保存设置\"按钮，才会写入配置文件。", bodyFont, bodyColor, 0);
            Emit("  • 关闭窗口只是最小化到托盘，程序仍在后台轮换；右键托盘图标可暂停 / 退出。", bodyFont, bodyColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 快捷键", headFont, headColor, 0);
            Emit("  • 默认：Ctrl+9 = 下一张，Ctrl+8 = 上一张（可在设置中修改绑定）。", bodyFont, bodyColor, 0);
            Emit("  • 主键盘与小键盘数字键都支持。", bodyFont, bodyColor, 0);
            Emit("  • \"上一张\"可连续回退，最远回到本次启动时显示的那张壁纸。", bodyFont, bodyColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 手动壁纸选择", headFont, headColor, 0);
            Emit("  • 点击\"启用手动壁纸选择\"打开勾选窗口；左上角\"启用手动选择功能\"是总开关，不开启时勾选不生效。", bodyFont, bodyColor, 0);
            Emit("  • 启用后，定时轮换与\"下一张\"只从勾选的壁纸里挑（未勾选的不参与切换）；随机顺序开关不受影响。", bodyFont, bodyColor, 0);
            Emit("  • 顶部输入框可按文件名筛选，\"全选 / 全不选 / 反选\"只作用于当前筛选出的图片。", bodyFont, bodyColor, 0);
            Emit("  • 勾选集合与总开关都保存在配置里，重启后保持；之后新增的图片默认未勾选。", bodyFont, bodyColor, 0);
            Emit("  • \"上一张\"属于历史回退、不受勾选限制；修改后请点窗口右下角\"保存\"才会生效。", bodyFont, bodyColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 支持的图片格式", headFont, headColor, 0);
            Emit("  • jpg / png / jfif / bmp / webp / gif / tiff。", bodyFont, bodyColor, 0);
            Emit("  • 自动跳过系统隐藏文件（如 Thumbs.db）与损坏的图片。", bodyFont, bodyColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 新增壁纸何时生效", headFont, headColor, 0);
            Emit("  • 程序不实时监控文件夹；定时到点会重新扫描并挑一张新壁纸（手动\"下一张\"则先走历史）。", bodyFont, bodyColor, 0);
            Emit("  • 新增图片会自动纳入轮换；随机模式下会重新洗牌，之后很快就能轮到新图。", bodyFont, bodyColor, 0);
            Emit("  • \"上一张\" / \"下一张\"在本次启动的历史里来回走：回退后按\"下一张\"会先恢复刚才回退掉的那张，", bodyFont, bodyColor, 0);
            Emit("    历史走完才会重新扫描挑新图；\"上一张\"最远回到本次启动时显示的那张壁纸。", bodyFont, bodyColor, 0);

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
