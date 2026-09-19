using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperChanger
{
    // Set the same wallpaper on every monitor via IDesktopWallpaper (Win8+),
    // supporting all six positions including Span. Falls back to
    // SystemParametersInfo if the COM interface is unavailable.
    public static class WallpaperEngine
    {
        // ---- IDesktopWallpaper COM ----
        [ComImport]
        [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,
                              [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            void GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorID);
            void GetMonitorDevicePathCount(out uint count);
            void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            // DESKTOP_WALLPAPER_POSITION. Declared as int, not as an enum: the
            // enum overload threw E_INVALIDARG (0x80070057) from the marshaler
            // before the call reached the shell, which sent every Apply down
            // the slow SystemParametersInfo fallback.
            void SetPosition([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int position);
            DWP GetPosition([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            void SetSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, IntPtr items, DSS direction);
            void GetSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out IntPtr items, out DSS direction);
            void SetSlideshowOptions(DSSO options, uint slideshowTick);
            void GetSlideshowOptions(out DSSO options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, DSS direction);
            DSS GetStatus();
            void Enable(bool enable);
        }

        private enum DWP { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }
        private enum DSS { Forward = 0, Backward = 1 }
        private enum DSSO { None = 0, ShuffleImages = 0x01 }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // DesktopWallpaper coclass
        private static readonly Guid CLSID_DesktopWallpaper = new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

        // ---- user32 ----
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam,
            string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        private const uint SPI_SETDESKWALLPAPER = 20;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        public static bool Apply(string imagePath, WallpaperStyle style)
        {
            if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath)) return false;

            // Phase timings, or null when nobody is measuring. Set by the
            // /flowtest diagnostic; a null check per phase costs nothing.
            List<string> trace = null;
            System.Diagnostics.Stopwatch sw = null;
            if (MeasureTrace != null)
            {
                trace = new List<string>();
                sw = System.Diagnostics.Stopwatch.StartNew();
            }

            try
            {
                IDesktopWallpaper dw = CreateDesktopWallpaper();
                if (dw == null)
                {
                    FallbackApply(imagePath, style);
                    Mark(trace, sw, "fallback");
                    Finish(trace);
                    return true;
                }
                Mark(trace, sw, "com");

                // True once a monitor has actually been given the wallpaper. A
                // position that the shell refuses (E_INVALIDARG on some
                // configurations) must NOT send us down the fallback path: the
                // picture is already on screen, and re-applying it through
                // SystemParametersInfo costs hundreds of milliseconds for
                // nothing. The style is persisted in the registry separately.
                bool anySet = false;

                if (style == WallpaperStyle.Span)
                {
                    // empty monitor id = whole virtual desktop (span across monitors)
                    dw.SetWallpaper("", imagePath);
                    anySet = true;
                    Mark(trace, sw, "set");
                    TrySetPosition(dw, "", ToDWP(style), trace, sw, "sp");
                }
                else
                {
                    uint count;
                    dw.GetMonitorDevicePathCount(out count);
                    Mark(trace, sw, "cnt" + count);
                    if (count == 0)
                    {
                        FallbackApply(imagePath, style, trace, sw);
                    }
                    else
                    {
                        for (uint i = 0; i < count; i++)
                        {
                            IntPtr idPtr;
                            dw.GetMonitorDevicePathAt(i, out idPtr);
                            string id = Marshal.PtrToStringUni(idPtr);
                            Marshal.FreeCoTaskMem(idPtr);
                            dw.SetWallpaper(id, imagePath);
                            anySet = true;
                            Mark(trace, sw, "set" + i);
                            TrySetPosition(dw, id, ToDWP(style), trace, sw, "pos" + i);
                        }
                    }
                }

                PersistStyleRegistry(style);
                Mark(trace, sw, "reg");
                NotifyShell();
                Mark(trace, sw, "notify");
                Finish(trace);
                return true;
            }
            catch (Exception ex)
            {
                // The COM route failing is not fatal - the legacy call still
                // sets the wallpaper - but it was completely silent, which hid
                // the fact that the fast path never ran. Record everything that
                // identifies it: type, HRESULT, message, first frames.
                Mark(trace, sw, "EX:" + ex.GetType().Name + "/0x"
                    + Marshal.GetHRForException(ex).ToString("X8") + "/" + ex.Message);
                try
                {
                    Mark(trace, sw, "st:" + (ex.StackTrace ?? "").Replace("\r", " ").Replace("\n", " ").Substring(0,
                        Math.Min(160, (ex.StackTrace ?? "").Length)));
                }
                catch { }
                try
                {
                    FallbackApply(imagePath, style, trace, sw);
                    Mark(trace, sw, "fallback");
                    Finish(trace);
                    return true;
                }
                catch (Exception ex2)
                {
                    Mark(trace, sw, "EX2:" + ex2.GetType().Name);
                    Finish(trace);
                    return false;
                }
            }
        }

        // A diagnostic hook: when set, Apply() records how long each phase took
        // and hands the finished list to this action. Nothing in the product
        // sets it, so the normal path is one null check per phase.
        public static Action<List<string>> MeasureTrace;

        private static void Mark(List<string> trace, System.Diagnostics.Stopwatch sw, string phase)
        {
            if (trace == null) return;
            trace.Add(phase + "=" + sw.ElapsedMilliseconds);
        }

        private static void Finish(List<string> trace)
        {
            if (trace == null) return;
            Action<List<string>> h = MeasureTrace;
            MeasureTrace = null;
            if (h != null) h(trace);
        }

        // SetPosition is the one call the shell refuses on some setups
        // (E_INVALIDARG from the marshaler, seen on a three-monitor desktop
        // where SetWallpaper on the same id succeeds). The wallpaper is applied
        // either way and the style is written to the registry, so a refusal
        // here is recorded and stepped over - it must never cost a second
        // full application of the image.
        private static void TrySetPosition(IDesktopWallpaper dw, string monitorId, DWP position,
            List<string> trace, System.Diagnostics.Stopwatch sw, string tag)
        {
            try
            {
                dw.SetPosition(monitorId, (int)position);
                Mark(trace, sw, tag);
            }
            catch (Exception ex)
            {
                Mark(trace, sw, tag + "!failed:" + ex.GetType().Name + "/0x"
                    + Marshal.GetHRForException(ex).ToString("X8"));
            }
        }

        private static IDesktopWallpaper CreateDesktopWallpaper()
        {
            Type t = Type.GetTypeFromCLSID(CLSID_DesktopWallpaper);
            if (t == null) return null;
            return (IDesktopWallpaper)Activator.CreateInstance(t);
        }

        private static DWP ToDWP(WallpaperStyle s)
        {
            switch (s)
            {
                case WallpaperStyle.Fit: return DWP.Fit;
                case WallpaperStyle.Stretch: return DWP.Stretch;
                case WallpaperStyle.Tile: return DWP.Tile;
                case WallpaperStyle.Center: return DWP.Center;
                case WallpaperStyle.Span: return DWP.Span;
                default: return DWP.Fill;
            }
        }

        // Legacy path: SystemParametersInfo sets the wallpaper on every monitor.
        private static void FallbackApply(string imagePath, WallpaperStyle style)
        {
            FallbackApply(imagePath, style, null, null);
        }

        private static void FallbackApply(string imagePath, WallpaperStyle style,
            List<string> trace, System.Diagnostics.Stopwatch sw)
        {
            PersistStyleRegistry(style);
            Mark(trace, sw, "reg");
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            Mark(trace, sw, "spi");
            NotifyShell();
            Mark(trace, sw, "notify");
        }

        // Read the current wallpaper path on every monitor (raw file path,
        // not the transcoded cache). Empty list on failure.
        public static List<string> GetCurrentWallpaperPaths()
        {
            List<string> result = new List<string>();
            try
            {
                IDesktopWallpaper dw = CreateDesktopWallpaper();
                if (dw == null) return result;

                uint count;
                dw.GetMonitorDevicePathCount(out count);
                if (count == 0)
                {
                    string p = dw.GetWallpaper("");
                    if (!string.IsNullOrEmpty(p)) result.Add(p);
                }
                else
                {
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr idPtr;
                        dw.GetMonitorDevicePathAt(i, out idPtr);
                        string id = Marshal.PtrToStringUni(idPtr);
                        Marshal.FreeCoTaskMem(idPtr);
                        if (string.IsNullOrEmpty(id)) continue;
                        string p = dw.GetWallpaper(id);
                        if (!string.IsNullOrEmpty(p)) result.Add(p);
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        // Persist the style so it survives a reboot.
        private static void PersistStyleRegistry(WallpaperStyle style)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                if (key == null) return;
                int wallpaperStyle;
                int tile;
                switch (style)
                {
                    case WallpaperStyle.Fit: wallpaperStyle = 6; tile = 0; break;
                    case WallpaperStyle.Stretch: wallpaperStyle = 22; tile = 0; break;
                    case WallpaperStyle.Tile: wallpaperStyle = 0; tile = 1; break;
                    case WallpaperStyle.Center: wallpaperStyle = 0; tile = 0; break;
                    case WallpaperStyle.Span: wallpaperStyle = 22; tile = 0; break;
                    default: wallpaperStyle = 10; tile = 0; break; // Fill
                }
                key.SetValue("WallpaperStyle", wallpaperStyle.ToString(), RegistryValueKind.String);
                key.SetValue("TileWallpaper", tile.ToString(), RegistryValueKind.String);
                key.Close();
            }
            catch
            {
            }
        }

        // Tell explorer to refresh the desktop without re-applying the image.
        //
        // Never on the caller's thread. SendMessageTimeout broadcasts to every
        // top-level window and waits for each one, so one busy window costs the
        // whole timeout - measured 2.5 s on a three-monitor desktop with a
        // browser open. The wallpaper is already on screen by that point; this
        // is only a nudge, so it goes to a background thread and its result is
        // nobody's business.
        private static void NotifyShell()
        {
            try
            {
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        UIntPtr result;
                        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero,
                            "TraySettings", SMTO_ABORTIFHUNG, 500, out result);
                    }
                    catch { }
                });
            }
            catch
            {
            }
        }
    }
}

