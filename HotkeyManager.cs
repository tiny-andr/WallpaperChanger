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

    // System-wide hotkeys (RegisterHotKey) - they work regardless of window
    // focus (e.g. while minimized to the tray), but exclusive-fullscreen
    // games such as League of Legends may still intercept the keys.
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
        //
        // The old version called Unregister() FIRST and validated afterwards, so
        // every rejected attempt also destroyed the binding that was working:
        // picking a digit another program already owns, or picking the same digit
        // for both actions, silently left the user with no hotkeys at all - and
        // because the caller had already written the bad digit to the ini, the
        // next launch failed the same way. Now the new bindings are taken while
        // the old ones are still held, the stale ids are only dropped once both
        // new ones are in place, and a failure restores the previous pair.
        public string Set(int next, int prev)
        {
            Log.Write("hotkey: Set(" + next + ", " + prev + ") handle=" + owner.Handle
                + " created=" + owner.IsHandleCreated);

            if (next >= 0 && next <= 9 && next == prev)
            {
                string msg = Loc.F("hotkey.err.same", next);
                Log.Write("hotkey: conflict " + msg);
                return msg;
            }

            string problem = null;
            bool nextBound = false;
            bool prevBound = false;
            if (next >= 0 && next <= 9)
            {
                nextBound = RegisterOne(ID_NEXT_MAIN + next, ID_NEXT_PAD + next, next);
                if (!nextBound) problem = Loc.F("hotkey.err.next", next);
            }
            if (prev >= 0 && prev <= 9)
            {
                prevBound = RegisterOne(ID_PREV_MAIN + prev, ID_PREV_PAD + prev, prev);
                if (!prevBound)
                {
                    problem = (problem == null ? "" : problem + Loc.T("hotkey.err.join")) +
                              Loc.F("hotkey.err.prev", prev);
                }
            }

            if (problem != null)
            {
                // Roll back whatever of the new pair did register and put the
                // previous pair back, so a rejected change is a no-op.
                ReleasePair(next, nextBound, prev, prevBound);
                Rebind(nextDigit, prevDigit);
                Log.Write("hotkey: " + problem + " (rolled back to "
                    + nextDigit + "/" + prevDigit + ")");
                return problem;
            }

            // Success: drop the old bindings the new digits no longer cover.
            ReleaseStale(next, prev);
            nextDigit = next;
            prevDigit = prev;
            Log.Write("hotkey: next=" + next + ", prev=" + prev);
            return null;
        }

        // Frees the ids of a candidate pair that was taken but is being abandoned.
        private void ReleasePair(int next, bool nextBound, int prev, bool prevBound)
        {
            if (nextBound) ReleaseIds(next, ID_NEXT_MAIN, ID_NEXT_PAD);
            if (prevBound) ReleaseIds(prev, ID_PREV_MAIN, ID_PREV_PAD);
        }

        // Frees the previously held ids for the actions whose digit just changed.
        private void ReleaseStale(int next, int prev)
        {
            if (nextDigit >= 0 && nextDigit != next) ReleaseIds(nextDigit, ID_NEXT_MAIN, ID_NEXT_PAD);
            if (prevDigit >= 0 && prevDigit != prev) ReleaseIds(prevDigit, ID_PREV_MAIN, ID_PREV_PAD);
        }

        private void ReleaseIds(int digit, int idMainBase, int idPadBase)
        {
            if (digit < 0 || digit > 9) return;
            try
            {
                UnregisterHotKey(owner.Handle, idMainBase + digit);
                UnregisterHotKey(owner.Handle, idPadBase + digit);
            }
            catch { }
        }

        // Best-effort re-registration of the previous pair (rollback path).
        // A failure here leaves the action unbound and logged rather than
        // throwing out of a UI callback.
        private void Rebind(int next, int prev)
        {
            try
            {
                if (next >= 0 && next <= 9) RegisterOne(ID_NEXT_MAIN + next, ID_NEXT_PAD + next, next);
                if (prev >= 0 && prev <= 9) RegisterOne(ID_PREV_MAIN + prev, ID_PREV_PAD + prev, prev);
            }
            catch { }
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
