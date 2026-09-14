using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WallpaperChanger
{
    // Startup-folder shortcut management (delete the .lnk to disable).
    public static class AutoStartHelper
    {
        // Test seam: only ever set through reflection by the test harness, so
        // the shortcut writer can be exercised against a scratch folder.
        private static string lnkPathOverride = null;

        private static string LnkPath
        {
            get
            {
                if (!string.IsNullOrEmpty(lnkPathOverride)) return lnkPathOverride;
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                return Path.Combine(startup, "WallpaperChanger.lnk");
            }
        }

        public static bool AutoStartExists()
        {
            try { return File.Exists(LnkPath); }
            catch { return false; }
        }

        // Passed by the Startup shortcut so the program comes up in the tray
        // instead of dropping a window in the middle of the screen at boot.
        public const string TrayArgument = "/tray";

        public static void SetAutoStart(bool enable)
        {
            try
            {
                string lnk = LnkPath;
                if (!enable)
                {
                    if (File.Exists(lnk)) File.Delete(lnk);
                    return;
                }

                string exe = Application.ExecutablePath;
                // Rewrite only when the shortcut is missing or stale (old
                // install without the tray argument, or a moved executable).
                if (ShortcutMatches(lnk, exe)) return;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                        new object[] { lnk });
                    Type scType = sc.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc,
                        new object[] { exe });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                        new object[] { Path.GetDirectoryName(exe) });
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc,
                        new object[] { TrayArgument });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
                        new object[] { "WallpaperChanger auto rotate" });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                    Marshal.FinalReleaseComObject(sc);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch
            {
            }
        }

        // True when an existing shortcut already points at this executable
        // with the tray argument.
        private static bool ShortcutMatches(string lnk, string exe)
        {
            if (!File.Exists(lnk)) return false;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                        new object[] { lnk });
                    try
                    {
                        Type scType = sc.GetType();
                        string target = scType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
                        string arguments = scType.InvokeMember("Arguments", BindingFlags.GetProperty, null, sc, null) as string;
                        return string.Equals(target, exe, StringComparison.OrdinalIgnoreCase)
                            && string.Equals((arguments ?? "").Trim(), TrayArgument, StringComparison.OrdinalIgnoreCase);
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(sc);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch
            {
                return false;
            }
        }
    }

    // Friendly name for a wallpaper source folder: its last path segment
    // ("D:\Walls\Nature" -> "Nature"), falling back to the full path for
    // drive roots and the like.
    public static class SourceNames
    {
        public static string Display(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            try
            {
                string n = Path.GetFileName(folder.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch
            {
            }
            return folder;
        }
    }

    // Append-only log (same adaptive location as the config); failures are
    // silently ignored.
    public static class Log
    {
        private static readonly object LockObj = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (LockObj)
                {
                    string path = Path.Combine(AppPaths.DataDir, "WallpaperChanger.log");
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }
}
