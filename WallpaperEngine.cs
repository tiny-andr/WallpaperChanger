using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperChanger
{
    // Sets the wallpaper on every monitor.
    //
    // SystemParametersInfo(SPI_SETDESKWALLPAPER) is the path that actually
    // repaints the desktop, so it is tried FIRST. IDesktopWallpaper is used only
    // where the legacy call cannot express the request (Span across monitors) or
    // when it fails.
    //
    // That ordering was learned the hard way. IDesktopWallpaper.SetWallpaper
    // returns S_OK on this machine and the desktop keeps the old picture: the
    // measured proof is the transcoded wallpaper file, whose timestamp moved
    // while its hash stayed identical, i.e. nothing was repainted. The log said
    // "applied: <path>" for every one of those calls. Reading the wallpaper back
    // through the same API does not catch it either - it reports the new path
    // while the screen still shows the old image. Only the legacy call changed
    // the hash.
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

            // Snapshot BEFORE the first call. The transcoded file's stamp only
            // proves anything when it is compared against a value taken before
            // the write: the old check asked "was this file written in the last
            // 8 seconds", which any other program touching it satisfies and
            // which says nothing about THIS call.
            DateTime baseline = TranscodeStamp();

            try
            {
                // The legacy call first: it is the one that actually repaints
                // the desktop, and it covers every style except Span.
                if (style != WallpaperStyle.Span)
                {
                    NotifySettingChange();
                    Mark(trace, sw, "notify");
                    if (!PersistStyleRegistry(style)) Mark(trace, sw, "reg:FAILED");
                    Mark(trace, sw, "reg");
                    ApplyViaSystemParameters(imagePath, trace, sw);
                    if (DesktopAdvanced(baseline))
                    {
                        Mark(trace, sw, "spi:ok");
                        Finish(trace);
                        return true;
                    }
                    Mark(trace, sw, "spi:no-change");
                }

                IDesktopWallpaper dw = CreateDesktopWallpaper();
                if (dw == null)
                {
                    // No COM at all: the legacy call is all that ran, so ask the
                    // verifier rather than announcing success unconditionally.
                    FallbackApply(imagePath, style, trace, sw);
                    Mark(trace, sw, "fallback");
                    bool okNoCom = VerifyApplied(imagePath, baseline, trace, sw);
                    Finish(trace);
                    return okNoCom;
                }
                Mark(trace, sw, "com");

                // Announce the change before the COM write; that is what makes
                // the desktop pick it up.
                NotifySettingChange();
                Mark(trace, sw, "notify2");

                if (style == WallpaperStyle.Span)
                {
                    // empty monitor id = whole virtual desktop (span across monitors)
                    dw.SetWallpaper("", imagePath);
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
                        dw.SetWallpaper("", imagePath);
                        Mark(trace, sw, "set");
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
                            Mark(trace, sw, "set" + i);
                            TrySetPosition(dw, id, ToDWP(style), trace, sw, "pos" + i);
                        }
                    }
                }

                if (!PersistStyleRegistry(style)) Mark(trace, sw, "reg2:FAILED");
                Mark(trace, sw, "reg2");

                // Only Span lands here on a healthy machine, so this read-back
                // is the Span path's own sanity check.
                string now = ReadBackWallpaper(dw);
                Mark(trace, sw, "read:" + (string.IsNullOrEmpty(now) ? "<empty>" : System.IO.Path.GetFileName(now)));
                if (!SameFile(now, imagePath))
                {
                    Mark(trace, sw, "MISMATCH -> spi again");
                    FallbackApply(imagePath, style, trace, sw);
                }

                // The read-back being right is not enough on its own (the header
                // comment records a case where the API reported the new path
                // while the screen still showed the old picture), so the desktop
                // itself gets the final word.
                bool ok = VerifyApplied(imagePath, baseline, trace, sw);
                Finish(trace);
                return ok;
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
                    bool okCatch = VerifyApplied(imagePath, baseline, trace, sw);
                    Finish(trace);
                    return okCatch;
                }
                catch (Exception ex2)
                {
                    Mark(trace, sw, "EX2:" + ex2.GetType().Name);
                    Finish(trace);
                    return false;
                }
            }
        }

        // True when the desktop really shows this image. This is the only place
        // allowed to answer "did the apply work": it combines the two honest
        // signals available from inside the process (the shell's own read-back
        // of the path, and the transcoded file the shell rewrites on a genuine
        // repaint) with the baseline captured before the write.
        private static bool VerifyApplied(string imagePath, DateTime baseline,
            List<string> trace, System.Diagnostics.Stopwatch sw)
        {
            if (DesktopAdvanced(baseline))
            {
                Mark(trace, sw, "verify:transcoded");
                return true;
            }

            IDesktopWallpaper dw = CreateDesktopWallpaper();
            if (dw != null)
            {
                try
                {
                    string shown = ReadBackWallpaper(dw);
                    Mark(trace, sw, "verify:readback=" +
                        (string.IsNullOrEmpty(shown) ? "<empty>" : System.IO.Path.GetFileName(shown)));
                    if (SameFile(shown, imagePath))
                    {
                        Mark(trace, sw, "verify:path");
                        return true;
                    }
                }
                catch { }
                // A per-monitor read-back only covers monitor 0; on a multi-monitor
                // Span setup it can disagree while the desktop is in fact correct.
                try
                {
                    if (AllMonitorsShow(dw, imagePath))
                    {
                        Mark(trace, sw, "verify:allmonitors");
                        return true;
                    }
                }
                catch { }
            }

            Mark(trace, sw, "verify:failed");
            return false;
        }

        private static bool AllMonitorsShow(IDesktopWallpaper dw, string imagePath)
        {
            uint count;
            dw.GetMonitorDevicePathCount(out count);
            if (count == 0) return SameFile(dw.GetWallpaper(""), imagePath);
            for (uint i = 0; i < count; i++)
            {
                IntPtr idPtr;
                dw.GetMonitorDevicePathAt(i, out idPtr);
                string id = Marshal.PtrToStringUni(idPtr);
                Marshal.FreeCoTaskMem(idPtr);
                if (string.IsNullOrEmpty(id)) continue;
                if (!SameFile(dw.GetWallpaper(id), imagePath)) return false;
            }
            return true;
        }

        // A diagnostic hook: when set, Apply() records how long each phase took
        // and hands the finished list to this action. Nothing in the product
        // sets it, so the normal path is one null check per phase.
        public static Action<List<string>> MeasureTrace;

        // What the shell says is on the desktop right now (first monitor).
        private static string ReadBackWallpaper(IDesktopWallpaper dw)
        {
            try
            {
                uint count;
                dw.GetMonitorDevicePathCount(out count);
                if (count == 0) return dw.GetWallpaper("");
                IntPtr idPtr;
                dw.GetMonitorDevicePathAt(0, out idPtr);
                string id = Marshal.PtrToStringUni(idPtr);
                Marshal.FreeCoTaskMem(idPtr);
                return dw.GetWallpaper(id);
            }
            catch
            {
                return null;
            }
        }

        private static bool SameFile(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

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

        // The legacy path, timed when a trace is running.
        private static void ApplyViaSystemParameters(string imagePath, List<string> trace,
            System.Diagnostics.Stopwatch sw)
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            Mark(trace, sw, "spi");
        }

        // Legacy path: SystemParametersInfo sets the wallpaper on every monitor.
        private static void FallbackApply(string imagePath, WallpaperStyle style)
        {
            FallbackApply(imagePath, style, null, null);
        }

        private static void FallbackApply(string imagePath, WallpaperStyle style,
            List<string> trace, System.Diagnostics.Stopwatch sw)
        {
            if (!PersistStyleRegistry(style)) Mark(trace, sw, "reg:FAILED");
            Mark(trace, sw, "reg");
            ApplyViaSystemParameters(imagePath, trace, sw);
            NotifyShell();
            Mark(trace, sw, "notify");
        }

        // What the DESKTOP says, not what the API says: the transcoded file the
        // shell writes when the wallpaper really changes. A SetWallpaper that
        // returns S_OK without repainting leaves this file's content alone, so
        // its timestamp is the only honest check available from inside the
        // process.
        //
        // The comparison is against a baseline taken BEFORE the write. The
        // earlier version of this check compared the stamp against "now minus 8
        // seconds", which is true for anything that touched the file recently -
        // including another wallpaper program, or an earlier apply in the same
        // rotation tick - so it reported success for writes that never landed.
        private static DateTime TranscodeStamp()
        {
            try
            {
                string tp = TranscodePath();
                if (tp == null || !System.IO.File.Exists(tp)) return DateTime.MinValue;
                return System.IO.File.GetLastWriteTimeUtc(tp);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        // True when the shell rewrote the transcoded file after the baseline.
        // DateTime.MinValue means "there was no file", and then any existing
        // stamp counts as an advance (the shell created it, which only happens
        // on a genuine repaint).
        private static bool DesktopAdvanced(DateTime baseline)
        {
            try
            {
                string tp = TranscodePath();
                if (tp == null || !System.IO.File.Exists(tp)) return false;
                DateTime stamp = System.IO.File.GetLastWriteTimeUtc(tp);
                return stamp > baseline;
            }
            catch
            {
                return false;
            }
        }

        private static string TranscodePath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
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
        //
        // Returns false when the write did not happen, so the caller can tell
        // "the style is stored" from "we silently failed to store it" (a policy
        // or a security product can lock Control Panel\Desktop). The key is
        // closed on the failure paths too - it used to leak the handle whenever
        // SetValue threw.
        private static bool PersistStyleRegistry(WallpaperStyle style)
        {
            RegistryKey key = null;
            try
            {
                key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                if (key == null) return false;
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
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (key != null) { try { key.Close(); } catch { } }
            }
        }

        // Tell the shell that desktop settings changed.
        //
        // This is NOT the optional nudge it was treated as: IDesktopWallpaper's
        // SetWallpaper updates the stored wallpaper and returns S_OK, but the
        // desktop keeps painting the previous picture until something tells
        // Explorer to reload. Without it the log says "applied: <path>" and the
        // screen shows the old wallpaper - exactly the "the wallpaper does not
        // change" report.
        //
        // Sent on the caller's thread here (unlike the old background nudge)
        // because the change has to land before we read the wallpaper back to
        // check it. HWND_BROADCAST walks every top-level window in turn and
        // waits up to the timeout on EACH one, so the timeout multiplies:
        // measured 1.5-2.8 s per switch at 1000 ms. 200 ms is far longer than a
        // healthy window needs for this message, and a hung one is skipped
        // outright by SMTO_ABORTIFHUNG.
        private static void NotifySettingChange()
        {
            try
            {
                UIntPtr result;
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero,
                    "Desktop", SMTO_ABORTIFHUNG, 200, out result);
            }
            catch
            {
            }
        }

        private static void NotifyShell()
        {
            NotifySettingChange();
        }
    }
}


