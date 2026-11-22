; WallpaperChanger installer script
; Build:  "C:\Users\Administrator\InnoSetup6\ISCC.exe" WallpaperChanger.iss

#define MyAppName "WallpaperChanger"
#define MyAppVersion "2.1.1"
#define MyAppExe "WallpaperChanger.exe"

[Setup]
AppId={{B3F5A8E2-7C4D-4A9E-9B2F-1D5E8C7A0F3B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=local
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=WallpaperChanger-Setup
SetupIconFile=..\icon.ico
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} Installer

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "autostart"; Description: "开机自动启动 {#MyAppName}"; GroupDescription: "附加选项:"; Flags: unchecked
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"
Name: "runnow"; Description: "安装完成后立即启动 {#MyAppName}"; GroupDescription: "附加选项:"

[Files]
Source: "..\dist_standalone\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: autostart
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; No skipifsilent: the in-app updater starts this installer with /VERYSILENT
; and then exits, so the app has to come back by itself afterwards. An
; interactive install still starts it from the finished page (postinstall).
; runasoriginaluser: the installer is elevated, but the app must come back
; with the user's normal token - otherwise it starts as admin, and before
; 2.1.1 that meant it read/created a config next to the exe in Program Files
; instead of the real one in %LocalAppData%.
Filename: "{app}\{#MyAppExe}"; Description: "启动 {#MyAppName}"; Tasks: runnow; Flags: nowait postinstall runasoriginaluser






