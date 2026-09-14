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
