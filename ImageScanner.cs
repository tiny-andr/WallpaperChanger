using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace WallpaperChanger
{
    // Scan the picture folder recursively, filter by extension whitelist,
    // verify magic bytes, natural-sort like Explorer.
    public static class ImageScanner
    {
        private static readonly HashSet<string> AllowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jfif", ".jpe", ".png", ".bmp", ".webp", ".gif", ".tif", ".tiff"
        };

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public static List<string> Scan(string root, bool recursive)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return result;

            try
            {
                SearchOption opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (string file in Directory.EnumerateFiles(root, "*", opt))
                {
                    try
                    {
                        string ext = Path.GetExtension(file);
                        if (!AllowedExts.Contains(ext)) continue;
                        FileAttributes attr = File.GetAttributes(file);
                        if ((attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0) continue;
                        if (!LooksLikeImage(file, ext)) continue;
                        result.Add(file);
                    }
                    catch
                    {
                        // unreadable file, skip
                    }
                }
            }
            catch
            {
                // folder partially unreadable, keep what we have
            }

            result.Sort(delegate (string a, string b) { return StrCmpLogicalW(a, b); });
            return result;
        }

        // Scan several folders and merge results, deduped by normalized full path.
        public static List<string> ScanMany(IEnumerable<string> folders, bool recursive)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> merged = new List<string>();
            if (folders == null) return merged;
            foreach (string root in folders)
            {
                List<string> part = Scan(root, recursive);
                foreach (string p in part)
                {
                    string norm;
                    try { norm = Path.GetFullPath(p); }
                    catch { norm = p; }
                    if (seen.Add(norm)) merged.Add(p);
                }
            }
            merged.Sort(delegate (string a, string b) { return StrCmpLogicalW(a, b); });
            return merged;
        }

        // Cheap magic-byte check to drop corrupted / fake files without decoding.
        private static bool LooksLikeImage(string file, string ext)
        {
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < 12) return false;
                    byte[] head = new byte[12];
                    int read = fs.Read(head, 0, head.Length);
                    if (read < 4) return false;

                    if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jfif", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jpe", StringComparison.OrdinalIgnoreCase))
                        return head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF;

                    if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                        return head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47;

                    if (ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                        return head[0] == 0x42 && head[1] == 0x4D;

                    if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                        return head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38;

                    if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                        return head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46;

                    if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                        return (head[0] == 0x49 && head[1] == 0x49 && head[2] == 0x2A && head[3] == 0x00) ||
                               (head[0] == 0x4D && head[1] == 0x4D && head[2] == 0x00 && head[3] == 0x2A);

                    return true; // unknown but whitelisted extension: trust it
                }
            }
            catch
            {
                return false;
            }
        }

        // Fisher-Yates shuffle.
        public static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
