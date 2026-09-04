using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WallpaperChanger
{
    // Decide where per-user data (config, log) lives.
    // - Portable run (exe next to writable folder): keep next to the exe.
    // - Installed under Program Files (not writable): fall back to
    //   %LocalAppData%\WallpaperChanger so settings actually persist.
    public static class AppPaths
    {
        private static string dataDir;

        public static string DataDir
        {
            get
            {
                if (dataDir == null) Init();
                return dataDir;
            }
        }

        private static void Init()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                string probe = Path.Combine(dir, ".wc_write_test");
                using (FileStream fs = new FileStream(probe, FileMode.Create, FileAccess.Write,
                    FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                }
                dataDir = dir;
                return;
            }
            catch
            {
                // exe dir not writable
            }

            string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallpaperChanger");
            try
            {
                Directory.CreateDirectory(alt);
                dataDir = alt;
            }
            catch
            {
                dataDir = dir;
            }
        }
    }

    // Plain key=value config file (self-contained, UTF-8).
    // "folder=" may appear multiple times to add several picture folders.
    public static class Config
    {
        public static List<string> Folders = new List<string>();
        public static int IntervalMinutes = 10;   // 1,5,10,30,60,360,720,1440
        public static WallpaperStyle Style = WallpaperStyle.Fill;
        public static bool RandomOrder = true;
        public static bool AutoStart = false;
        public static bool Recursive = true;
        public static int Hotkey = -1;            // -1 none, 0-9 = Ctrl+digit (next wallpaper)
        public static int HotkeyPrev = 8;         // -1 none, 0-9 = Ctrl+digit (previous wallpaper)

        // Manual wallpaper picker: master switch plus the checked file set.
        // When ManualSelectionEnabled is on, every forward switch (manual next,
        // auto timer) draws from scanned images that are also in ManualPicked;
        // the random/order mode keeps working on that smaller pool unchanged.
        public static bool ManualSelectionEnabled = false;
        public static List<string> ManualPicked = new List<string>();

        private static string ConfigPath
        {
            get
            {
                return Path.Combine(AppPaths.DataDir, "WallpaperChanger.ini");
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                Dictionary<string, string> single = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                List<string> folderList = new List<string>();
                List<string> pickedList = new List<string>();
                foreach (string rawLine in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "folder", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val.Length > 0) folderList.Add(val);
                    }
                    else if (string.Equals(key, "picked", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val.Length > 0) pickedList.Add(val);
                    }
                    else
                    {
                        single[key] = val;
                    }
                }

                if (folderList.Count > 0) Folders = folderList;
                ManualPicked = pickedList;
                string v;
                if (single.TryGetValue("interval_minutes", out v))
                {
                    int n;
                    if (int.TryParse(v, out n) && n > 0) IntervalMinutes = n;
                }
                if (single.TryGetValue("style", out v))
                {
                    WallpaperStyle s;
                    if (Enum.TryParse<WallpaperStyle>(v, true, out s)) Style = s;
                }
                if (single.TryGetValue("random", out v)) RandomOrder = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (single.TryGetValue("auto_start", out v)) AutoStart = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (single.TryGetValue("recursive", out v)) Recursive = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (single.TryGetValue("manual_enabled", out v)) ManualSelectionEnabled = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (single.TryGetValue("hotkey", out v))
                {
                    int n;
                    if (int.TryParse(v, out n) && n >= -1 && n <= 9) Hotkey = n;
                }
                if (single.TryGetValue("hotkey_prev", out v))
                {
                    int n;
                    if (int.TryParse(v, out n) && n >= -1 && n <= 9) HotkeyPrev = n;
                }
            }
            catch
            {
                // keep defaults on any parse problem
            }
        }

        public static void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; WallpaperChanger configuration");
                sb.AppendLine("; Multiple 'folder=' lines = multiple source folders");
                sb.AppendLine("; Multiple 'picked=' lines = files checked in the manual picker");
                foreach (string folder in Folders)
                {
                    sb.AppendLine("folder=" + folder);
                }
                sb.AppendLine("manual_enabled=" + (ManualSelectionEnabled ? "1" : "0"));
                foreach (string picked in ManualPicked)
                {
                    sb.AppendLine("picked=" + picked);
                }
                sb.AppendLine("interval_minutes=" + IntervalMinutes);
                sb.AppendLine("style=" + Style);
                sb.AppendLine("random=" + (RandomOrder ? "1" : "0"));
                sb.AppendLine("auto_start=" + (AutoStart ? "1" : "0"));
                sb.AppendLine("recursive=" + (Recursive ? "1" : "0"));
                sb.AppendLine("hotkey=" + Hotkey);
                sb.AppendLine("hotkey_prev=" + HotkeyPrev);
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    public enum WallpaperStyle
    {
        Fill,     // 填充
        Fit,      // 适应
        Stretch,  // 拉伸
        Tile,     // 平铺
        Center,   // 居中
        Span      // 跨区
    }

    public static class StyleNames
    {
        public static readonly string[] Chinese =
        {
            "填充", "适应", "拉伸", "平铺", "居中", "跨区"
        };
    }
}
