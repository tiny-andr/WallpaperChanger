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

        static ThumbCache()
        {
            try { Directory.CreateDirectory(CacheDir); } catch { }
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
                    thumb.Save(cp, ImageFormat.Png);
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
                    if (src == null || src.Width < 8 || src.Height < 8) return null;
                    return CoverCrop(src, w, h);
                }
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
            catch { }

            try
            {
                BitmapDecoder dec = BitmapDecoder.Create(new Uri(path),
                    BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.OnLoad);
                if (dec == null || dec.Frames.Count == 0) return null;
                BitmapSource frame = dec.Frames[0];
                if (frame == null || frame.PixelWidth < 8 || frame.PixelHeight < 8) return null;
                FormatConvertedBitmap bgra = new FormatConvertedBitmap(frame,
                    System.Windows.Media.PixelFormats.Bgra32, null, 0);
                Bitmap bmp = new Bitmap(bgra.PixelWidth, bgra.PixelHeight,
                    PixelFormat.Format32bppArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, bgra.PixelWidth, bgra.PixelHeight),
                        data.Scan0, data.Stride * bgra.PixelHeight, data.Stride);
                }
                finally { bmp.UnlockBits(data); }
                return bmp;
            }
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
