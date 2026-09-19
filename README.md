# WallpaperChanger

> 🌐 **简体中文** | 中文用户请阅读：[**中文版 README**](README.zh-CN.md) (Chinese version)

A lightweight Windows desktop wallpaper rotation tool that lives in the system tray. Pick one or more folders as your wallpaper source, choose a fit style and an interval, and let it cycle — or flip wallpapers on demand with global hotkeys.

Built with C# / .NET 8 (WinForms), self-contained, no runtime needed. Designed for Windows 8 and later.

![WallpaperChanger overview](docs/screenshot-overview.png)

## The window

One window, three pages in the left rail:

| Page | What is on it |
|---|---|
| **Overview** | Preview of the current wallpaper with its counts; three drop-downs for fit style / interval / order (edit them right there); a strip of the most recent wallpapers along the bottom |
| **Sources** | The source list (enable switch / name / path / image count / remove), add-folder, and the manual picker |
| **General** | Start with Windows, light / dark theme, interface language, global hotkeys, config and log location |

The big button at the bottom of the rail shows the rotation state (**Rotating** / **Paused**) and toggles it.

## Features

- **Multi-folder sources, individually switchable** — images from all *enabled* sources are merged into one rotation pool. The Sources page lists every source with its wallpaper count (recursive, subfolders included) and lets you add, remove, or turn a single source off without losing it — a disabled source simply stops contributing and comes back with one tick
- **6 fit styles** — Fill, Fit, Stretch, Tile, Center, Span (multi-monitor span supported)
- **8 switch intervals** — 1 / 5 / 10 / 30 minutes, 1 / 6 / 12 / 24 hours. The next switch time is shown in the status bar, with its day attached when the interval crosses midnight ("tomorrow 22:00")
- **Random or sequential order**
- **Multi-monitor** — the same wallpaper is applied to all screens
- **Light / dark theme** — one switch on the General page, remembered immediately; the window and the tray menu follow
- **Global hotkeys** (main keyboard **and** numpad), rebindable by clicking the key box on the General page:
  - `Ctrl+9` — next wallpaper
  - `Ctrl+8` — previous wallpaper (steps back through this session's history; a following `Ctrl+9` returns to the wallpaper you stepped away from)
- **History strip** — the overview keeps the **last 5** wallpapers as thumbnails: pictures only, no file names, with the one on screen ringed in accent. Click a tile to go back to it
- **Manual wallpaper picker** — curate exactly which wallpapers rotate: **Pick wallpapers** on the Sources page opens the 16:9 grid (live file-name filter plus select-all / select-none / invert acting on the filtered set, and a source filter when several sources feed it; reopening starts on the checked view). Checking any wallpaper turns manual mode on; unchecking all of them turns it off. While active, auto rotation and "next" only draw from the checked set. Selections persist across restarts; newly added images default to unchecked
- **Tray resident** — right-click the tray icon for pause / next / previous / manual picker / open / exit; closing the window just minimizes to tray
- **Auto start with Windows** (optional) — boots straight into the tray with no window; launching the app again just brings the existing window to the front
- **Supported formats** — jpg / png / jfif / bmp / webp / gif / tiff; `Thumbs.db` and corrupt images are skipped automatically
- Chinese, English and Japanese interface. Fit style, interval and order are written when you press **Save settings**; language and theme apply and save on the spot
- The window is a fixed 980x668: it neither maximizes nor resizes, because the layout follows the design's fixed coordinates

## Install

Grab the latest installer from the [Releases](../../releases) page (`WallpaperChanger-Setup.exe`, Chinese wizard, works on Windows 10/11 x64). Re-running the installer upgrades in place and keeps your existing config.

No .NET runtime needed — the app is self-contained.

> Current version: **v2.0.0** (interface rebuilt from scratch)

## Usage

1. Launch WallpaperChanger (starts minimized to the tray by default; click the tray icon to bring the window up).
2. On the **Sources** page, press **Add folder** and pick an image folder; each row can be toggled or removed.
3. Back on **Overview**, set fit style, interval and order with the three drop-downs, then press **Save settings**.

New images added to a source folder are picked up on the next switch — every rotation re-scans the folders, so you never have to restart the app.

Config and logs live next to the exe when the directory is writable, otherwise in `%LocalAppData%\WallpaperChanger\`. The path can be opened from **General → Config & logs**.

## Build from source

Requires the .NET 8 SDK:

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o dist_standalone
```

The installer is produced with Inno Setup (`installer/WallpaperChanger.iss`) after that publish.

## License

MIT
