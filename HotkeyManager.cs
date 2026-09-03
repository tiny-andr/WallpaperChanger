using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Which action a received hotkey maps to.
    public enum HotkeyAction
    {
        None,
        Next,   // switch to the next wallpaper
        Prev    // go back to the previous wallpaper
    }

    // System-wide hotkeys (RegisterHotKey) - they work regardless of focus,
    // including while running in the tray or inside fullscreen games.
    // Supports TWO independent Ctrl+digit bindings (next and previous),
    // each registered on BOTH the main keyboard and the numpad so either
    // keypad works.
    public sealed class HotkeyManager : IDisposable
    {
        public const uint WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;

        // id layout: [next|prev][main|pad] base + digit
        private const int ID_NEXT_MAIN = 0xB001;   // Ctrl+digit, next, main keyboard
        private const int ID_NEXT_PAD = 0xB011;    // Ctrl+digit, next, numpad
        private const int ID_PREV_MAIN = 0xB101;   // Ctrl+digit, previous, main keyboard
        private const int ID_PREV_PAD = 0xB111;    // Ctrl+digit, previous, numpad

        private readonly Control owner;
        private int nextDigit = -1;   // -1 = not registered
        private int prevDigit = -1;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public HotkeyManager(Control owner)
        {
            this.owner = owner;
        }

        // (Re)register both hotkeys from config values (-1 = disabled).
        // Returns null on success, otherwise a human-readable problem.
        public string Set(int next, int prev)
        {
            Unregister();
            if (next >= 0 && next <= 9 && next == prev)
            {
                string msg = "上一张快捷键不能与下一张相同（都是 Ctrl+" + next + "）";
                Log.Write("hotkey: conflict " + msg);
                return msg;
            }

            string problem = null;
            if (next >= 0 && next <= 9)
            {
                if (!RegisterOne(ID_NEXT_MAIN + next, ID_NEXT_PAD + next, next))
                    problem = "下一张快捷键 Ctrl+" + next + " 注册失败（可能已被其他程序占用）";
            }
            if (prev >= 0 && prev <= 9)
            {
                if (!RegisterOne(ID_PREV_MAIN + prev, ID_PREV_PAD + prev, prev))
                    problem = (problem == null ? "" : problem + "；") +
                              "上一张快捷键 Ctrl+" + prev + " 注册失败（可能已被其他程序占用）";
            }

            nextDigit = next;
            prevDigit = prev;
            if (problem != null)
            {
                Log.Write("hotkey: " + problem);
                return problem;
            }
            Log.Write("hotkey: next=" + next + ", prev=" + prev);
            return null;
        }

        // digit: -1 = disabled, 0-9 = Ctrl+digit. Registers BOTH the main
        // keyboard key and the numpad key so either one works. Returns true
        // when at least one of the two keypads got registered.
        private bool RegisterOne(int idMain, int idPad, int digit)
        {
            bool mainOk = RegisterHotKey(owner.Handle, idMain,
                MOD_CONTROL, (uint)('0' + digit));
            bool padOk = RegisterHotKey(owner.Handle, idPad,
                MOD_CONTROL, (uint)(0x60 + digit));
            return mainOk || padOk;
        }

        public void Unregister()
        {
            if (nextDigit >= 0)
            {
                UnregisterHotKey(owner.Handle, ID_NEXT_MAIN + nextDigit);
                UnregisterHotKey(owner.Handle, ID_NEXT_PAD + nextDigit);
                nextDigit = -1;
            }
            if (prevDigit >= 0)
            {
                UnregisterHotKey(owner.Handle, ID_PREV_MAIN + prevDigit);
                UnregisterHotKey(owner.Handle, ID_PREV_PAD + prevDigit);
                prevDigit = -1;
            }
        }

        // Map a WM_HOTKEY message to the action it represents.
        public HotkeyAction Identify(uint msg, IntPtr wParam)
        {
            if (msg != WM_HOTKEY) return HotkeyAction.None;
            int id = wParam.ToInt32();
            if ((id >= ID_NEXT_MAIN && id <= ID_NEXT_MAIN + 9) ||
                (id >= ID_NEXT_PAD && id <= ID_NEXT_PAD + 9))
            {
                return HotkeyAction.Next;
            }
            if ((id >= ID_PREV_MAIN && id <= ID_PREV_MAIN + 9) ||
                (id >= ID_PREV_PAD && id <= ID_PREV_PAD + 9))
            {
                return HotkeyAction.Prev;
            }
            return HotkeyAction.None;
        }

        public void Dispose()
        {
            Unregister();
        }
    }
}
