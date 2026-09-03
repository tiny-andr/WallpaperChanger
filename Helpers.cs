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
        private static string LnkPath
        {
            get
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                return Path.Combine(startup, "WallpaperChanger.lnk");
            }
        }

        public static bool AutoStartExists()
        {
            try { return File.Exists(LnkPath); }
            catch { return false; }
        }

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
                if (File.Exists(lnk)) return;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                        new object[] { lnk });
                    Type scType = sc.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc,
                        new object[] { Application.ExecutablePath });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                        new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
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
