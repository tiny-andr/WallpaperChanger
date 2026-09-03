# WallpaperChanger 壁纸轮换

一款常驻系统托盘的 Windows 桌面壁纸轮换小工具。选择一个或多个文件夹作为壁纸源，设定填充样式和切换间隔，自动轮换——或使用全局快捷键随时切换。

技术栈：C# / .NET 8（WinForms），自包含单文件，无需安装 .NET 运行库。支持 Windows 8 及以上系统（推荐 Windows 10/11 x64）。

## 功能特性

- **多文件夹壁纸源** — 可添加多个文件夹，所有图片合并进同一个轮换池
- **6 种填充样式** — 填充 / 适应 / 拉伸 / 平铺 / 居中 / 跨屏（支持多显示器同图）
- **8 档切换间隔** — 1 / 5 / 10 / 30 分钟，1 / 6 / 12 / 24 小时
- **随机 / 顺序**两种轮换方式
- **多显示器** — 所有屏幕同步应用同一张壁纸
- **全局快捷键**（主键盘**和小键盘**都支持）：
  - `Ctrl+9` — 下一张壁纸
  - `Ctrl+8` — 上一张壁纸（按本次会话的应用历史逐级回退）
- **托盘常驻** — 右键托盘图标可 暂停 / 下一张 / 上一张 / 退出；关闭窗口只是最小化到托盘
- **开机自启**（可选）
- **支持格式** — jpg / png / jfif / bmp / webp / gif / tiff；自动跳过 `Thumbs.db` 和损坏图片
- 中文设置界面，改动后需点击**保存设置**按钮才会写入配置文件

## 下载安装

到 [Releases](../../releases) 页面下载最新安装包（`WallpaperChanger-Setup.exe`，中文安装向导，适用于 Windows 10/11 x64）。重复安装即覆盖升级，已有配置会保留。

应用为自包含发布，**无需安装 .NET 运行库**。

> 当前版本：**v0.0.1**

## 使用说明

1. 启动 WallpaperChanger（默认最小化到托盘）。
2. 打开设置窗口，用 **添加...** 按钮加入壁纸源文件夹（列表只读，不可手打；可逐条删除或一键清空）。
3. 选择填充样式和切换间隔，点击**保存设置**。

**新增壁纸何时生效**：程序不做实时文件监听，而是在每次切换动作（定时轮换 / Ctrl+9 / 下一张按钮）时现场重新扫描源目录——往文件夹里丢入新图后，下一次切换就会把它纳入轮换池；想立即生效就按一下 `Ctrl+9`。`Ctrl+8` 上一张属于纯历史回退，不触发重新扫描。

配置与日志：exe 所在目录可写时写在同目录，否则自动写入 `%LocalAppData%\WallpaperChanger\`（安装到 Program Files 时即此路径）。

更多细节（壁纸源管理 / 保存与后台 / 快捷键 / 支持格式 / 新增壁纸生效逻辑）可在程序内点右下角**帮助**按钮查看。

## 从源码构建

需要 .NET 8 SDK：

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o dist_standalone
```

安装包由 Inno Setup 生成（`installer/WallpaperChanger.iss`）。

## 开源协议

MIT
