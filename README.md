# WallpaperChanger

> 🌐 **简体中文** | 中文用户请阅读：[**中文版 README**](README.zh-CN.md) (Chinese version)

A lightweight Windows desktop wallpaper rotation tool that lives in the system tray. Pick one or more folders as your wallpaper source, choose a fit style and an interval, and let it cycle — or flip wallpapers on demand with global hotkeys.

Built with C# / .NET 8 (WinForms), no runtime dependencies. Designed for Windows 8 and later.

## Features

- **Multi-folder sources** — add several folders; images from all of them are merged into one rotation pool
- **6 fit styles** — Fill, Fit, Stretch, Tile, Center, Span (multi-monitor span supported)
- **8 switch intervals** — 1 / 5 / 10 / 30 minutes, 1 / 6 / 12 / 24 hours
- **Random or sequential order**
- **Multi-monitor** — the same wallpaper is applied to all screens
- **Global hotkeys** (main keyboard **and** numpad):
  - `Ctrl+9` — next wallpaper
  - `Ctrl+8` — previous wallpaper (steps back through the history of this session)
- **Tray resident** — right-click the tray icon for pause / next / previous / exit; closing the window just minimizes to tray
- **Auto start with Windows** (optional)
- **Supported formats** — jpg / png / jfif / bmp / webp / gif / tiff; `Thumbs.db` and corrupt images are skipped automatically
- Chinese settings UI, explicit **Save settings** button

## Install

Grab the latest installer from the [Releases](../../releases) page (`WallpaperChanger-Setup.exe`, Chinese wizard, works on Windows 10/11 x64). Re-running the installer upgrades in place and keeps your existing config.

No .NET runtime needed — the app is self-contained.

## Usage

1. Launch WallpaperChanger (starts minimized to the tray by default).
2. Open the settings window and add wallpaper folders with the **Add...** button.
3. Pick a style and an interval, then click **Save settings**.

New images added to a source folder are picked up automatically on the next switch — every rotation re-scans the folders, so you never have to restart the app.

Config and logs live next to the exe when the directory is writable, otherwise in `%LocalAppData%\WallpaperChanger\`.

## Build from source

Requires the .NET 8 SDK:

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o dist_standalone
```

The installer is produced with Inno Setup (`installer/WallpaperChanger.iss`).

## License

MIT
