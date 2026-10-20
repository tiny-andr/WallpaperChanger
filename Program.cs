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

        // Set for the slash switches that are not part of the product's own
        // interface. A diagnostic run builds the real window and reads whatever
        // ini happens to sit next to the exe, so it must not touch machine-wide
        // state: on 2026-10-17 /flowtest and /ordertest each repointed the
        // user's autostart shortcut at the test build, because that ini said
        // auto_start=1 (see MainForm's SetAutoStart call).
        internal static bool DiagnosticRun;

        private static bool IsDiagnosticRun(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            string a = args[0];
            if (a.Equals("/tray", StringComparison.OrdinalIgnoreCase)) return false;
            if (a.Equals("/minimized", StringComparison.OrdinalIgnoreCase)) return false;
            if (a.Equals("/apply", StringComparison.OrdinalIgnoreCase)) return false;
            if (a.Equals("/current", StringComparison.OrdinalIgnoreCase)) return false;
            return a.StartsWith("/", StringComparison.Ordinal);
        }

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // There was no global handler at all: an exception on the UI thread
            // ended the process and left nothing behind, which is exactly the
            // "it just closed by itself" report. Record it and keep running - a
            // tray app is still useful after one switch has gone wrong.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Log.Write("unhandled ui exception: " + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("unhandled exception: "
                    + (e.ExceptionObject == null ? "<null>" : e.ExceptionObject.ToString()));
            };

            Config.Load();
            Loc.Init();
            DiagnosticRun = IsDiagnosticRun(args);

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

            // Diagnostic:  WallpaperChanger.exe /flowtest <folder> [count]
            if (args.Length > 0 && args[0].Equals("/flowtest", StringComparison.OrdinalIgnoreCase))
            {
                string folder = args.Length > 1 ? args[1] : null;
                int count = 3;
                if (args.Length > 2) int.TryParse(args[2], out count);
                Environment.Exit(folder == null ? 2 : RunFlowTest(folder, count));
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /pickersort <0..5>
            // Exercises the sort setting against the REAL config file, the way
            // the picker does: read what a fresh load sees, write the new value,
            // then re-read it as the next launch would. Prints one line per step
            // so a missing round trip is visible without guessing.
            if (args.Length > 0 && args[0].Equals("/pickersort", StringComparison.OrdinalIgnoreCase))
            {
                int want = 0;
                if (args.Length > 1) int.TryParse(args[1], out want);

                Config.Load();
                Console.WriteLine("loaded        : sort=" + Config.PickerSort);

                ManualPickerForm probe = new ManualPickerForm(null);
                probe.Test_TriggerLoad();
                Console.WriteLine("picker shows  : index=" + probe.Test_SortComboIndex
                    + " of " + probe.Test_SortComboItemCount
                    + " (mode " + probe.Test_CurrentSort + ")");
                probe.Dispose();

                Config.PickerSort = want;
                Config.Save();
                Console.WriteLine("saved         : sort=" + want);

                Config.PickerSort = -777;
                Config.Load();
                Console.WriteLine("reloaded      : sort=" + Config.PickerSort);

                ManualPickerForm again = new ManualPickerForm(null);
                again.Test_TriggerLoad();
                int againIndex = again.Test_SortComboIndex;
                int againMode = again.Test_CurrentSort;
                Console.WriteLine("picker again  : index=" + againIndex
                    + " (mode " + againMode + ")");
                again.Dispose();

                // The drop-down must show what was saved, and the mode behind it
                // must agree, or the grid would draw in a different order than
                // the control claims.
                bool ok = Config.PickerSort == want && againMode == want && againIndex == want;
                Console.WriteLine(ok ? "ROUNDTRIP OK" : "ROUNDTRIP BAD");
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /pickerwindow <targetDpi> <pngPath>
            if (args.Length > 0 && args[0].Equals("/pickerwindow", StringComparison.OrdinalIgnoreCase))
            {
                int wantDpi = 144;
                if (args.Length > 1) int.TryParse(args[1], out wantDpi);
                string shot = args.Length > 2 ? args[2] : "pickerwindow.png";
                Environment.Exit(RunPickerWindow(wantDpi, shot));
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /pickertest
            // Exercises the manual picker's sort orders against a folder whose
            // names, sizes and dates are built here, then asserts the exact
            // expected order for each of the six modes - including the
            // asynchronous path a size/date sort has to wait for. No window is
            // shown, so this runs headless.
            if (args.Length > 0 && args[0].Equals("/pickertest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RunPickerTest());
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /ordertest
            // Assert, as data, the sequence the two time-based rotation modes
            // walk through - the one thing the log can only show after the fact.
            // Builds a throwaway folder, applies no wallpaper, opens no window.
            if (args.Length > 0 && args[0].Equals("/ordertest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RunOrderTest());
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /langtest
            // Builds the window in one language, switches through the other two
            // the way the language drop-down does, and reads every page header
            // and card caption back. No window is shown.
            if (args.Length > 0 && args[0].Equals("/langtest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RunLangTest());
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /dpiwindow <targetDpi> <pngPath> [page]
            if (args.Length > 0 && args[0].Equals("/dpiwindow", StringComparison.OrdinalIgnoreCase))
            {
                int want = 144;
                if (args.Length > 1) int.TryParse(args[1], out want);
                string png = args.Length > 2 ? args[2] : "dpiwindow.png";
                // Optional page index: the window opens on Overview, and the
                // other pages are only laid out when they are shown, so a
                // screenshot of page 1 or 2 has to switch to it first.
                int page = -1;
                if (args.Length > 3) int.TryParse(args[3], out page);
                Environment.Exit(RunDpiWindow(want, png, page));
                return;
            }

            // Diagnostic:  WallpaperChanger.exe /dpitest [targetDpi]
            if (args.Length > 0 && args[0].Equals("/dpitest", StringComparison.OrdinalIgnoreCase))
            {
                int want = 144;
                if (args.Length > 1) int.TryParse(args[1], out want);
                Environment.Exit(RunDpiTest(want));
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

        // Diagnostic:  WallpaperChanger.exe /flowtest <folder> [n]
        // Times every step of a "next wallpaper" click, because the steps after
        // the thumbnail turned out not to be the slow one either:
        //   scan -> pick -> APPLY (SystemParametersInfo) -> push history ->
        //   refresh the card (thumbnail) -> repaint
        // The apply step writes the wallpaper through Windows, which for a
        // 14 MB PNG is a plausible multi-second cost that nothing else can hide.
        private static int RunFlowTest(string folder, int count)
        {
            try
            {
                string[] files = System.IO.Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                if (files.Length == 0) { Log.Write("flowtest: no files"); return 1; }
                int n = Math.Max(1, Math.Min(count, files.Length));

                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(40, 40);
                form.Show();
                Pump(700);

                System.Reflection.MethodInfo push = typeof(MainForm).GetMethod("PushHistory",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                System.Reflection.MethodInfo refresh = typeof(MainForm).GetMethod("RefreshNowCard",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                // Only the first two: the point is the split between phases, and
                // walking the whole folder just makes the run long.
                n = Math.Min(n, 2);

                for (int i = 0; i < n; i++)
                {
                    string p = files[i];
                    Invalidate(p);
                    double mb = new System.IO.FileInfo(p).Length / 1048576.0;

                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    System.Diagnostics.Stopwatch apply = System.Diagnostics.Stopwatch.StartNew();
                    WallpaperEngine.MeasureTrace = delegate (List<string> phases)
                    {
                        Log.Write("flowtest   apply phases: " + string.Join(" ", phases.ToArray()));
                    };
                    WallpaperEngine.Apply(p, Config.Style);
                    apply.Stop();
                    long tApply = sw.ElapsedMilliseconds;
                    push.Invoke(form, new object[] { p });
                    long tPush = sw.ElapsedMilliseconds;
                    refresh.Invoke(form, null);
                    long tRefresh = sw.ElapsedMilliseconds;

                    form.Refresh();
                    Pump(1);
                    long tPaint = sw.ElapsedMilliseconds;

                    Log.Write(string.Format(
                        "flowtest: {0,6:0.00} MB  apply {1,6:0} ms | push {2,5:0} | refresh {3,5:0} | paint {4,5:0} | total {5,6:0} ms  {6}",
                        mb, apply.ElapsedMilliseconds, tPush - tApply, tRefresh - tPush, tPaint - tRefresh,
                        tPaint, System.IO.Path.GetFileName(p)));
                }
                form.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("flowtest error: " + ex);
                return 1;
            }
        }

        private static System.Drawing.Image PreviewBoxProbe(MainForm form)
        {
            PreviewBox box = Field<PreviewBox>(form, "nowPreview");
            return box == null ? null : box.Image;
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

        // Diagnostic:  WallpaperChanger.exe /pickerwindow <targetDpi> <pngPath>
        // Opens the real manual picker on the monitor with that scaling, waits
        // for its scan, and photographs it. Used to confirm the sort row lands
        // where it should - and that the dialog's own DPI handling (AutoScaleMode
        // .Dpi, unlike the main window) is not double-scaling.
        private static int RunPickerWindow(int targetDpi, string pngPath)
        {
            try
            {
                System.Windows.Forms.Screen target = FindMonitorAtDpi(targetDpi);
                if (target == null)
                {
                    Log.Write("pickerwindow: no monitor at " + targetDpi + " DPI");
                    return 2;
                }
                ManualPickerForm form = new ManualPickerForm(null);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(target.Bounds.X + 80, target.Bounds.Y + 80);
                form.Show();
                // The folder scan and the sort metadata load are both async.
                for (int i = 0; i < 60; i++)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                }
                Log.Write("pickerwindow: " + target.DeviceName + " dpi=" + form.DeviceDpi
                    + " size=" + form.Width + "x" + form.Height
                    + " client=" + form.ClientSize.Width + "x" + form.ClientSize.Height);

                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    bool ok = false;
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { ok = PrintWindow(form.Handle, hdc, 2); }
                        finally { g.ReleaseHdc(hdc); }
                    }
                    bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                    Log.Write("pickerwindow: saved " + pngPath + " (" + bmp.Width + "x" + bmp.Height + ", ok=" + ok + ")");
                }
                form.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("pickerwindow error: " + ex);
                return 1;
            }
        }

        // Diagnostic:  WallpaperChanger.exe /pickertest
        //
        // The picker's grid order is the one thing a screenshot cannot check, so
        // it is asserted as data. A throwaway folder is built with names, sizes
        // and modification times chosen so every one of the six sort modes has
        // exactly one correct answer, and each is compared against it.
        //
        // The size/date modes also cover the asynchronous path: their metadata is
        // read on a worker thread, so the test has to wait for it before the
        // order means anything. Skipping that wait is how a sort that "works"
        // ends up showing the previous order.
        private static int RunPickerTest()
        {
            int pass = 0, fail = 0;
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wc-pickertest-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                // Sizes and dates are all distinct so each mode has one answer.
                // The names mix digits on purpose: the natural order (s2 before
                // s10) differs from a plain string sort, and that difference is
                // what the first two checks pin down.
                MakeFile(dir, "zzz.jpg", 400, new DateTime(2020, 1, 2));
                MakeFile(dir, "s2.jpg", 200, new DateTime(2021, 1, 2));
                MakeFile(dir, "s10.jpg", 100, new DateTime(2022, 1, 2));
                MakeFile(dir, "alpha.jpg", 300, new DateTime(2023, 1, 2));
                MakeFile(dir, "beta.png", 500, new DateTime(2024, 1, 2));

                ManualPickerForm form = new ManualPickerForm(null);
                List<string> paths = new List<string>();
                foreach (string f in System.IO.Directory.GetFiles(dir)) paths.Add(f);

                form.Test_SetPaths(paths);
                Check(ref pass, ref fail, "scan yields every file", paths.Count == 5, paths.Count + " files");

                // ---- file name, natural order
                form.Test_SetSort(0);
                form.Test_Sort();
                List<string> asc = form.Test_Names();
                Check(ref pass, ref fail, "name A-Z sorts as a whole list",
                    Join(asc) == "alpha.jpg,beta.png,s2.jpg,s10.jpg,zzz.jpg", Join(asc));
                // The reason the picker uses StrCmpLogicalW instead of a plain
                // string comparison: digits compare as numbers.
                Check(ref pass, ref fail, "name A-Z is natural, s2 before s10",
                    asc.IndexOf("s2.jpg") >= 0 && asc.IndexOf("s10.jpg") >= 0
                    && asc.IndexOf("s2.jpg") < asc.IndexOf("s10.jpg"), Join(asc));

                form.Test_SetSort(1);
                form.Test_Sort();
                List<string> desc = form.Test_Names();
                Check(ref pass, ref fail, "name Z-A is natural reversed",
                    Join(desc) == "zzz.jpg,s10.jpg,s2.jpg,beta.png,alpha.jpg", Join(desc));

                // ---- modification time
                form.Test_SetSort(2);
                form.Test_Sort();
                WaitForMeta(form);
                Check(ref pass, ref fail, "modified newest first",
                    Join(form.Test_Names()) == "beta.png,alpha.jpg,s10.jpg,s2.jpg,zzz.jpg",
                    Join(form.Test_Names()));

                form.Test_SetSort(3);
                form.Test_Sort();
                WaitForMeta(form);
                Check(ref pass, ref fail, "modified oldest first",
                    Join(form.Test_Names()) == "zzz.jpg,s2.jpg,s10.jpg,alpha.jpg,beta.png",
                    Join(form.Test_Names()));

                // ---- file size
                form.Test_SetSort(4);
                form.Test_Sort();
                WaitForMeta(form);
                Check(ref pass, ref fail, "size largest first",
                    Join(form.Test_Names()) == "beta.png,zzz.jpg,alpha.jpg,s2.jpg,s10.jpg",
                    Join(form.Test_Names()));

                form.Test_SetSort(5);
                form.Test_Sort();
                WaitForMeta(form);
                Check(ref pass, ref fail, "size smallest first",
                    Join(form.Test_Names()) == "s10.jpg,s2.jpg,alpha.jpg,zzz.jpg,beta.png",
                    Join(form.Test_Names()));

                // ---- sorting must not disturb the checked set. Checked by name
                // rather than by index: indices are positions in the grid, and
                // reordering the grid is the whole point of this feature.
                form.Test_Pick(System.IO.Path.Combine(dir, "beta.png"), true);
                form.Test_Pick(System.IO.Path.Combine(dir, "s2.jpg"), true);
                string pickedBefore = Join(form.Test_PickedNames());
                foreach (int m in new int[] { 0, 1, 2, 3, 4, 5 })
                {
                    form.Test_SetSort(m);
                    form.Test_Sort();
                    WaitForMeta(form);
                }
                Check(ref pass, ref fail, "checked set survives every sort",
                    Join(form.Test_PickedNames()) == pickedBefore,
                    Join(form.Test_PickedNames()) + " (was " + pickedBefore + ")");

                foreach (int i in new int[] { 0, 3, 5, 99, -1 })
                {
                    Config.PickerSort = i;
                    Config.Save();
                    Config.PickerSort = -999;
                    Config.Load();
                    int want = (i >= 0 && i <= 5) ? i : 0;
                    Check(ref pass, ref fail, "config sort=" + i + " falls back to " + want,
                        Config.PickerSort == want, "got " + Config.PickerSort);
                }
                // ---- the full round trip the real user makes: choose a sort,
                // leave the window, open it again. Setting Config and calling
                // Config.Save() is exactly what OnSortChanged does, so what this
                // checks is whether a NEW window reads that value back.
                //
                // The config is redirected to a throwaway directory first: the
                // real ini must not be touched, and a redirected instance is a
                // faithful stand-in for the next app launch.
                string cfg = System.IO.Path.Combine(dir, "test-config");
                System.IO.Directory.CreateDirectory(cfg);
                AppPaths.DataDirOverride = cfg;
                for (int m = 0; m < 6; m++)
                {
                    Config.PickerSort = m;
                    Config.Save();
                    string cfgFile = System.IO.Path.Combine(AppPaths.DataDir, "WallpaperChanger.ini");
                    string onDisk = System.IO.File.Exists(cfgFile)
                        ? System.IO.File.ReadAllText(cfgFile) : "<no file>";
                    string sortLine = "";
                    foreach (string ln in onDisk.Split('\n'))
                    {
                        if (ln.StartsWith("sort=")) sortLine = ln.Trim();
                    }
                    Log.Write("pickertest roundtrip m=" + m + " mem=" + Config.PickerSort
                        + " path=" + cfgFile + " file[" + sortLine + "]");
                    Console.WriteLine("  m=" + m + " mem=" + Config.PickerSort + " file[" + sortLine + "]");
                    Config.PickerSort = -777;
                    Config.Load();
                    Console.WriteLine("  m=" + m + " after Config.Load=" + Config.PickerSort);
                    Config.PickerSort = m;
                    Config.Save();
                    ManualPickerForm reopened = new ManualPickerForm(null);
                    // OnLoad is what fills the sort drop-down; without a message
                    // loop it never fires on its own, so drive it.
                    reopened.Test_TriggerLoad();
                    int restored = reopened.Test_CurrentSort;
                    Console.WriteLine("  m=" + m + " picker restored=" + restored
                        + " comboIndex=" + reopened.Test_SortComboIndex
                        + " comboItems=" + reopened.Test_SortComboItemCount);
                    Check(ref pass, ref fail, "sort " + m + " comes back after reopening",
                        restored == m, "got " + restored);
                    Check(ref pass, ref fail, "sort " + m + " shows in the drop-down",
                        reopened.Test_SortComboIndex == m,
                        "combo index " + reopened.Test_SortComboIndex);
                    reopened.Dispose();
                }
                AppPaths.DataDirOverride = null;
                Config.PickerSort = 0;
                Config.Save();

                // Regression cover for the reported bug: a sort picked while the
                // scan is still running used to be discarded outright - never
                // applied AND never saved - so reopening the window looked like
                // the choice had not been stored at all.
                for (int m = 1; m <= 5; m++)
                {
                    ManualPickerForm midScan = new ManualPickerForm(null);
                    midScan.Test_TriggerLoad();
                    int persisted = midScan.Test_PickSortWhileScanning(m);
                    Check(ref pass, ref fail, "sort " + m + " picked during the scan is saved",
                        persisted == m, "config got " + persisted);
                    midScan.Dispose();
                }
                Config.PickerSort = 0;
                Config.Save();

                form.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("pickertest error: " + ex);
                fail++;
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }

            Log.Write("pickertest: pass=" + pass + " fail=" + fail);
            Console.WriteLine("RESULT pickertest pass=" + pass + " fail=" + fail);
            return fail == 0 ? 0 : 1;
        }

        // Diagnostic:  WallpaperChanger.exe /ordertest
        //
        // The rotation order is a sequence, and a sequence is exactly what a
        // screenshot cannot check. A throwaway folder is built whose
        // modification times are deliberately in a different order from its file
        // names: if the pool ever falls back to the path order, or to whatever
        // order the scanner handed it over in, the expected walk below cannot
        // come out. Headless - no window is shown and no wallpaper is applied
        // (the source list is emptied before the window is built, see the
        // resume block), and the real ini is never touched (the config is
        // redirected into the same folder).
        private static int RunOrderTest()
        {
            int pass = 0, fail = 0;
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wc-ordertest-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                System.IO.Directory.CreateDirectory(dir);

                // File names in one order, modification times in another. The
                // dates run oldest -> newest as: delta, alpha, echo, bravo,
                // charlie.
                MakeFile(dir, "delta.jpg", 64, new DateTime(2020, 1, 1));
                MakeFile(dir, "alpha.jpg", 64, new DateTime(2020, 1, 2));
                MakeFile(dir, "echo.jpg", 64, new DateTime(2020, 1, 3));
                MakeFile(dir, "bravo.jpg", 64, new DateTime(2020, 1, 4));
                MakeFile(dir, "charlie.jpg", 64, new DateTime(2020, 1, 5));

                List<string> pool = new List<string>();
                foreach (string f in System.IO.Directory.GetFiles(dir)) pool.Add(f);
                CheckOrder(ref pass, ref fail, "pool holds every file", pool.Count == 5, pool.Count + " files");

                string[] newestFirst = { "charlie.jpg", "bravo.jpg", "echo.jpg", "alpha.jpg", "delta.jpg" };
                string[] oldestFirst = { "delta.jpg", "alpha.jpg", "echo.jpg", "bravo.jpg", "charlie.jpg" };

                // ---- the pool order each mode produces
                CheckOrder(ref pass, ref fail, "newest-first orders the pool",
                    NamesOf(MainForm.SortByModTime(pool, true)) == string.Join(",", newestFirst),
                    NamesOf(MainForm.SortByModTime(pool, true)));
                CheckOrder(ref pass, ref fail, "oldest-first orders the pool",
                    NamesOf(MainForm.SortByModTime(pool, false)) == string.Join(",", oldestFirst),
                    NamesOf(MainForm.SortByModTime(pool, false)));

                // The order must be a property of the files, not of the order
                // the scan happened to return them in - otherwise every fresh
                // scan would reshuffle the rotation.
                List<string> reversed = new List<string>(pool);
                reversed.Reverse();
                CheckOrder(ref pass, ref fail, "pool order ignores the incoming order",
                    NamesOf(MainForm.SortByModTime(reversed, true)) == string.Join(",", newestFirst),
                    NamesOf(MainForm.SortByModTime(reversed, true)));

                // ---- the walk the rotation actually performs
                foreach (bool newest in new bool[] { true, false })
                {
                    string label = newest ? "newest-first" : "oldest-first";
                    string[] want = newest ? newestFirst : oldestFirst;
                    List<string> ordered = MainForm.SortByModTime(pool, newest);

                    // Nothing applied yet (first run, or a config with no
                    // last_applied=): start at the front.
                    string first = MainForm.NextInOrder(ordered, null);
                    CheckOrder(ref pass, ref fail, label + " starts at the front of the pool",
                        NameOf(first) == want[0], NameOf(first));

                    // Then visit every entry exactly once, in order.
                    List<string> visited = new List<string>();
                    string cur = first;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        visited.Add(NameOf(cur));
                        cur = MainForm.NextInOrder(ordered, cur);
                    }
                    CheckOrder(ref pass, ref fail, label + " walks the whole pool in order",
                        string.Join(",", visited.ToArray()) == string.Join(",", want),
                        string.Join(",", visited.ToArray()));

                    // ... and wraps around instead of stalling on the last one.
                    CheckOrder(ref pass, ref fail, label + " wraps back to the first",
                        NameOf(cur) == want[0], NameOf(cur));

                    // A wallpaper that left the pool (unchecked, source turned
                    // off, deleted) must not stall the rotation: the walk starts
                    // over at the front instead.
                    string gone = System.IO.Path.Combine(dir, "gone.jpg");
                    CheckOrder(ref pass, ref fail, label + " restarts when the current file left the pool",
                        NameOf(MainForm.NextInOrder(ordered, gone)) == want[0],
                        NameOf(MainForm.NextInOrder(ordered, gone)));
                }

                // ---- equal timestamps must not degenerate into random order
                MakeFile(dir, "tie2.jpg", 64, new DateTime(2021, 6, 1));
                MakeFile(dir, "tie1.jpg", 64, new DateTime(2021, 6, 1));
                List<string> ties = new List<string>();
                ties.Add(System.IO.Path.Combine(dir, "tie2.jpg"));
                ties.Add(System.IO.Path.Combine(dir, "tie1.jpg"));
                CheckOrder(ref pass, ref fail, "equal timestamps fall back to the natural order",
                    NamesOf(MainForm.SortByModTime(ties, true)) == "tie1.jpg,tie2.jpg",
                    NamesOf(MainForm.SortByModTime(ties, true)));

                // ---- config: the new key, its range, the legacy mapping
                string cfg = System.IO.Path.Combine(dir, "cfg");
                System.IO.Directory.CreateDirectory(cfg);
                AppPaths.DataDirOverride = cfg;
                foreach (int m in new int[] { 0, 1, 2 })
                {
                    Config.RotateOrder = m;
                    Config.Save();
                    Config.RotateOrder = -999;
                    Config.Load();
                    CheckOrder(ref pass, ref fail, "rotate_order=" + m + " survives a save/load",
                        Config.RotateOrder == m, "got " + Config.RotateOrder);
                    CheckOrder(ref pass, ref fail, "rotate_order=" + m + " shows random=" + (m == 0 ? 1 : 0),
                        Config.RandomOrder == (m == 0), "got " + Config.RandomOrder);
                }
                foreach (int i in new int[] { 3, -1, 99 })
                {
                    Config.RotateOrder = i;
                    Config.Save();
                    Config.RotateOrder = -999;
                    Config.Load();
                    CheckOrder(ref pass, ref fail, "rotate_order=" + i + " falls back to 0",
                        Config.RotateOrder == 0, "got " + Config.RotateOrder);
                }

                // The upgrade path that matters: an ini written before this key
                // existed has only random=. random=1 stays random; random=0 (the
                // old "walk by file name" mode, which is gone) must land on
                // newest-first rather than silently becoming random.
                WriteLegacyIni(cfg, "1");
                Config.RotateOrder = -999;
                Config.Load();
                CheckOrder(ref pass, ref fail, "legacy random=1 loads as random",
                    Config.RotateOrder == 0, "got " + Config.RotateOrder);
                WriteLegacyIni(cfg, "0");
                Config.RotateOrder = -999;
                Config.Load();
                CheckOrder(ref pass, ref fail, "legacy random=0 loads as newest-first",
                    Config.RotateOrder == 1, "got " + Config.RotateOrder);

                // Once both keys exist, the new one decides.
                Config.RotateOrder = 2;
                Config.Save();
                Config.RotateOrder = -999;
                Config.Load();
                CheckOrder(ref pass, ref fail, "rotate_order= outvotes random=",
                    Config.RotateOrder == 2, "got " + Config.RotateOrder);

                // ---- a restart must not send the walk back to the top
                //
                // Where the sequence stopped is written as last_applied=, and
                // the next session resumes after that entry. This part needs a
                // real window: the field it feeds is filled in the
                // constructor, so asserting the helper alone would still pass
                // with the constructor line deleted - the "the experiment did
                // not test the fix" trap. The source list is emptied before the
                // window is built because the constructor rotates the desktop
                // when it has sources and the current wallpaper is not one.
                Config.Folders = new List<string>();
                Config.RotateOrder = 1;
                Config.LastApplied = System.IO.Path.Combine(dir, "bravo.jpg");
                Config.Save();
                Config.LastApplied = "";   // forget it: only the ini can bring it back
                Config.Load();
                CheckOrder(ref pass, ref fail, "last_applied= survives a save/load",
                    NameOf(Config.LastApplied) == "bravo.jpg", NameOf(Config.LastApplied));

                MainForm form = new MainForm();
                try
                {
                    CheckOrder(ref pass, ref fail, "a restarted session remembers what was on screen",
                        NameOf(form.Test_LastApplied) == "bravo.jpg",
                        NameOf(form.Test_LastApplied));

                    List<string> walking = MainForm.SortByModTime(pool, true);
                    CheckOrder(ref pass, ref fail, "the first pick after a restart continues the walk",
                        NameOf(MainForm.NextInOrder(walking, form.Test_LastApplied)) == "echo.jpg",
                        NameOf(MainForm.NextInOrder(walking, form.Test_LastApplied)));

                    // The writing half of the round trip, driven directly
                    // because a real apply would change the desktop wallpaper.
                    string echo = System.IO.Path.Combine(dir, "echo.jpg");
                    form.Test_RememberApplied(echo);
                    string iniText = System.IO.File.ReadAllText(
                        System.IO.Path.Combine(cfg, "WallpaperChanger.ini"));
                    CheckOrder(ref pass, ref fail, "a landed wallpaper is recorded in the ini",
                        iniText.IndexOf("last_applied=" + echo, StringComparison.OrdinalIgnoreCase) >= 0,
                        "no last_applied=" + echo + " in the ini");
                }
                finally
                {
                    form.Dispose();
                }

                // No key at all (every config written until now) keeps the old
                // behaviour: no record, start at the front.
                Config.LastApplied = "";
                Config.Save();
                Config.Load();
                CheckOrder(ref pass, ref fail, "a config without the key resumes nowhere",
                    NameOf(MainForm.InitialLastApplied()) == "<null>",
                    NameOf(MainForm.InitialLastApplied()));

                AppPaths.DataDirOverride = null;
                Config.RotateOrder = 0;
                Config.LastApplied = "";
            }
            catch (Exception ex)
            {
                Log.Write("ordertest error: " + ex);
                fail++;
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
            Log.Write("ordertest: pass=" + pass + " fail=" + fail);
            Console.WriteLine("RESULT ordertest pass=" + pass + " fail=" + fail);
            return fail == 0 ? 0 : 1;
        }

        // Diagnostic:  WallpaperChanger.exe /langtest
        //
        // Builds the real window in one language, then switches to the others
        // through the same path the language drop-down uses, and reads every
        // page header and card caption back.
        //
        // This is the case /dpiwindow cannot cover: it photographs a window that
        // was built in a single language, so a header that never re-reads its
        // text looks perfect in every screenshot and is still wrong the moment
        // the user switches language - which is exactly how the page titles and
        // card captions shipped broken while every control around them followed.
        //
        // The config is redirected into a throwaway folder (the real ini is
        // never touched) and no window is shown.
        private static int RunLangTest()
        {
            int pass = 0, fail = 0;
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wc-langtest-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                AppPaths.DataDirOverride = dir;
                // The window constructor rotates the desktop when the source
                // list is non-empty and the current wallpaper is not from it,
                // and Config still carries the folders loaded at startup.
                // Building a window must never touch the live wallpaper.
                Config.Folders = new List<string>();
                Config.Language = Loc.Zh;
                Config.Save();
                Loc.SetLanguage(Loc.Zh);

                MainForm form = new MainForm();
                try
                {
                    // The overview page deliberately carries no header of its
                    // own, so it is not in this list: pages 1 and 2 are the
                    // sources and general pages.
                    string[] pageKeys = { null, "nav.sources", "nav.general" };
                    string[] noteKeys = { null, "src.page.note", "gen.page.note" };
                    string[] codes = { Loc.Zh, Loc.En, Loc.Ja };
                    string prevHeader = null;

                    for (int step = 0; step < codes.Length; step++)
                    {
                        string code = codes[step];
                        if (step > 0) form.Test_SwitchLanguage(code);

                        CheckLang(ref pass, ref fail, code + ": Loc reports the language",
                            Loc.Language == code, "got " + Loc.Language);

                        for (int p = 1; p < pageKeys.Length; p++)
                        {
                            string wantT = Loc.T(pageKeys[p]);
                            string wantN = Loc.T(noteKeys[p]);
                            CheckLang(ref pass, ref fail, code + ": page " + p + " header follows",
                                form.Test_PageTitle(p) == wantT,
                                "[" + form.Test_PageTitle(p) + "] want [" + wantT + "]");
                            CheckLang(ref pass, ref fail, code + ": page " + p + " note follows",
                                form.Test_PageNote(p) == wantN,
                                "[" + form.Test_PageNote(p) + "] want [" + wantN + "]");
                        }

                        // "It equals Loc.T(...)" is not enough on its own: a
                        // string frozen in the build language also satisfies it
                        // on the first pass. It has to differ from the previous
                        // language as well.
                        if (prevHeader != null)
                        {
                            CheckLang(ref pass, ref fail,
                                code + ": the sources header really changed",
                                form.Test_PageTitle(1) != prevHeader,
                                "still [" + form.Test_PageTitle(1) + "]");
                        }
                        prevHeader = form.Test_PageTitle(1);

                        int cards = form.Test_CardCount;
                        CheckLang(ref pass, ref fail, code + ": the cards are registered",
                            cards >= 5, cards + " cards");
                        int bad = 0;
                        string badWhat = "";
                        for (int i = 0; i < cards; i++)
                        {
                            string tk = form.Test_CardTitleKey(i);
                            string nk = form.Test_CardNoteKey(i);
                            if (tk != null && form.Test_CardTitle(i) != Loc.T(tk))
                            {
                                bad++;
                                badWhat = "card " + i + " title [" + form.Test_CardTitle(i)
                                    + "] want [" + Loc.T(tk) + "]";
                            }
                            if (nk != null && form.Test_CardNote(i) != Loc.T(nk))
                            {
                                bad++;
                                badWhat = "card " + i + " note [" + form.Test_CardNote(i)
                                    + "] want [" + Loc.T(nk) + "]";
                            }
                        }
                        CheckLang(ref pass, ref fail, code + ": every card caption follows",
                            bad == 0, bad + " wrong, last: " + badWhat);
                    }
                }
                finally
                {
                    form.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Write("langtest error: " + ex);
                fail++;
            }
            finally
            {
                AppPaths.DataDirOverride = null;
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
            Log.Write("langtest: pass=" + pass + " fail=" + fail);
            Console.WriteLine("RESULT langtest pass=" + pass + " fail=" + fail);
            return fail == 0 ? 0 : 1;
        }

        private static void CheckLang(ref int pass, ref int fail, string what, bool ok, string got)
        {
            if (ok)
            {
                pass++;
                Log.Write("langtest PASS " + what);
            }
            else
            {
                fail++;
                Log.Write("langtest FAIL " + what + " [got: " + got + "]");
                Console.WriteLine("FAIL " + what + " [got: " + got + "]");
            }
        }

        // An ini exactly as a build without rotate_order= wrote it.
        private static void WriteLegacyIni(string cfgDir, string randomValue)
        {
            System.IO.Directory.CreateDirectory(cfgDir);
            string path = System.IO.Path.Combine(cfgDir, "WallpaperChanger.ini");
            System.IO.File.WriteAllText(path,
                "; legacy config, no rotate_order key\nrandom=" + randomValue + "\ninterval_minutes=10\n",
                new System.Text.UTF8Encoding(false));
        }

        private static string NameOf(string path)
        {
            return path == null ? "<null>" : System.IO.Path.GetFileName(path);
        }

        private static string NamesOf(List<string> paths)
        {
            List<string> names = new List<string>();
            foreach (string p in paths) names.Add(NameOf(p));
            return string.Join(",", names.ToArray());
        }

        private static void CheckOrder(ref int pass, ref int fail, string what, bool ok, string got)
        {
            if (ok)
            {
                pass++;
                Log.Write("ordertest PASS " + what);
            }
            else
            {
                fail++;
                Log.Write("ordertest FAIL " + what + " [got: " + got + "]");
                Console.WriteLine("FAIL " + what + " [got: " + got + "]");
            }
        }

        private static void MakeFile(string dir, string name, int size, DateTime modified)
        {
            string p = System.IO.Path.Combine(dir, name);
            byte[] data = new byte[size];
            // JPEG magic, so ImageScanner's header check accepts the file.
            data[0] = 0xFF; data[1] = 0xD8; data[2] = 0xFF;
            System.IO.File.WriteAllBytes(p, data);
            System.IO.File.SetLastWriteTimeUtc(p, modified.ToUniversalTime());
        }

        private static string Join(List<string> items)
        {
            return string.Join(",", items.ToArray());
        }

        private static void Check(ref int pass, ref int fail, string what, bool ok, string got)
        {
            if (ok)
            {
                pass++;
                Log.Write("pickertest PASS " + what);
            }
            else
            {
                fail++;
                Log.Write("pickertest FAIL " + what + " [got: " + got + "]");
                Console.WriteLine("FAIL " + what + " [got: " + got + "]");
            }
        }

        // The size/date sorts read their metadata on a worker thread and marshal
        // the result back, so the flag is polled while pumping messages.
        private static void WaitForMeta(ManualPickerForm form)
        {
            for (int i = 0; i < 150; i++)
            {
                if (form.Test_MetaComplete && !form.Test_MetaLoading)
                {
                    form.Test_Sort();      // what OnMetaLoaded does
                    return;
                }
                System.Threading.Thread.Sleep(100);
                System.Windows.Forms.Application.DoEvents();
            }
            Log.Write("pickertest: timed out waiting for file metadata");
        }

        // Diagnostic:  WallpaperChanger.exe /dpitest [targetDpi]
        // Moves the window between monitors with different scaling - the "drag it
        // to the 4K screen" case - and reports the DPI every layer believes it is
        // on. If a child reports a different DPI than its parent, the child is
        // painted at one scale inside a box laid out at another, which is how the
        // text ends up overlapping the controls.
        // Diagnostic:  WallpaperChanger.exe /dpiwindow <targetDpi> <pngPath>
        // Opens the real window on the monitor with that scaling, waits for the
        // layout to settle, and writes a PNG of it. This is the honest way to
        // look at a 150% window from a process that is not itself on that
        // monitor: the coordinate spaces of a caller sitting on a 100% screen do
        // not describe the 4K window at all.
        private static int RunDpiWindow(int targetDpi, string pngPath, int pageIndex)
        {
            try
            {
                System.Windows.Forms.Screen target = FindMonitorAtDpi(targetDpi);
                if (target == null)
                {
                    Log.Write("dpiwindow: no monitor at " + targetDpi + " DPI");
                    return 2;
                }
                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(target.Bounds.X + 60, target.Bounds.Y + 60);
                form.Show();
                Pump(2000);

                // Optional page switch (0 overview / 1 sources / 2 general).
                // ShowPage is private - it is the rail's own path, and calling
                // it is what makes the page lay itself out at the live DPI.
                if (pageIndex >= 0)
                {
                    System.Reflection.MethodInfo m = typeof(MainForm).GetMethod("ShowPage",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (m != null)
                    {
                        m.Invoke(form, new object[] { pageIndex });
                        Pump(500);
                        Log.Write("dpiwindow: page=" + pageIndex);
                    }
                }

                Log.Write("dpiwindow: " + target.DeviceName + " formDpi=" + form.DeviceDpi
                    + " size=" + form.Width + "x" + form.Height
                    + " fontPx=" + (form.Font.SizeInPoints * form.DeviceDpi / 72.0).ToString("0.0"));

                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    bool ok = false;
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { ok = PrintWindow(form.Handle, hdc, 2); }
                        finally { g.ReleaseHdc(hdc); }
                    }
                    bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                    Log.Write("dpiwindow: saved " + pngPath + " (" + bmp.Width + "x" + bmp.Height + ", ok=" + ok + ")");
                }
                form.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("dpiwindow error: " + ex);
                return 1;
            }
        }

        private static System.Windows.Forms.Screen FindMonitorAtDpi(int targetDpi)
        {
            foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
            {
                IntPtr mon = MonitorFromPoint(new POINT
                {
                    X = s.Bounds.X + s.Bounds.Width / 2,
                    Y = s.Bounds.Y + s.Bounds.Height / 2
                }, 0);
                uint dx = 0, dy = 0;
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0, out dx, out dy) == 0
                    && (int)dx == targetDpi)
                {
                    return s;
                }
            }
            return null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        private static int RunDpiTest(int targetDpi)
        {
            try
            {
                MainForm form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                System.Windows.Forms.Screen target = null;
                foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
                {
                    IntPtr mon = MonitorFromPoint(new POINT
                    {
                        X = s.Bounds.X + s.Bounds.Width / 2,
                        Y = s.Bounds.Y + s.Bounds.Height / 2
                    }, 0);
                    uint dx = 0, dy = 0;
                    if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0, out dx, out dy) == 0
                        && (int)dx == targetDpi)
                    {
                        target = s;
                        break;
                    }
                }
                if (target == null)
                {
                    Log.Write("dpitest: no monitor at " + targetDpi + " DPI");
                    return 2;
                }

                form.Location = new System.Drawing.Point(target.Bounds.X + 60, target.Bounds.Y + 60);
                form.Show();
                Pump(1200);
                Report(form, "on " + target.DeviceName + " (want " + targetDpi + " DPI)");

                foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
                {
                    if (s.DeviceName == target.DeviceName) continue;
                    form.Location = new System.Drawing.Point(s.Bounds.X + 60, s.Bounds.Y + 60);
                    Pump(1200);
                    Report(form, "moved to " + s.DeviceName);
                    form.Location = new System.Drawing.Point(target.Bounds.X + 60, target.Bounds.Y + 60);
                    Pump(1200);
                    Report(form, "moved back to " + target.DeviceName);
                    break;
                }

                form.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("dpitest error: " + ex);
                return 1;
            }
        }

        private static void Report(MainForm form, string label)
        {
            System.Windows.Forms.Control content = Field<System.Windows.Forms.Control>(form, "content");
            System.Windows.Forms.Control prev = Field<System.Windows.Forms.Control>(form, "btnPrev");
            System.Windows.Forms.Control next = Field<System.Windows.Forms.Control>(form, "btnNext");
            System.Windows.Forms.Control hero = Field<System.Windows.Forms.Control>(form, "nowPreview");
            Log.Write(string.Format(
                "dpitest {0}: formDpi={1} size={2}x{3} fontPx={4:0.0} | content={5}x{6} dpi={7} | prev={8}x{9} dpi={10} | next={11}x{12} | preview={13}x{14} dpi={15}",
                label, form.DeviceDpi, form.Width, form.Height,
                form.Font.SizeInPoints * form.DeviceDpi / 72.0,
                content == null ? -1 : content.Width, content == null ? -1 : content.Height,
                content == null ? -1 : content.DeviceDpi,
                prev == null ? -1 : prev.Width, prev == null ? -1 : prev.Height,
                prev == null ? -1 : prev.DeviceDpi,
                next == null ? -1 : next.Width, next == null ? -1 : next.Height,
                hero == null ? -1 : hero.Width, hero == null ? -1 : hero.Height,
                hero == null ? -1 : hero.DeviceDpi));
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);

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
