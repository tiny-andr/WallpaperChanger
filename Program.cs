using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WallpaperChanger
{
    public static class Program
    {
        // Named event the running copy listens on, so a second launch can ask it
        // to come to the front (it may be sitting in the tray with no window).
        private const string ShowEventName = @"Local\WallpaperChanger_ShowWindow";

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            Config.Load();
            Loc.Init();

            // Silent apply mode:  WallpaperChanger.exe /apply [file-or-folder]
            // exit codes: 0 ok, 1 apply failed, 2 bad argument, 3 no images
            if (args.Length > 0 && args[0].Equals("/apply", StringComparison.OrdinalIgnoreCase))
            {
                string target = args.Length > 1 ? args[1] : null;
                Environment.Exit(RunSilentApply(target));
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /current
            // Logs the current wallpaper path on every monitor, exit 0.
            if (args.Length > 0 && args[0].Equals("/current", StringComparison.OrdinalIgnoreCase))
            {
                List<string> cur = WallpaperEngine.GetCurrentWallpaperPaths();
                if (cur.Count == 0)
                {
                    Log.Write("current: <unavailable>");
                }
                else
                {
                    foreach (string p in cur) Log.Write("current: " + p);
                }
                Environment.Exit(0);
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /layout [/page=N]
            // Opens the real window, writes the on-screen rectangle of every
            // named control to the log, then exits. UI regression scripts need
            // real coordinates to click, and hand-copied ones go stale as soon
            // as a card changes height.
            if (args.Length > 0 && args[0].Equals("/layout", StringComparison.OrdinalIgnoreCase))
            {
                int page = 0;
                foreach (string a in args)
                {
                    if (a.StartsWith("/page=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(a.Substring("/page=".Length), out page);
                    }
                }
                Environment.Exit(RunLayoutDump(page));
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /ddtest
            if (args.Length > 0 && args[0].Equals("/ddtest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RunDropDownTest());
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /probe
            if (args.Length > 0 && args[0].Equals("/probe", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RunStateProbe());
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /keytest
            if (args.Length > 0 && args[0].Equals("/keytest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RunKeyCapTest());
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /previewtest <folder> [count]
            if (args.Length > 0 && args[0].Equals("/previewtest", StringComparison.OrdinalIgnoreCase))
            {
                string folder = args.Length > 1 ? args[1] : null;
                int count = 3;
                if (args.Length > 2) int.TryParse(args[2], out count);
                Environment.Exit(folder == null ? 2 : RunPreviewTest(folder, count));
                return;
            }

            // Started by the Startup shortcut: come up in the tray, no window.
            bool startInTray = false;
            foreach (string a in args)
            {
                if (a.Equals("/tray", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase))
                {
                    startInTray = true;
                }
            }

            // Single-instance guard. Ownership is the test, not createdNew:
            // new Mutex(false, ...) hands back an *unowned* mutex, so checking
            // createdNew let a second process take the free mutex and run a
            // second copy. Owning it here also keeps Mutex(true, ...) out of
            // the way, since that overload treats a mutex left behind by a
            // killed instance as "another copy is running" and exits at once.
            using (Mutex mutex = new Mutex(false, @"Local\WallpaperChanger_SingleInstance"))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;   // previous owner crashed; we own it now
                }

                if (!acquired)
                {
                    ActivateExisting();
                    return;
                }

                MainForm form = new MainForm(startInTray);
                ListenForShowRequests(form);
                Application.Run(form);
            }
        }

        // Wait on the named event in the background and surface the window when
        // another launch sets it. The thread is a background thread so it never
        // keeps the process alive on its own.
        private static void ListenForShowRequests(MainForm form)
        {
            try
            {
                IntPtr handle = form.Handle;   // created up front so BeginInvoke is valid
                if (handle == IntPtr.Zero) return;

                EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                Thread t = new Thread(delegate ()
                {
                    while (true)
                    {
                        try
                        {
                            ev.WaitOne();
                            form.BeginInvoke(new Action(delegate { form.ShowFromTray(); }));
                        }
                        catch
                        {
                            return;
                        }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch
            {
            }
        }

        // Walk the live window and log where each control actually is, in
        // screen coordinates, so a UI script can click real pixels. Fields are
        // found by reflection over MainForm: the point is to report the
        // product's own controls, not a hand-maintained list that drifts.
        private static int RunLayoutDump(int page)
        {
            try
            {
                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(40, 40);
                form.Show();

                System.Reflection.MethodInfo showPage = typeof(MainForm).GetMethod("ShowPage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (showPage != null) showPage.Invoke(form, new object[] { page });
                for (int i = 0; i < 30; i++)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(20);
                }

                System.Drawing.Rectangle fr = form.RectangleToScreen(form.ClientRectangle);
                Log.Write("layout: {\"window\":\"" + fr.X + "," + fr.Y + "," + fr.Width + "," + fr.Height + "\"}");

                foreach (System.Reflection.FieldInfo fi in typeof(MainForm).GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public))
                {
                    System.Windows.Forms.Control c = fi.GetValue(form) as System.Windows.Forms.Control;
                    if (c == null) continue;
                    if (!c.IsHandleCreated) continue;
                    System.Drawing.Rectangle r = c.RectangleToScreen(c.ClientRectangle);
                    Log.Write("layout: {\"name\":\"" + fi.Name + "\",\"type\":\"" + c.GetType().Name
                        + "\",\"rect\":\"" + r.X + "," + r.Y + "," + r.Width + "," + r.Height
                        + "\",\"visible\":" + (c.Visible ? "true" : "false") + "}");
                }
                form.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("layout error: " + ex.Message);
                return 1;
            }
        }

        // Diagnostic:  WallpaperChanger.exe /ddtest
        // Opens every KitDropdown in the real window twice, picking an item in
        // between, and reports whether the list came up each time. This is the
        // "the drop-down only opens once" report: the second open is the one
        // that was broken, and only the product's own click path can show it.
        private static int RunDropDownTest()
        {
            int failures = 0;
            try
            {
                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(40, 40);
                form.Show();
                Pump(600);

                string[] fields = { "cmbStyle", "cmbInterval", "cmbOrder" };
                System.Reflection.MethodInfo showPage = typeof(MainForm).GetMethod("ShowPage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                foreach (string f in fields) failures += DropDownCycle(form, f);
                if (showPage != null) showPage.Invoke(form, new object[] { 2 });
                Pump(400);
                failures += DropDownCycle(form, "cmbLang");

                form.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("ddtest error: " + ex);
                failures++;
            }
            Log.Write("ddtest: failures=" + failures);
            return failures == 0 ? 0 : 1;
        }

        private static int DropDownCycle(MainForm form, string field)
        {
            System.Reflection.FieldInfo fi = typeof(MainForm).GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            KitDropdown box = fi == null ? null : fi.GetValue(form) as KitDropdown;
            if (box == null || !box.Visible)
            {
                Log.Write("ddtest " + field + ": not found or hidden");
                return 1;
            }

            int bad = 0;
            for (int round = 1; round <= 3; round++)
            {
                Click(box);
                Pump(200);
                bool open = box.IsOpen;
                if (!open) bad++;
                Log.Write("ddtest " + field + " round " + round + ": opened=" + open
                    + " selected=" + box.SelectedIndex + " items=" + box.Items.Length);
                if (open)
                {
                    PickMiddle(box);
                    Pump(250);
                    if (box.IsOpen)
                    {
                        Log.Write("ddtest " + field + " round " + round + ": still open after picking");
                        bad++;
                    }
                }
            }
            return bad == 0 ? 0 : 1;
        }

        private static void Click(Control c)
        {
            System.Reflection.MethodInfo down = typeof(Control).GetMethod("OnMouseDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo up = typeof(Control).GetMethod("OnMouseUp",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Windows.Forms.MouseEventArgs e =
                new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 5, 5, 0);
            down.Invoke(c, new object[] { e });
            up.Invoke(c, new object[] { e });
        }

        // Pick the row after the current one by driving the popup the way a
        // click does, then make sure the popup took itself down.
        private static void PickMiddle(KitDropdown box)
        {
            int next = box.SelectedIndex + 1;
            if (next >= box.Items.Length) next = 0;
            foreach (System.Windows.Forms.Form f in System.Windows.Forms.Application.OpenForms)
            {
                if (f == box.FindForm() || !f.Visible) continue;
                System.Reflection.MethodInfo md = f.GetType().GetMethod("OnMouseDown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                int rowH = (int)f.GetType().GetProperty("RowHeight").GetValue(f, null);
                int pad = (int)f.GetType().GetProperty("ListPad").GetValue(f, null);
                int y = pad + next * rowH + rowH / 2;
                md.Invoke(f, new object[] {
                    new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 20, y, 0) });
                return;
            }
        }

        private static void Pump(int ms)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
        }

        // Diagnostic:  WallpaperChanger.exe /probe
        // Logs the live state of the pieces that are easy to get wrong and
        // hard to see: the rail's pause card, the overview's fact numbers, and
        // the two hero buttons' widths.
        private static int RunStateProbe()
        {
            try
            {
                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(40, 40);
                form.Show();
                Pump(800);

                NavRail rail = Field<NavRail>(form, "rail");
                StatusBar footer = Field<StatusBar>(form, "footer");
                Log.Write("probe: rail rotating=" + rail.Rotating
                    + " countdown='" + rail.Countdown + "'"
                    + " caption='" + rail.CountdownCaption + "'"
                    + " pauseText='" + rail.PauseText + "'");
                Log.Write("probe: footer main='" + footer.MainText + "' sub='" + footer.SubText
                    + "' notice='" + footer.Notice + "'");

                System.Windows.Forms.Control prev = Field<System.Windows.Forms.Control>(form, "btnPrev");
                System.Windows.Forms.Control next = Field<System.Windows.Forms.Control>(form, "btnNext");
                if (prev != null && next != null)
                {
                    Log.Write("probe: buttons prev=" + prev.Width + "x" + prev.Height
                        + " next=" + next.Width + "x" + next.Height
                        + " equal=" + (prev.Width == next.Width));
                }
                for (int i = 0; i < 3; i++)
                {
                    System.Windows.Forms.Control v = Field<System.Windows.Forms.Control>(form, "factVal" + i);
                    if (v != null) Log.Write("probe: fact[" + i + "]='" + v.Text + "'");
                }
                Log.Write("probe: hidden labels gone: lblNowName=" + (Field<System.Windows.Forms.Control>(form, "lblNowName") == null
                    ? "absent" : "PRESENT")
                    + " lblNowPath=" + (Field<System.Windows.Forms.Control>(form, "lblNowPath") == null ? "absent" : "PRESENT")
                    + " lblKbdHint=" + (Field<System.Windows.Forms.Control>(form, "lblKbdHint") == null ? "absent" : "PRESENT"));
                Log.Write("probe: interval=" + Config.IntervalMinutes + " manualPicked=" + Config.ManualPicked.Count);

                // The paused state is the one the card got wrong, so report it
                // too: the toggle goes through the product's own path.
                System.Reflection.MethodInfo toggle = typeof(MainForm).GetMethod("TogglePause",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (toggle != null)
                {
                    toggle.Invoke(form, null);
                    Pump(400);
                    Log.Write("probe: after pause -> rotating=" + rail.Rotating
                        + " countdown='" + rail.Countdown + "'"
                        + " caption='" + rail.CountdownCaption + "'"
                        + " pauseText='" + rail.PauseText + "'");
                    toggle.Invoke(form, null);
                    Pump(400);
                    Log.Write("probe: after resume -> rotating=" + rail.Rotating
                        + " countdown='" + rail.Countdown + "'"
                        + " caption='" + rail.CountdownCaption + "'"
                        + " pauseText='" + rail.PauseText + "'");
                }
                form.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("probe error: " + ex.Message);
                return 1;
            }
        }

        // Diagnostic:  WallpaperChanger.exe /keytest
        // Drives the hotkey capture button the way a click and a keypress do,
        // and reports what the button shows and what the config holds after it.
        // The report is "it stays on 按下组合键 until I leave the page and come
        // back", which points at the capture either not ending or ending
        // without a repaint.
        private static int RunKeyCapTest()
        {
            int bad = 0;
            try
            {
                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(40, 40);
                form.Show();
                Pump(600);

                System.Reflection.MethodInfo showPage = typeof(MainForm).GetMethod("ShowPage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (showPage != null) showPage.Invoke(form, new object[] { 2 });
                Pump(500);

                KeyCapButton cap = Field<KeyCapButton>(form, "keyPrev");
                if (cap == null) { Log.Write("keytest: keyPrev not found"); return 1; }

                int was = Config.HotkeyPrev;
                Log.Write("keytest: before value=" + cap.Value + " capturing=" + cap.Capturing
                    + " config=" + Config.HotkeyPrev + " visible=" + cap.Visible);

                System.Reflection.MethodInfo down = typeof(System.Windows.Forms.Control).GetMethod("OnMouseDown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                down.Invoke(cap, new object[] {
                    new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 5, 5, 0) });
                Pump(200);
                Log.Write("keytest: after click capturing=" + cap.Capturing
                    + " focused=" + cap.Focused + " anyCapturing=" + KeyCapButton.AnyCapturing);

                System.Reflection.MethodInfo kd = typeof(System.Windows.Forms.Control).GetMethod("OnKeyDown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                // Bind a key that is NOT the current one: re-setting the same
                // value would pass even if the handler never ran.
                int target = Config.HotkeyPrev == 5 ? 6 : 5;
                down.Invoke(cap, new object[] {
                    new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 5, 5, 0) });
                Pump(200);
                kd.Invoke(cap, new object[] {
                    new System.Windows.Forms.KeyEventArgs((System.Windows.Forms.Keys)(System.Windows.Forms.Keys.D0 + target)
                        | System.Windows.Forms.Keys.Control) });
                Pump(400);
                Log.Write("keytest: pressed Ctrl+" + target + " -> value=" + cap.Value
                    + " capturing=" + cap.Capturing + " config=" + Config.HotkeyPrev);
                if (cap.Capturing) { bad++; Log.Write("keytest: STILL CAPTURING after the keypress"); }
                if (cap.Value != target) { bad++; Log.Write("keytest: the button value did not take the key"); }
                if (Config.HotkeyPrev != target) { bad++; Log.Write("keytest: the config did not take the key"); }

                // And the button's label must read the new binding on the next
                // paint. "It only shows up after I leave the page and come
                // back" means this is where it goes wrong.
                System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(cap.Width, cap.Height);
                cap.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, cap.Width, cap.Height));
                int accent = 0, ink = 0;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        System.Drawing.Color c = bmp.GetPixel(x, y);
                        if (c.R < 150 && c.G < 150 && c.B < 150) ink++;
                        // AccentSoft / AccentText are the capture state's colours.
                        if (Theme.Current == AppTheme.Light
                            ? (c.B > 200 && c.R < 120)
                            : (c.B > 180 && c.R < 120)) accent++;
                    }
                }
                Log.Write("keytest: paint after binding -> ink=" + ink + " accentish=" + accent
                    + " (capture state would be mostly accent, a bound label is not)");
                bmp.Dispose();

                cap.Value = was;
                form.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("keytest error: " + ex);
                bad++;
            }
            Log.Write("keytest: failures=" + bad);
            return bad == 0 ? 0 : 1;
        }

        // Diagnostic:  WallpaperChanger.exe /previewtest <folder> [n]
        // Times what the user sees: how long after switching to a wallpaper the
        // "now showing" box actually holds a picture. The thumbnails for the
        // files it will use are deleted first, so this measures the cold path
        // that produced the "it takes five or six seconds" report.
        private static int RunPreviewTest(string folder, int count)
        {
            try
            {
                string[] files = System.IO.Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                if (files.Length == 0) { Log.Write("previewtest: no files"); return 1; }
                int n = Math.Max(1, Math.Min(count, files.Length));

                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(40, 40);
                form.Show();
                Pump(700);

                PreviewBox box = Field<PreviewBox>(form, "nowPreview");
                System.Reflection.MethodInfo push = typeof(MainForm).GetMethod("PushHistory",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                System.Reflection.MethodInfo refresh = typeof(MainForm).GetMethod("RefreshNowCard",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                // Drop the cached thumbnails for the files this will walk, at
                // both sizes the app asks for, so every step is a cold one.
                for (int i = 0; i < n; i++) Invalidate(files[i]);

                for (int i = 0; i < n; i++)
                {
                    string p = files[i];
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    push.Invoke(form, new object[] { p });
                    refresh.Invoke(form, null);
                    long sync = sw.ElapsedMilliseconds;
                    long ready = -1;
                    while (sw.ElapsedMilliseconds < 20000)
                    {
                        Pump(25);
                        if (box.Image != null) { ready = sw.ElapsedMilliseconds; break; }
                    }
                    double mb = new System.IO.FileInfo(p).Length / 1048576.0;
                    Log.Write(string.Format("previewtest: {0,7:0.00} MB  sync block {1} ms, image at {2} ms   {3}",
                        mb, sync, ready, System.IO.Path.GetFileName(p)));
                }
                form.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("previewtest error: " + ex);
                return 1;
            }
        }

        // Deletes the thumbnail cache entries for one source file, both sizes.
        private static void Invalidate(string path)
        {
            try
            {
                string cache = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WallpaperChanger", "thumbs");
                foreach (int[] size in new int[][] {
                    new int[] { 374, 210 }, new int[] { 264, 148 } })
                {
                    string key = ThumbKey(path, size[0], size[1]);
                    string f = System.IO.Path.Combine(cache, key + ".png");
                    if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
                }
            }
            catch { }
        }

        private static string ThumbKey(string path, int w, int h)
        {
            long len = 0, ticks = 0;
            try
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(path);
                len = fi.Length;
                ticks = fi.LastWriteTimeUtc.Ticks;
            }
            catch { }
            string seed = path + "|" + len + "|" + ticks + "|" + w + "x" + h;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
                System.Text.StringBuilder sb = new System.Text.StringBuilder(32);
                for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static T Field<T>(object o, string name) where T : class
        {
            System.Reflection.FieldInfo fi = o.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            return fi == null ? null : fi.GetValue(o) as T;
        }

        private static int RunSilentApply(string target)
        {
            try
            {
                string path = null;
                if (!string.IsNullOrEmpty(target))
                {
                    if (Directory.Exists(target))
                    {
                        var imgs = ImageScanner.Scan(target, Config.Recursive);
                        if (imgs.Count == 0) return 3;
                        path = imgs[new Random().Next(imgs.Count)];
                    }
                    else if (File.Exists(target))
                    {
                        path = target;
                    }
                    else
                    {
                        return 2;
                    }
                }
                else if (Config.EnabledFolders().Count > 0)
                {
                    var imgs = ImageScanner.ScanMany(Config.EnabledFolders(), Config.Recursive);
                    if (imgs.Count == 0) return 3;
                    path = imgs[new Random().Next(imgs.Count)];
                }
                else
                {
                    return 2;
                }

                bool ok = WallpaperEngine.Apply(path, Config.Style);
                if (ok) Log.Write("silent apply: " + path);
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Write("silent apply error: " + ex.Message);
                return 1;
            }
        }

        private static void ActivateExisting()
        {
            // The running copy may be hidden in the tray, in which case it has
            // no main window to restore - ask it through the named event.
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(ShowEventName))
                {
                    ev.Set();
                }
                return;
            }
            catch
            {
            }

            // Fallback for an instance that predates the event.
            try
            {
                foreach (Process p in Process.GetProcessesByName("WallpaperChanger"))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                        SetForegroundWindow(p.MainWindowHandle);
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
