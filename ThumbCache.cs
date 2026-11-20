using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace WallpaperChanger
{
    // Persistent on-disk thumbnail cache for the manual picker.
    //
    // The picker must NEVER decode full-resolution originals into memory for
    // every tile (a 4K JPEG needs ~32 MB; 196 of them exceed 6 GB). Instead,
    // this class generates small 16:9 thumbnails once and stores them as PNG
    // files under %LocalAppData%\WallpaperChanger\thumbs. The picker then
    // loads only those tiny PNGs, so opening, scrolling, resizing and
    // maximizing stay fast regardless of source image size.
    internal static class ThumbCache
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperChanger", "thumbs");

        private static readonly HashSet<string> Generating =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The cache had no ceiling: every distinct (path, size, mtime) pair adds
        // a PNG and nothing ever removed one, so it grows for as long as the app
        // is used. Measured 5,509 files / 242 MB after 2.5 months, about 95 MB a
        // month - a few GB over a couple of years, for thumbnails of pictures the
        // user may no longer even have.
        private const long MaxCacheBytes = 400L * 1024 * 1024;
        private const string StampName = ".prune";

        static ThumbCache()
        {
            try { Directory.CreateDirectory(CacheDir); } catch { }
            try { PruneIfDue(); } catch { }
        }

        // Oldest-first eviction down to three quarters of the cap, so this does
        // not run again on the very next launch. Once a week is enough to keep
        // the directory bounded; the stamp file records the last run.
        private static void PruneIfDue()
        {
            string stamp = Path.Combine(CacheDir, StampName);
            try
            {
                if (File.Exists(stamp) &&
                    (DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp)).TotalDays < 7) return;
            }
            catch { }

            try
            {
                DirectoryInfo di = new DirectoryInfo(CacheDir);
                FileInfo[] files = di.GetFiles("*.png");
                long total = 0;
                foreach (FileInfo f in files) total += f.Length;
                if (total > MaxCacheBytes)
                {
                    Array.Sort(files, delegate (FileInfo a, FileInfo b)
                    {
                        return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
                    });
                    long target = (long)(MaxCacheBytes * 0.75);
                    foreach (FileInfo f in files)
                    {
                        if (total <= target) break;
                        try
                        {
                            long len = f.Length;
                            f.Delete();
                            total -= len;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try { File.WriteAllText(stamp, DateTime.UtcNow.ToString("o")); } catch { }
        }

        // Returns the cached thumbnail if it exists and has the expected size.
        public static Bitmap Get(string sourcePath, int w, int h)
        {
            string cp = CachePath(sourcePath, w, h);
            if (!File.Exists(cp)) return null;
            try
            {
                Bitmap bmp = new Bitmap(cp);
                if (bmp.Width == w && bmp.Height == h) return bmp;
                bmp.Dispose();
            }
            catch { }
            // The cache file is unreadable or the wrong size: drop it so the
            // next Generate() can rebuild instead of being skipped by the
            // File.Exists shortcut. Without this, one truncated PNG (killed
            // mid-write by an older build) poisoned that tile permanently.
            try { File.Delete(cp); } catch { }
            return null;
        }

        // Generates the thumbnail synchronously on the calling thread. The
        // caller is expected to run this on a background thread.
        public static void Generate(string sourcePath, int w, int h)
        {
            string cp = CachePath(sourcePath, w, h);
            lock (Generating)
            {
                if (Generating.Contains(cp) || File.Exists(cp)) return;
                Generating.Add(cp);
            }
            try
            {
                Bitmap thumb = DecodeAndScale(sourcePath, w, h);
                if (thumb == null) return;
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    // Save to a side file and move it into place. Saving straight
                    // to cp meant a process killed mid-write left a truncated
                    // PNG that still *exists*, and File.Exists is what Get() and
                    // this method treat as "done" - so that tile stayed blank
                    // forever (Get fails to decode it, Generate refuses to
                    // rebuild it) until the source file's mtime changed.
                    string temp = cp + ".tmp";
                    thumb.Save(temp, ImageFormat.Png);
                    try
                    {
                        File.Move(temp, cp);   // atomic on the same volume
                    }
                    catch
                    {
                        try { File.Copy(temp, cp, true); } catch { }
                        try { File.Delete(temp); } catch { }
                    }
                }
                catch { }
                finally { thumb.Dispose(); }
            }
            finally
            {
                lock (Generating) { Generating.Remove(cp); }
            }
        }

        private static string CachePath(string sourcePath, int w, int h)
        {
            string key = Key(sourcePath, w, h);
            return Path.Combine(CacheDir, key + ".png");
        }

        private static string Key(string sourcePath, int w, int h)
        {
            long len = 0;
            long ticks = 0;
            try
            {
                FileInfo fi = new FileInfo(sourcePath);
                len = fi.Length;
                ticks = fi.LastWriteTimeUtc.Ticks;
            }
            catch { }
            string seed = sourcePath + "|" + len + "|" + ticks + "|" + w + "x" + h;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                StringBuilder sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static Bitmap DecodeAndScale(string path, int w, int h)
        {
            if (w < 8 || h < 8) return null;
            try
            {
                using (Image src = DecodeAny(path))
                {
                    if (src != null && src.Width >= 8 && src.Height >= 8)
                    {
                        return CoverCrop(src, w, h);
                    }
                }
            }
            catch { }

            // GDI+ could not open it. Decode it through WIC at a reduced size
            // when the aspect allows, and only then crop.
            try
            {
                Bitmap scaled = DecodeAnyAtScale(path, w, h);
                if (scaled == null) return null;
                if (ScaleFits(scaled, w, h))
                {
                    // CoverCrop returns its own bitmap, so the decoded one is
                    // ours to dispose - but only after the copy exists.
                    try { return CoverCrop(scaled, w, h); }
                    finally { scaled.Dispose(); }
                }
                // Wrong aspect for a crop-free downscale: hand back what WIC
                // gave us rather than nothing.
                return scaled;
            }
            catch { return null; }
        }

        private static Bitmap CoverCrop(Image src, int w, int h)
        {
            float scale = Math.Max((float)w / src.Width, (float)h / src.Height);
            float cropW = Math.Min(src.Width, w / scale);
            float cropH = Math.Min(src.Height, h / scale);
            float sx = (src.Width - cropW) / 2f;
            float sy = (src.Height - cropH) / 2f;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(src, new RectangleF(0, 0, w, h),
                    new RectangleF(sx, sy, cropW, cropH), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        private static Image DecodeAny(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (Image tmp = Image.FromStream(fs))
                {
                    if (tmp.Width < 8 || tmp.Height < 8) return null;
                    return CopyToArgb(tmp);
                }
            }
            catch { return null; }
        }

        // ---- the WIC route, decoded at a reduced size ----------------------
        //
        // GDI+ cannot open every format in a wallpaper folder (WebP on older
        // builds, some very large PNGs), so those go through WIC. The catch was
        // that BitmapDecoder decoded them at FULL resolution and the copy then
        // scaled that down: a 14 MB 4K PNG took seconds, and the preview card
        // sat empty while its thumbnail was generated.
        //
        // BitmapImage with DecodePixelWidth lets WIC do the downscale while it
        // decodes, which is the whole point of the API.

        // True when the caller asked for a size that DecodeAnyAtScale can honour.
        // The scaled route needs the target's aspect to match the thumbnail's,
        // because a decode-time downscale cannot crop.
        private static bool ScaleFits(Image src, int w, int h)
        {
            if (src == null || w < 8 || h < 8) return false;
            double want = (double)w / h;
            double got = (double)src.Width / src.Height;
            return Math.Abs(want - got) <= 0.02;
        }

        private static Bitmap DecodeAnyAtScale(string path, int w, int h)
        {
            // WIC downscales to the requested width while decoding; a little
            // headroom keeps the final high-quality crop honest.
            Bitmap result = DecodeWicScaled(path, w * 2, h * 2);
            if (result != null) return result;
            return ToBitmap(DecodeAny(path));
        }

        private static Bitmap DecodeWicScaled(string path, int maxW, int maxH)
        {
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = Math.Max(8, maxW);
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                if (bi.PixelWidth < 8 || bi.PixelHeight < 8) return null;
                return ToBitmap(bi);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap ToBitmap(BitmapSource frame)
        {
            if (frame == null) return null;
            try
            {
                FormatConvertedBitmap bgra = frame.Format == System.Windows.Media.PixelFormats.Bgra32
                    ? null
                    : new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                BitmapSource src = bgra ?? (BitmapSource)frame;
                Bitmap bmp = new Bitmap(src.PixelWidth, src.PixelHeight, PixelFormat.Format32bppArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    src.CopyPixels(new System.Windows.Int32Rect(0, 0, src.PixelWidth, src.PixelHeight),
                        data.Scan0, data.Stride * src.PixelHeight, data.Stride);
                }
                finally { bmp.UnlockBits(data); }
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap ToBitmap(Image img)
        {
            if (img == null) return null;
            try { return CopyToArgb(img); }
            catch { return null; }
        }

        private static Bitmap CopyToArgb(Image img)
        {
            Bitmap copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(copy))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(img, 0, 0, img.Width, img.Height);
            }
            return copy;
        }
    }
}
