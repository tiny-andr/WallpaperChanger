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
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            Config.Load();

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

            // Single-instance guard. A Mutex(true, ...) misdetects an
            // abandoned mutex (left behind when the previous instance was
            // killed with taskkill /F) as "another instance is running" and
            // exits immediately. So create non-owned, then probe with a
            // non-blocking WaitOne: an abandoned mutex surfaces as
            // AbandonedMutexException and we take ownership instead.
            using (Mutex mutex = new Mutex(false, @"Local\WallpaperChanger_SingleInstance", out bool createdNew))
            {
                if (!createdNew)
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
                }
                Application.Run(new MainForm());
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
                else if (Config.Folders != null && Config.Folders.Count > 0)
                {
                    var imgs = ImageScanner.ScanMany(Config.Folders, Config.Recursive);
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
