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
            void SetPosition([MarshalAs(UnmanagedType.LPWStr)] string monitorID, DWP position);
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

            try
            {
                IDesktopWallpaper dw = CreateDesktopWallpaper();
                if (dw == null)
                {
                    FallbackApply(imagePath, style);
                    return true;
                }

                if (style == WallpaperStyle.Span)
                {
                    // empty monitor id = whole virtual desktop (span across monitors)
                    dw.SetWallpaper("", imagePath);
                    dw.SetPosition("", DWP.Span);
                }
                else
                {
                    uint count;
                    dw.GetMonitorDevicePathCount(out count);
                    if (count == 0)
                    {
                        FallbackApply(imagePath, style);
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
                            dw.SetPosition(id, ToDWP(style));
                        }
                    }
                }

                PersistStyleRegistry(style);
                NotifyShell();
                return true;
            }
            catch
            {
                try
                {
                    FallbackApply(imagePath, style);
                    return true;
                }
                catch
                {
                    return false;
                }
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
            PersistStyleRegistry(style);
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
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
        private static void NotifyShell()
        {
            try
            {
                UIntPtr result;
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero,
                    "TraySettings", SMTO_ABORTIFHUNG, 1000, out result);
            }
            catch
            {
            }
        }
    }
}
