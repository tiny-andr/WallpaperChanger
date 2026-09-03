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
            Controls.Add(rtb);

            Button btnClose = new Button();
            btnClose.Text = "知道了";
            btnClose.SetBounds(382, 422, 76, 28);
            btnClose.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btnClose);
            AcceptButton = btnClose;

            BuildContent();
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
            Emit("  • 主键盘与小键盘数字键都支持，在全屏游戏中也生效。", bodyFont, bodyColor, 0);
            Emit("  • \"上一张\"可连续回退，最远回到本次启动时显示的那张壁纸。", bodyFont, bodyColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 支持的图片格式", headFont, headColor, 0);
            Emit("  • jpg / png / jfif / bmp / webp / gif / tiff。", bodyFont, bodyColor, 0);
            Emit("  • 自动跳过系统隐藏文件（如 Thumbs.db）与损坏的图片。", bodyFont, bodyColor, 0);
            Emit("", bodyFont, bodyColor, 0);

            Emit("■ 新增壁纸何时生效", headFont, headColor, 0);
            Emit("  • 程序不实时监控文件夹；每次切换（定时到点、Ctrl+9 或点击\"下一张壁纸\"）都会重新扫描目录。", bodyFont, bodyColor, 0);
            Emit("  • 新增图片会自动纳入轮换；随机模式下会重新洗牌，之后很快就能轮到新图。", bodyFont, bodyColor, 0);
            Emit("  • \"上一张\"属于历史回退、不触发扫描，只会回到本次启动后已经显示过的壁纸。", bodyFont, bodyColor, 0);

            rtb.SelectionStart = 0;
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
