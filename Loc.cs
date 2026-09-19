using System;
using System.Collections.Generic;
using System.Threading;

namespace WallpaperChanger
{
    // Lightweight in-app localization for zh-CN / en / ja.
    // The GitHub-facing docs (README, release notes) stay bilingual EN/ZH;
    // only the application UI offers the three languages.
    //
    // Usage: Loc.Init() once after Config.Load(), then Loc.T("key") /
    // Loc.F("key", args) at any place a user-visible string is produced.
    // Missing keys fall back to English, then to the raw key so that a
    // forgotten translation is loud but never crashes the UI.
    public static class Loc
    {
        public const string Zh = "zh";
        public const string En = "en";
        public const string Ja = "ja";

        // Native names shown in the language combo; intentionally NOT
        // translated (each language refers to itself by its own name).
        public static readonly string[] LanguageDisplayNames = { "中文", "English", "日本語" };
        public static readonly string[] LanguageCodes = { Zh, En, Ja };

        private static readonly Dictionary<string, string> ZhMap = BuildZh();
        private static readonly Dictionary<string, string> EnMap = BuildEn();
        private static readonly Dictionary<string, string> JaMap = BuildJa();

        private static Dictionary<string, string> current = ZhMap;

        public static string Language { get; private set; } = Zh;

        // Read the persisted language (Config must be loaded); fall back to
        // OS UI language detection when unset or invalid.
        public static void Init()
        {
            string lang = Config.Language;
            if (lang != Zh && lang != En && lang != Ja) lang = Detect();
            SetLanguage(lang);
        }

        private static string Detect()
        {
            try
            {
                string two = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
                if (two == "zh") return Zh;
                if (two == "ja") return Ja;
            }
            catch
            {
            }
            return En;
        }

        public static void SetLanguage(string lang)
        {
            if (lang != Zh && lang != En && lang != Ja) lang = Detect();
            Language = lang;
            current = (lang == En) ? EnMap : (lang == Ja) ? JaMap : ZhMap;
        }

        public static string T(string key)
        {
            string v;
            if (current != null && current.TryGetValue(key, out v) && v != null) return v;
            if (EnMap.TryGetValue(key, out v) && v != null) return v;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        // ---- wallpaper style combo labels (index-locked to the enum) ----
        public static string[] StyleNames()
        {
            if (Language == En)
                return new string[] { "Fill (default)", "Fit", "Stretch", "Tile", "Center", "Span" };
            if (Language == Ja)
                return new string[] { "塗りつぶし（既定）", "合わせる", "引き伸ばし", "並べて表示", "中央に表示", "またぐ" };
            return new string[] { "填充（默认）", "适应", "拉伸", "平铺", "居中", "跨区" };
        }

        // ---- rotation interval combo labels (index-locked to minute values) ----
        public static string[] IntervalNames()
        {
            if (Language == En)
                return new string[] { "1 minute", "5 minutes", "10 minutes", "30 minutes", "1 hour", "6 hours", "12 hours", "24 hours" };
            if (Language == Ja)
                return new string[] { "1分", "5分", "10分", "30分", "1時間", "6時間", "12時間", "24時間" };
            return new string[] { "1 分钟", "5 分钟", "10 分钟", "30 分钟", "1 小时", "6 小时", "12 小时", "24 小时" };
        }

        // ================= Chinese =================
        private static Dictionary<string, string> BuildZh()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["tray.tip"] = "WallpaperChanger - 壁纸轮换";

            m["main.source.group"] = "壁纸源";
            m["main.source.add"] = "添加...";
            m["main.source.remove"] = "删除选中";
            m["main.source.manage"] = "壁纸源管理";
            m["main.source.summary.none"] = "尚未添加壁纸源，点击上方按钮添加";
            m["main.source.summary"] = "共 {0} 个源 · 启用 {1} · 禁用 {2}";
            m["main.source.all.on"] = "所有壁纸源均已启用";
            m["main.manual.btn"] = "手动壁纸选择：已关闭";
            m["main.manual.btn.on"] = "手动壁纸选择：已选择 {0} 张壁纸";
            m["main.settings.group"] = "轮换设置";
            m["main.other.group"] = "其他设置";
            m["main.settings.style"] = "壁纸样式:";
            m["main.settings.interval"] = "切换频率:";
            m["main.settings.random"] = "随机图片顺序";
            m["main.settings.next"] = "下一张:";
            m["main.settings.prev"] = "上一张:";
            m["main.settings.autostart"] = "开机自动启动（启动文件夹快捷方式）";
            m["main.settings.language"] = "语言/Language:";
            m["main.btn.next"] = "下一张壁纸";
            m["main.btn.prev"] = "上一张壁纸";
            m["main.btn.save"] = "保存设置";
            m["main.btn.help"] = "帮助";
            m["main.theme.tip"] = "切换主题";
            m["main.hotkey.none"] = "无快捷键";

            m["status.saved"] = "设置已保存";
            m["hotkey.err.same"] = "上一张快捷键不能与下一张相同（都是 Ctrl+{0}）";
            m["hotkey.err.next"] = "下一张快捷键 Ctrl+{0} 注册失败（可能已被其他程序占用）";
            m["hotkey.err.prev"] = "上一张快捷键 Ctrl+{0} 注册失败（可能已被其他程序占用）";
            m["hotkey.err.join"] = "；";
            m["status.folder.dup"] = "该文件夹已经在壁纸源列表里";
            m["status.folder.added"] = "已添加壁纸源：{0}（点保存生效）";
            m["status.folder.pickfirst"] = "请先在列表中选中要删除的壁纸源";
            m["status.folder.empty"] = "壁纸源列表已经是空的";
            m["dialog.pickfolder"] = "选择一个壁纸图片文件夹";
            m["status.novalidfolder"] = "请先添加至少一个有效的图片文件夹";
            m["status.current"] = "当前壁纸: {0}（共 {1} 张）";
            m["status.current.prev"] = "当前壁纸: {0}（上一张）";
            m["status.manual.emptypool"] = "手动模式已开启，但勾选集合里没有可用图片（请打开手动壁纸选择勾选）";
            m["status.nopictures"] = "所有文件夹里都没有可用图片";
            m["status.applyfail"] = "壁纸设置失败: {0}";
            m["status.error"] = "出错: {0}";
            m["status.noprev"] = "没有更早的壁纸了（这是本次启动后的第一张）";
            m["status.prevtag"] = "（上一张）";
            m["status.prevfail"] = "壁纸回退失败: {0}";
            m["status.nextswitch"] = "下次切换: {0}";
            m["status.paused"] = "轮换已暂停";
            m["status.rotate.paused"] = "已暂停轮换";
            m["status.rotate.resumed"] = "轮换已恢复";
            m["mode.tag"] = "　[手动模式 · {0} 张]";

            m["tray.next"] = "下一张壁纸";
            m["tray.prev"] = "上一张壁纸";
            m["tray.pause"] = "暂停轮换";
            m["tray.resume"] = "继续轮换";
            m["tray.manual"] = "手动壁纸选择…";
            m["tray.open"] = "打开设置";
            m["tray.exit"] = "退出";
            m["tray.exit.confirm"] = "有未保存的设置更改，退出前要保存吗？";
            m["balloon.stillrunning"] = "程序仍在后台运行，右键托盘图标可暂停 / 退出";
            m["balloon.manual.on"] = "已启用手动壁纸选择（勾选 {0} 张参与切换）";
            m["balloon.manual.off"] = "已关闭手动壁纸选择，恢复全部壁纸切换";

            m["picker.title"] = "手动壁纸选择 - WallpaperChanger v{0}";
            m["picker.all"] = "全选";
            m["picker.none"] = "全不选";
            m["picker.invert"] = "反选";
            m["picker.filter.hint"] = "筛选文件名（不区分大小写）…";
            m["picker.count"] = "已选 {0} / 共 {1}";
            m["picker.save"] = "保存";
            m["picker.saveclose"] = "保存并关闭";
            m["picker.disableall"] = "彻底关闭手动选择壁纸";
            m["picker.disableall.confirm"] = "手动壁纸选择功能已关闭，回到全部壁纸轮换模式，此窗口将关闭。";
            m["picker.close"] = "关闭";
            m["picker.scanning"] = "正在扫描图片…";
            m["picker.nofolders"] = "没有找到可用壁纸，请先在主窗口的壁纸源里添加图片文件夹";
            m["picker.nofiltermatch"] = "没有匹配的文件名";
            m["picker.saved.on"] = "已保存：手动壁纸选择已启用，切换范围为已勾选的 {0} 张壁纸";
            m["picker.saved.off"] = "已保存：没有勾选壁纸，手动壁纸选择已关闭（恢复全部壁纸切换）";
            m["picker.confirm.close"] = "有未保存的勾选更改，关闭前要保存吗？";
            m["picker.bottom.hint"] = "未勾选的壁纸不参与自动 / 手动切换；随机顺序开关不受影响，仍在勾选池内打乱";
            m["picker.src.label"] = "壁纸源:";
            m["picker.src.all"] = "全部壁纸";
            m["picker.src.picked"] = "已选中的壁纸 ({0})";
            m["picker.src.one"] = "{0}（{1} 张）";
            m["picker.nosrcpick"] = "还没有勾选任何壁纸，切换到\"全部壁纸\"开始挑选";
            m["picker.caption"] = "手动壁纸选择";

            m["src.title"] = "壁纸源管理 - WallpaperChanger v{0}";
            m["src.caption"] = "壁纸源管理";
            m["src.col.state"] = "状态";
            m["src.col.name"] = "名称";
            m["src.col.path"] = "路径";
            m["src.col.count"] = "图片数量";
            m["src.state.on"] = "启用";
            m["src.state.off"] = "禁用";
            m["src.allon"] = "启用全部";
            m["src.alloff"] = "禁用全部";
            m["src.refresh"] = "刷新数量";
            m["src.noSource"] = "尚未添加任何壁纸源，点击「添加...」选择一个图片文件夹";
            m["src.summary"] = "共 {0} 个源 · 启用 {1} · 禁用 {2} · 启用源图片合计 {3} 张";
            m["src.hint"] = "勾选「状态」＝ 启用该源，取消勾选 ＝ 禁用（暂时移出轮换）。\r\n禁用只影响轮换，图片与已勾选记录都会保留，重新勾选即恢复；数量递归统计子文件夹，只计支持的壁纸格式。";
            m["src.count.pending"] = "…";
            m["src.count.unavailable"] = "不可用";
            m["src.saved"] = "已保存：{0} 个壁纸源（启用 {1} / 禁用 {2}）";
            m["src.confirm.close"] = "有未保存的壁纸源更改，关闭前要保存吗？";

            m["nav.overview"] = "概览";
            m["nav.sources"] = "壁纸源";
            m["nav.rotate"] = "轮换";
            m["nav.general"] = "常规";
            m["rail.rotating"] = "轮换中";
            m["rail.paused"] = "已暂停";
            m["rail.next"] = "距下次切换";
            m["rail.pause"] = "暂停轮换";
            m["rail.resume"] = "继续轮换";
            m["sb.current"] = "当前壁纸：{0}（共 {1} 张）";
            m["sb.manual"] = "手动模式 · {0} 张";
            m["sb.all"] = "全部图片";
            m["sb.dirty"] = "有未保存的更改";
            m["win.min"] = "最小化";
            m["win.max"] = "最大化";
            m["win.restore"] = "还原";
            m["win.close"] = "关闭";
            m["app.name"] = "WallpaperChanger";
            m["win.help"] = "帮助";
            m["ov.now.showing"] = "正在显示";
            m["ov.next"] = "下一张壁纸";
            m["ov.prev"] = "上一张";
            m["ov.path"] = "文件路径";
            m["ov.fact.pool"] = "参与轮换";
            m["ov.fact.total"] = "扫描到图片";
            m["ov.fact.mode"] = "切换模式";
            m["ov.mode.manual"] = "手动选择";
            m["ov.mode.all"] = "全部源";
            m["src.add.folder"] = "添加文件夹";
            m["src.row.open"] = "打开";
            m["src.row.remove"] = "移除";
            m["src.row.count"] = "{0} 张";
            m["rot.next.at"] = "下次切换 {0}";
            m["gen.light"] = "浅色";
            m["gen.dark"] = "深色";
            m["gen.open.folder"] = "打开所在文件夹";

            m["ov.strip.style"] = "壁纸样式";
            m["ov.strip.interval"] = "切换间隔";
            m["ov.strip.order"] = "图片顺序";
            m["ov.strip.adjust"] = "调整 →";
            m["ov.preview.none"] = "未设置壁纸";
            m["ov.order.random"] = "随机";
            m["ov.order.inorder"] = "顺序";
            m["hist.title"] = "切换历史";
            m["hist.note"] = "本次启动后的浏览轨迹。深色为已看过的，浅色为按「下一张」可恢复的。";
            m["hist.cur"] = "正在显示";
            m["hist.seen"] = "已看过";
            m["hist.undoable"] = "可恢复";
            m["hist.back"] = "回到这张";
            m["hist.unknown.src"] = "未知来源";
            m["hist.empty"] = "本次启动还没有切换过壁纸。";
            m["rot.style.note"] = "缩略图示意图片在屏幕上的铺放方式。";
            m["rot.interval.note"] = "定时到点会重新扫描源目录并挑一张新壁纸。";
            m["rot.order.note"] = "随机为洗牌后不重复播放，顺序为按文件名依次播放。";
            m["rot.hotkeys.title"] = "全局快捷键";
            m["rot.hotkeys.note"] = "点击按键框后按下组合键即可重新绑定，主键盘与小键盘均支持。";
            m["rot.hk.note.next"] = "切换后立即生效";
            m["rot.hk.note.prev"] = "按本次会话历史逐级回退";
            m["gen.startup.title"] = "开机自动启动";
            m["gen.startup.note"] = "开机时直接收进托盘、不弹出窗口，再次启动只是把窗口唤到前台。";
            m["gen.appearance"] = "外观";
            m["gen.appearance.note"] = "浅色 / 深色即时生效，所有窗口与托盘菜单一起跟随。";
            m["gen.language.title"] = "界面语言";
            m["gen.language.note"] = "语言属于即时生效项，点选即写入配置，无需点保存。";
            m["gen.config.title"] = "配置与日志";
            m["gen.about.line"] = "WallpaperChanger {0} · MIT 许可 · 支持 {1}";
            m["src.summary"] = "共 {0} 个源 · 启用 {1} · 禁用 {2} · 合计 {3} 张";
            m["src.page.note"] = "多个文件夹的图片合并进同一个轮换池，可逐个启用或暂时禁用。";
            m["src.list.title"] = "源列表";
            m["src.footnote"] = "数量为递归统计，只计支持的壁纸格式。禁用的源会暂时移出轮换，图片与已勾选记录全部保留。";
            m["src.empty"] = "还没有壁纸源，点右上角「添加文件夹」开始。";
            m["src.manage"] = "管理壁纸源";
            m["src.manual"] = "手动挑选壁纸";
            m["rot.page.note"] = "决定壁纸怎么铺、多久换一次、按什么顺序挑。";
            m["rot.keycap.capture"] = "按下组合键…";
            m["ov.kbd.hint"] = "随时随地按 {0} 下一张 · {1} 上一张";
            m["gen.page.note"] = "启动行为、界面语言与外观。";

            m["help.title"] = "使用帮助";
            m["help.sub"] = "左选章节，右看正文；设置改完记得点状态栏的「保存」。";
            m["help.toc"] = "章节";
            m["help.gotit"] = "知道了";
            return m;
        }

        // ================= English =================
        private static Dictionary<string, string> BuildEn()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["tray.tip"] = "WallpaperChanger - wallpaper rotator";

            m["main.source.group"] = "Wallpaper sources";
            m["main.source.add"] = "Add...";
            m["main.source.remove"] = "Remove selected";
            m["main.source.manage"] = "Manage sources";
            m["main.source.summary.none"] = "No wallpaper source yet - click the button above to add one";
            m["main.source.summary"] = "{0} sources · {1} enabled · {2} disabled";
            m["main.source.all.on"] = "All sources are enabled";
            m["main.manual.btn"] = "Manual wallpaper selection: off";
            m["main.manual.btn.on"] = "Manual wallpaper selection: {0} selected";
            m["main.settings.group"] = "Rotation settings";
            m["main.other.group"] = "Other settings";
            m["main.settings.style"] = "Style:";
            m["main.settings.interval"] = "Interval:";
            m["main.settings.random"] = "Random order";
            m["main.settings.next"] = "Next:";
            m["main.settings.prev"] = "Previous:";
            m["main.settings.autostart"] = "Start with Windows (shortcut in Startup folder)";
            m["main.settings.language"] = "语言/Language:";
            m["main.btn.next"] = "Next wallpaper";
            m["main.btn.prev"] = "Previous wallpaper";
            m["main.btn.save"] = "Save settings";
            m["main.btn.help"] = "Help";
            m["main.theme.tip"] = "Toggle theme";
            m["main.hotkey.none"] = "No hotkey";

            m["status.saved"] = "Settings saved";
            m["hotkey.err.same"] = "Previous hotkey cannot match Next (both Ctrl+{0})";
            m["hotkey.err.next"] = "Could not register the Next hotkey Ctrl+{0} (another program may be using it)";
            m["hotkey.err.prev"] = "Could not register the Previous hotkey Ctrl+{0} (another program may be using it)";
            m["hotkey.err.join"] = "; ";
            m["status.folder.dup"] = "This folder is already in the source list";
            m["status.folder.added"] = "Added wallpaper source: {0} (save to apply)";
            m["status.folder.pickfirst"] = "Select a source in the list first";
            m["status.folder.empty"] = "The source list is already empty";
            m["dialog.pickfolder"] = "Choose a wallpaper picture folder";
            m["status.novalidfolder"] = "Add at least one valid picture folder first";
            m["status.current"] = "Current: {0} ({1} total)";
            m["status.current.prev"] = "Current: {0} (previous)";
            m["status.manual.emptypool"] = "Manual mode is on but no checked image is available (open manual selection and check some)";
            m["status.nopictures"] = "No usable images found in any folder";
            m["status.applyfail"] = "Failed to set wallpaper: {0}";
            m["status.error"] = "Error: {0}";
            m["status.noprev"] = "No earlier wallpaper (this is the first one since startup)";
            m["status.prevtag"] = " (previous)";
            m["status.prevfail"] = "Failed to step back: {0}";
            m["status.nextswitch"] = "Next switch: {0}";
            m["status.paused"] = "Rotation paused";
            m["status.rotate.paused"] = "Rotation paused";
            m["status.rotate.resumed"] = "Rotation resumed";
            m["mode.tag"] = "  [manual · {0}]";

            m["tray.next"] = "Next wallpaper";
            m["tray.prev"] = "Previous wallpaper";
            m["tray.pause"] = "Pause rotation";
            m["tray.resume"] = "Resume rotation";
            m["tray.manual"] = "Manual wallpaper selection...";
            m["tray.open"] = "Open settings";
            m["tray.exit"] = "Exit";
            m["tray.exit.confirm"] = "There are unsaved setting changes. Save before exiting?";
            m["balloon.stillrunning"] = "Still running in the background. Right-click the tray icon to pause or exit.";
            m["balloon.manual.on"] = "Manual selection enabled ({0} checked wallpapers in rotation)";
            m["balloon.manual.off"] = "Manual selection disabled - all wallpapers rotate again";

            m["picker.title"] = "Manual wallpaper selection - WallpaperChanger v{0}";
            m["picker.all"] = "All";
            m["picker.none"] = "None";
            m["picker.invert"] = "Invert";
            m["picker.filter.hint"] = "Filter by file name (case-insensitive)...";
            m["picker.count"] = "{0} of {1} checked";
            m["picker.save"] = "Save";
            m["picker.saveclose"] = "Save and close";
            m["picker.disableall"] = "Turn off manual selection";
            m["picker.disableall.confirm"] = "Manual wallpaper selection is now off. Rotation returns to the full wallpaper pool and this window will close.";
            m["picker.close"] = "Close";
            m["picker.scanning"] = "Scanning images...";
            m["picker.nofolders"] = "No wallpapers found. Add a picture folder in the main window first";
            m["picker.nofiltermatch"] = "No file name matches the filter";
            m["picker.saved.on"] = "Saved: manual selection enabled, rotation limited to {0} checked wallpapers";
            m["picker.saved.off"] = "Saved: no wallpapers checked, manual selection off (all wallpapers rotate)";
            m["picker.confirm.close"] = "There are unsaved changes. Save before closing?";
            m["picker.bottom.hint"] = "Unchecked wallpapers never rotate; the random-order option keeps working inside the checked set";
            m["picker.src.label"] = "Source:";
            m["picker.src.all"] = "All wallpapers";
            m["picker.src.picked"] = "Checked wallpapers ({0})";
            m["picker.src.one"] = "{0} ({1})";
            m["picker.nosrcpick"] = "Nothing is checked yet - switch to \"All wallpapers\" to start picking";
            m["picker.caption"] = "Manual wallpaper selection";

            m["src.title"] = "Wallpaper sources - WallpaperChanger v{0}";
            m["src.caption"] = "Wallpaper sources";
            m["src.col.state"] = "State";
            m["src.col.name"] = "Name";
            m["src.col.path"] = "Path";
            m["src.col.count"] = "Wallpapers";
            m["src.state.on"] = "Enabled";
            m["src.state.off"] = "Disabled";
            m["src.allon"] = "Enable all";
            m["src.alloff"] = "Disable all";
            m["src.refresh"] = "Recount";
            m["src.noSource"] = "No wallpaper source yet - click \"Add...\" to pick a picture folder";
            m["src.summary"] = "{0} sources · {1} enabled · {2} disabled · {3} wallpapers in enabled sources";
            m["src.hint"] = "Tick \"State\" to enable a source, untick to disable it (temporarily removed from rotation).\r\nDisabling only pauses its contribution: the files and their checked wallpapers are kept, and re-ticking restores everything. Counts are recursive and cover supported wallpaper formats only.";
            m["src.count.pending"] = "...";
            m["src.count.unavailable"] = "Unavailable";
            m["src.saved"] = "Saved: {0} sources (enabled {1} / disabled {2})";
            m["src.confirm.close"] = "There are unsaved source changes. Save before closing?";

            m["nav.overview"] = "Overview";
            m["nav.sources"] = "Sources";
            m["nav.rotate"] = "Rotation";
            m["nav.general"] = "General";
            m["rail.rotating"] = "Rotating";
            m["rail.paused"] = "Paused";
            m["rail.next"] = "Next change in";
            m["rail.pause"] = "Pause rotation";
            m["rail.resume"] = "Resume rotation";
            m["sb.current"] = "Current: {0} ({1} total)";
            m["sb.manual"] = "Manual mode · {0}";
            m["sb.all"] = "All wallpapers";
            m["sb.dirty"] = "Unsaved changes";
            m["win.min"] = "Minimize";
            m["win.max"] = "Maximize";
            m["win.restore"] = "Restore";
            m["win.close"] = "Close";
            m["app.name"] = "WallpaperChanger";
            m["win.help"] = "Help";
            m["ov.now.showing"] = "Now showing";
            m["ov.next"] = "Next wallpaper";
            m["ov.prev"] = "Previous";
            m["ov.path"] = "File path";
            m["ov.fact.pool"] = "In rotation";
            m["ov.fact.total"] = "Images scanned";
            m["ov.fact.mode"] = "Mode";
            m["ov.mode.manual"] = "Manual pick";
            m["ov.mode.all"] = "All sources";
            m["src.add.folder"] = "Add folder";
            m["src.row.open"] = "Open";
            m["src.row.remove"] = "Remove";
            m["src.row.count"] = "{0} images";
            m["rot.next.at"] = "Next change {0}";
            m["gen.light"] = "Light";
            m["gen.dark"] = "Dark";
            m["gen.open.folder"] = "Open containing folder";

            m["ov.strip.style"] = "Style";
            m["ov.strip.interval"] = "Interval";
            m["ov.strip.order"] = "Order";
            m["ov.strip.adjust"] = "Adjust →";
            m["ov.preview.none"] = "No wallpaper set";
            m["ov.order.random"] = "Random";
            m["ov.order.inorder"] = "In order";
            m["hist.title"] = "Switch history";
            m["hist.note"] = "Where you have been this session. Dimmed rows were seen already, brighter rows can be restored with Next.";
            m["hist.cur"] = "Now showing";
            m["hist.seen"] = "Seen";
            m["hist.undoable"] = "Restorable";
            m["hist.back"] = "Back to this";
            m["hist.unknown.src"] = "Unknown source";
            m["hist.empty"] = "No wallpaper has been switched yet this session.";
            m["rot.style.note"] = "The thumbnail shows how the image is laid out on screen.";
            m["rot.interval.note"] = "When the timer fires the sources are rescanned and a new wallpaper is picked.";
            m["rot.order.note"] = "Random shuffles and plays without repeats; in-order walks by filename.";
            m["rot.hotkeys.title"] = "Global hotkeys";
            m["rot.hotkeys.note"] = "Click a key box and press a combination to rebind it. Top-row and numpad keys both work.";
            m["rot.hk.note.next"] = "Takes effect immediately";
            m["rot.hk.note.prev"] = "Steps back through this session";
            m["gen.startup.title"] = "Start with Windows";
            m["gen.startup.note"] = "Starts straight into the tray without a window; launching again just brings the window forward.";
            m["gen.appearance"] = "Appearance";
            m["gen.appearance.note"] = "Light / dark applies instantly, and every window and the tray menu follow.";
            m["gen.language.title"] = "Interface language";
            m["gen.language.note"] = "Language changes apply instantly and are written to the config, no save needed.";
            m["gen.config.title"] = "Config & logs";
            m["gen.about.line"] = "WallpaperChanger {0} · MIT licence · supports {1}";
            m["src.summary"] = "{0} sources · {1} on · {2} off · {3} images";
            m["src.page.note"] = "Images from several folders merge into one rotation pool; enable or disable each one.";
            m["src.list.title"] = "Source list";
            m["src.footnote"] = "Counts are recursive and only cover supported wallpaper formats. A disabled source leaves rotation but keeps its files and its checked selection.";
            m["src.empty"] = "No wallpaper source yet - start with \"Add folder\" in the top right.";
            m["src.manage"] = "Manage sources";
            m["src.manual"] = "Pick wallpapers";
            m["rot.page.note"] = "How wallpapers are laid out, how often they change, and in what order they are picked.";
            m["rot.keycap.capture"] = "Press a combination...";
            m["ov.kbd.hint"] = "Press {0} anywhere for next · {1} for previous";
            m["gen.page.note"] = "Startup behaviour, interface language and appearance.";

            m["help.title"] = "Help";
            m["help.sub"] = "Pick a section on the left, read it on the right; remember to press Save in the status bar.";
            m["help.toc"] = "Sections";
            m["help.gotit"] = "OK";
            return m;
        }

        // ================= Japanese =================
        private static Dictionary<string, string> BuildJa()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["tray.tip"] = "WallpaperChanger - 壁紙ローテーター";

            m["main.source.group"] = "壁紙ソース";
            m["main.source.add"] = "追加...";
            m["main.source.remove"] = "選択を削除";
            m["main.source.manage"] = "壁紙ソース管理";
            m["main.source.summary.none"] = "壁紙ソースがまだありません。上のボタンから追加してください";
            m["main.source.summary"] = "ソース {0} 件 · 有効 {1} · 無効 {2}";
            m["main.source.all.on"] = "すべてのソースが有効です";
            m["main.manual.btn"] = "手動壁紙選択：オフ";
            m["main.manual.btn.on"] = "手動壁紙選択：{0} 枚を選択中";
            m["main.settings.group"] = "ローテーション設定";
            m["main.other.group"] = "その他の設定";
            m["main.settings.style"] = "表示スタイル:";
            m["main.settings.interval"] = "切り替え間隔:";
            m["main.settings.random"] = "ランダム順序";
            m["main.settings.next"] = "次へ:";
            m["main.settings.prev"] = "前へ:";
            m["main.settings.autostart"] = "Windows 起動時に自動開始（スタートアップのショートカット）";
            m["main.settings.language"] = "语言/Language:";
            m["main.btn.next"] = "次の壁紙";
            m["main.btn.prev"] = "前の壁紙";
            m["main.btn.save"] = "設定を保存";
            m["main.btn.help"] = "ヘルプ";
            m["main.theme.tip"] = "テーマ切り替え";
            m["main.hotkey.none"] = "ホットキーなし";

            m["status.saved"] = "設定を保存しました";
            m["hotkey.err.same"] = "前の壁紙のショートカットは次と同じにできません（どちらも Ctrl+{0}）";
            m["hotkey.err.next"] = "次の壁紙のショートカット Ctrl+{0} を登録できませんでした（他のプログラムが使用中の可能性があります）";
            m["hotkey.err.prev"] = "前の壁紙のショートカット Ctrl+{0} を登録できませんでした（他のプログラムが使用中の可能性があります）";
            m["hotkey.err.join"] = "；";
            m["status.folder.dup"] = "このフォルダーはすでにソース一覧にあります";
            m["status.folder.added"] = "壁紙ソースを追加しました：{0}（保存で反映）";
            m["status.folder.pickfirst"] = "先にリストで削除するソースを選択してください";
            m["status.folder.empty"] = "ソース一覧はすでに空です";
            m["dialog.pickfolder"] = "壁紙画像のフォルダーを選択してください";
            m["status.novalidfolder"] = "有効な画像フォルダーを先に追加してください";
            m["status.current"] = "現在の壁紙: {0}（全 {1} 枚）";
            m["status.current.prev"] = "現在の壁紙: {0}（前の壁紙）";
            m["status.manual.emptypool"] = "手動モードが有効ですが、チェック済みの画像がありません（手動壁紙選択でチェックしてください）";
            m["status.nopictures"] = "どのフォルダーにも利用できる画像がありません";
            m["status.applyfail"] = "壁紙の設定に失敗: {0}";
            m["status.error"] = "エラー: {0}";
            m["status.noprev"] = "これより前の壁紙はありません（起動後最初の1枚です）";
            m["status.prevtag"] = "（前の壁紙）";
            m["status.prevfail"] = "戻る処理に失敗: {0}";
            m["status.nextswitch"] = "次回の切り替え: {0}";
            m["status.paused"] = "ローテーション停止中";
            m["status.rotate.paused"] = "ローテーションを停止しました";
            m["status.rotate.resumed"] = "ローテーションを再開しました";
            m["mode.tag"] = "　[手動モード · {0} 枚]";

            m["tray.next"] = "次の壁紙";
            m["tray.prev"] = "前の壁紙";
            m["tray.pause"] = "ローテーション一時停止";
            m["tray.resume"] = "ローテーション再開";
            m["tray.manual"] = "手動壁紙選択…";
            m["tray.open"] = "設定を開く";
            m["tray.exit"] = "終了";
            m["tray.exit.confirm"] = "未保存の設定変更があります。終了前に保存しますか？";
            m["balloon.stillrunning"] = "バックグラウンドで動作中です。トレイアイコンを右クリックで一時停止 / 終了できます";
            m["balloon.manual.on"] = "手動壁紙選択を有効にしました（{0} 枚が切り替え対象）";
            m["balloon.manual.off"] = "手動壁紙選択を無効にしました。すべての壁紙が対象になります";

            m["picker.title"] = "手動壁紙選択 - WallpaperChanger v{0}";
            m["picker.all"] = "全選択";
            m["picker.none"] = "全解除";
            m["picker.invert"] = "反転";
            m["picker.filter.hint"] = "ファイル名で絞り込み（大文字小文字を区別しない）…";
            m["picker.count"] = "選択 {0} / 全 {1}";
            m["picker.save"] = "保存";
            m["picker.saveclose"] = "保存して閉じる";
            m["picker.disableall"] = "手動選択を完全にオフにする";
            m["picker.disableall.confirm"] = "手動壁紙選択をオフにしました。すべての壁紙のローテーションに戻ります。このウィンドウは閉じます。";
            m["picker.close"] = "閉じる";
            m["picker.scanning"] = "画像をスキャン中…";
            m["picker.nofolders"] = "利用できる壁紙がありません。先にメインウィンドウの壁紙ソースにフォルダーを追加してください";
            m["picker.nofiltermatch"] = "一致するファイル名がありません";
            m["picker.saved.on"] = "保存しました：手動壁紙選択が有効です。チェックした {0} 枚が切り替え対象です";
            m["picker.saved.off"] = "保存しました：壁紙がチェックされていないため、手動壁紙選択は無効です（すべての壁紙が対象）";
            m["picker.confirm.close"] = "未保存の変更があります。閉じる前に保存しますか？";
            m["picker.bottom.hint"] = "未チェックの壁紙は切り替え対象外です。ランダム順序の設定はそのまま動作します";
            m["picker.src.label"] = "壁紙ソース:";
            m["picker.src.all"] = "すべての壁紙";
            m["picker.src.picked"] = "選択済みの壁紙 ({0})";
            m["picker.src.one"] = "{0}（{1} 枚）";
            m["picker.nosrcpick"] = "まだ壁紙が選択されていません。「すべての壁紙」に切り替えて選んでください";
            m["picker.caption"] = "手動壁紙選択";

            m["src.title"] = "壁紙ソース管理 - WallpaperChanger v{0}";
            m["src.caption"] = "壁紙ソース管理";
            m["src.col.state"] = "状態";
            m["src.col.name"] = "名前";
            m["src.col.path"] = "パス";
            m["src.col.count"] = "壁紙数";
            m["src.state.on"] = "有効";
            m["src.state.off"] = "無効";
            m["src.allon"] = "すべて有効";
            m["src.alloff"] = "すべて無効";
            m["src.refresh"] = "数を再取得";
            m["src.noSource"] = "壁紙ソースがありません。「追加...」で画像フォルダーを選択してください";
            m["src.summary"] = "ソース {0} 件 · 有効 {1} · 無効 {2} · 有効ソースの壁紙合計 {3} 枚";
            m["src.hint"] = "「状態」をチェック＝そのソースを有効、外す＝無効（一時的にローテーションから除外）。\r\n無効にしても画像とチェック済みの記録は保持され、再チェックで復元されます。枚数は再帰的に集計し、対応形式のみを数えます。";
            m["src.count.pending"] = "…";
            m["src.count.unavailable"] = "利用不可";
            m["src.saved"] = "保存しました：壁紙ソース {0} 件（有効 {1} / 無効 {2}）";
            m["src.confirm.close"] = "未保存の壁紙ソース変更があります。閉じる前に保存しますか？";

            m["nav.overview"] = "概要";
            m["nav.sources"] = "壁紙ソース";
            m["nav.rotate"] = "ローテーション";
            m["nav.general"] = "一般";
            m["rail.rotating"] = "ローテーション中";
            m["rail.paused"] = "一時停止中";
            m["rail.next"] = "次回切り替えまで";
            m["rail.pause"] = "ローテーションを停止";
            m["rail.resume"] = "ローテーションを再開";
            m["sb.current"] = "現在の壁紙：{0}（全 {1} 枚）";
            m["sb.manual"] = "手動モード · {0} 枚";
            m["sb.all"] = "すべての壁紙";
            m["sb.dirty"] = "未保存の変更があります";
            m["win.min"] = "最小化";
            m["win.max"] = "最大化";
            m["win.restore"] = "元に戻す";
            m["win.close"] = "閉じる";
            m["app.name"] = "WallpaperChanger";
            m["win.help"] = "ヘルプ";
            m["ov.now.showing"] = "表示中";
            m["ov.next"] = "次の壁紙";
            m["ov.prev"] = "前へ";
            m["ov.path"] = "ファイルパス";
            m["ov.fact.pool"] = "ローテーション対象";
            m["ov.fact.total"] = "読み込み画像";
            m["ov.fact.mode"] = "切り替えモード";
            m["ov.mode.manual"] = "手動選択";
            m["ov.mode.all"] = "すべてのソース";
            m["src.add.folder"] = "フォルダーを追加";
            m["src.row.open"] = "開く";
            m["src.row.remove"] = "削除";
            m["src.row.count"] = "{0} 枚";
            m["rot.next.at"] = "次回 {0}";
            m["gen.light"] = "ライト";
            m["gen.dark"] = "ダーク";
            m["gen.open.folder"] = "保存先を開く";

            m["ov.strip.style"] = "壁紙スタイル";
            m["ov.strip.interval"] = "切り替え間隔";
            m["ov.strip.order"] = "画像の順序";
            m["ov.strip.adjust"] = "調整 →";
            m["ov.preview.none"] = "壁紙が未設定です";
            m["ov.order.random"] = "ランダム";
            m["ov.order.inorder"] = "順番";
            m["hist.title"] = "切り替え履歴";
            m["hist.note"] = "今回の起動後の履歴です。暗い行は閲覧済み、明るい行は「次の壁紙」で復元できます。";
            m["hist.cur"] = "表示中";
            m["hist.seen"] = "閲覧済み";
            m["hist.undoable"] = "復元可能";
            m["hist.back"] = "この画像へ";
            m["hist.unknown.src"] = "不明なソース";
            m["hist.empty"] = "今回の起動ではまだ壁紙を切り替えていません。";
            m["rot.style.note"] = "サムネイルは画像の配置方法を示しています。";
            m["rot.interval.note"] = "タイマーが作動するとソースを再スキャンして新しい壁紙を選びます。";
            m["rot.order.note"] = "ランダムは重複なしで再生し、順番はファイル名順に再生します。";
            m["rot.hotkeys.title"] = "グローバルホットキー";
            m["rot.hotkeys.note"] = "キー欄をクリックしてから組み合わせキーを押すと再割り当てできます。";
            m["rot.hk.note.next"] = "切り替え後すぐに反映されます";
            m["rot.hk.note.prev"] = "今回のセッション履歴を順に戻ります";
            m["gen.startup.title"] = "Windows 起動時に開始";
            m["gen.startup.note"] = "起動時はウィンドウを出さずトレイに入ります。再起動するとウィンドウが前面に出るだけです。";
            m["gen.appearance"] = "外観";
            m["gen.appearance.note"] = "ライト / ダークは即時反映され、すべてのウィンドウとトレイメニューが追従します。";
            m["gen.language.title"] = "表示言語";
            m["gen.language.note"] = "言語は即時反映され、選択と同時に設定へ保存されます。";
            m["gen.config.title"] = "設定とログ";
            m["gen.about.line"] = "WallpaperChanger {0} · MIT ライセンス · 対応形式 {1}";
            m["src.summary"] = "ソース {0} 件 · 有効 {1} · 無効 {2} · 合計 {3} 枚";
            m["src.page.note"] = "複数フォルダの画像を 1 つのローテーションにまとめます。個別に有効 / 無効を切り替えられます。";
            m["src.list.title"] = "ソース一覧";
            m["src.footnote"] = "枚数は再帰的に数え、対応形式のみを対象とします。無効にしたソースはローテーションから外れるだけで、画像と選択状態は保持されます。";
            m["src.empty"] = "壁紙ソースがまだありません。右上の「フォルダーを追加」から始めてください。";
            m["src.manage"] = "ソースを管理";
            m["src.manual"] = "壁紙を選択";
            m["rot.page.note"] = "壁紙の配置方法、切り替え頻度、選択順を決めます。";
            m["rot.keycap.capture"] = "組み合わせキーを押す…";
            m["ov.kbd.hint"] = "いつでも {0} で次の壁紙 · {1} で前の壁紙";
            m["gen.page.note"] = "起動時の動作、表示言語、外観。";

            m["help.title"] = "ヘルプ";
            m["help.sub"] = "左で項目を選び、右で本文を読みます。設定後はステータスバーの「保存」を忘れずに。";
            m["help.toc"] = "項目";
            m["help.gotit"] = "閉じる";
            return m;
        }

        // ================= help content =================
        // Line kinds: 0 = body, 1 = section head, 2 = title.
        public struct HelpLine
        {
            public readonly int Kind;
            public readonly string Text;
            public HelpLine(int kind, string text) { Kind = kind; Text = text; }
        }

        public static HelpLine[] HelpContent()
        {
            if (Language == En) return BuildHelpEn();
            if (Language == Ja) return BuildHelpJa();
            return BuildHelpZh();
        }

        private static HelpLine[] BuildHelpZh()
        {
            return new HelpLine[]
            {
                new HelpLine(1, "界面一览"),
                new HelpLine(0, "  • 左侧导航共四页：概览 / 壁纸源 / 轮换 / 常规。"),
                new HelpLine(0, "  • 改完设置（壁纸源列表也一样）点一次底部状态栏右侧的「保存」才写盘；有未保存改动时状态栏会提示。"),
                new HelpLine(0, "  • 标题栏右侧依次是帮助、最小化 / 最大化 / 关闭；点关闭只是收进托盘。"),
                new HelpLine(0, ""),
                new HelpLine(1, "壁纸源"),
                new HelpLine(0, "  • 在「壁纸源」页直接管理：右上角「添加文件夹」新增一个源。"),
                new HelpLine(0, "  • 每行左边的开关＝启用 / 禁用该源，行尾的垃圾桶＝从列表里移除。"),
                new HelpLine(0, "  • 禁用只是暂时移出轮换，图片与已勾选的壁纸都保留，重新打开开关即恢复。"),
                new HelpLine(0, "  • 每行显示了该源的壁纸数量（递归统计子文件夹，只计支持的壁纸格式）；文件夹不存在会显示「不可用」。"),
                new HelpLine(0, "  • 「启用全部 / 禁用全部」批量切换，「重新统计」重新数一遍数量。"),
                new HelpLine(0, "  • 可以添加多个文件夹，只有启用的源会进入同一个轮换池。"),
                new HelpLine(0, ""),
                new HelpLine(1, "轮换与外观"),
                new HelpLine(0, "  • 「轮换」页决定壁纸怎么铺（填充 / 适应 / 拉伸 / 平铺 / 居中 / 跨屏）、多久换一次（1 分钟至 1 天）、按随机顺序还是列表顺序挑。"),
                new HelpLine(0, "  • 「常规」页控制开机自动启动、浅色 / 深色主题、界面语言（中文 / English / 日本語）。"),
                new HelpLine(0, "  • 主题与语言点选即生效、立即写入配置；其余设置需要点「保存」。"),
                new HelpLine(0, ""),
                new HelpLine(1, "快捷键"),
                new HelpLine(0, "  • 默认：Ctrl+9 = 下一张，Ctrl+8 = 上一张（可在「轮换」页重新绑定）。"),
                new HelpLine(0, "  • 主键盘与小键盘数字键都支持。"),
                new HelpLine(0, "  • 「上一张」可连续回退，最远回到本次启动时显示的那张壁纸。"),
                new HelpLine(0, ""),
                new HelpLine(1, "手动挑选壁纸"),
                new HelpLine(0, "  • 入口在「壁纸源」页的「手动挑选壁纸」；勾选至少一张就进入手动模式，全部取消勾选即退出。"),
                new HelpLine(0, "  • 定时轮换与「下一张」只从勾选的壁纸里挑，随机顺序开关照常生效。"),
                new HelpLine(0, "  • 顶部输入框按文件名筛选，「全选 / 全不选 / 反选」只作用于当前筛选出的图片。"),
                new HelpLine(0, "  • 有多个源时顶部还有源筛选器；再次打开时默认只显示上次勾选的壁纸。"),
                new HelpLine(0, "  • 勾选集合存在配置里，重启后保持；之后新增的图片默认未勾选。"),
                new HelpLine(0, "  • 「上一张」属于历史回退，不受勾选限制。"),
                new HelpLine(0, "  • 挑完点「保存并关闭」，一次性保存并退出；窗口若有未保存改动会先询问。"),
                new HelpLine(0, ""),
                new HelpLine(1, "支持的图片格式"),
                new HelpLine(0, "  • jpg / png / jfif / bmp / webp / gif / tiff。"),
                new HelpLine(0, "  • 自动跳过系统隐藏文件（如 Thumbs.db）与损坏的图片。"),
                new HelpLine(0, ""),
                new HelpLine(1, "新增壁纸何时生效"),
                new HelpLine(0, "  • 程序不实时监控文件夹；定时到点会重新扫描并挑一张新壁纸（手动「下一张」则先走历史）。"),
                new HelpLine(0, "  • 新增图片会自动纳入轮换；随机模式下会重新洗牌，之后很快就能轮到新图。"),
                new HelpLine(0, "  • 「上一张」与「下一张」在本次启动的历史里来回走：回退后按「下一张」会先恢复刚才回退掉的那张，历史走完才会重新扫描挑新图。"),
            };
        }

        private static HelpLine[] BuildHelpEn()
        {
            return new HelpLine[]
            {
                new HelpLine(1, "The window at a glance"),
                new HelpLine(0, "  • Four pages down the left: Overview / Sources / Rotation / General."),
                new HelpLine(0, "  • After changing anything - the source list included - press Save on the right of the status bar. The bar warns while changes are unsaved."),
                new HelpLine(0, "  • The buttons at the right of the title bar are Help, then minimise / maximise / close. Closing only puts the window back in the tray."),
                new HelpLine(0, ""),
                new HelpLine(1, "Wallpaper sources"),
                new HelpLine(0, "  • Manage them right on the Sources page: Add folder in the top right starts a new source."),
                new HelpLine(0, "  • The switch at the left of a row enables or disables that source; the bin at the end of the row removes it."),
                new HelpLine(0, "  • Disabling only takes a source out of rotation: its files and its checked wallpapers are kept, and switching it back on restores everything."),
                new HelpLine(0, "  • Each row shows how many wallpapers the source holds (recursive, subfolders included, supported formats only). A missing folder is marked Unavailable."),
                new HelpLine(0, "  • Enable all / Disable all toggle every row at once; Recount re-reads the counts."),
                new HelpLine(0, "  • Several folders are fine, but only the enabled ones feed the one rotation pool."),
                new HelpLine(0, ""),
                new HelpLine(1, "Rotation and appearance"),
                new HelpLine(0, "  • The Rotation page decides how a wallpaper is laid out (fill / fit / stretch / tile / centre / span), how often it changes (1 minute to 1 day), and whether the order is random or by list."),
                new HelpLine(0, "  • The General page holds Start with Windows, the light / dark theme and the interface language (中文 / English / 日本語)."),
                new HelpLine(0, "  • Theme and language apply and are written to the config the moment they are picked; everything else waits for Save."),
                new HelpLine(0, ""),
                new HelpLine(1, "Hotkeys"),
                new HelpLine(0, "  • Defaults: Ctrl+9 = next wallpaper, Ctrl+8 = previous. Both are rebindable on the Rotation page."),
                new HelpLine(0, "  • Both the main-row and the numeric keypad digits work."),
                new HelpLine(0, "  • Previous steps back repeatedly, as far as the wallpaper shown when the program started."),
                new HelpLine(0, ""),
                new HelpLine(1, "Picking wallpapers by hand"),
                new HelpLine(0, "  • The entry point is Pick wallpapers on the Sources page. Checking at least one turns manual mode on; unchecking all of them turns it off."),
                new HelpLine(0, "  • While at least one is checked, timed rotation and Next draw only from the checked set; the random-order option keeps working."),
                new HelpLine(0, "  • The box at the top filters by file name. All / None / Invert affect only the images currently filtered in."),
                new HelpLine(0, "  • With several sources a source filter appears at the top, so you can pick per source; reopening the window shows the wallpapers checked last time."),
                new HelpLine(0, "  • The checked set lives in the config and survives a restart; images added later start unchecked."),
                new HelpLine(0, "  • Previous is a history walk and ignores the checked set."),
                new HelpLine(0, "  • Save and close stores the picks and leaves in one step; the window asks first if there is anything unsaved."),
                new HelpLine(0, ""),
                new HelpLine(1, "Supported image formats"),
                new HelpLine(0, "  • jpg / png / jfif / bmp / webp / gif / tiff."),
                new HelpLine(0, "  • Hidden system files (Thumbs.db and friends) and corrupted images are skipped automatically."),
                new HelpLine(0, ""),
                new HelpLine(1, "When new wallpapers take effect"),
                new HelpLine(0, "  • Folders are not watched in real time; every timer tick rescans and picks a fresh wallpaper (a manual Next walks the history first)."),
                new HelpLine(0, "  • Images added later join rotation automatically; in random mode the list is reshuffled, so new ones come up soon."),
                new HelpLine(0, "  • Previous and Next walk this session history: after stepping back, Next first restores the wallpaper you stepped away from, and only picks fresh images once the history runs out. Previous reaches back at most to the wallpaper shown at startup."),
            };
        }

        private static HelpLine[] BuildHelpJa()
        {
            return new HelpLine[]
            {
                new HelpLine(1, "画面の概要"),
                new HelpLine(0, "  • 左側のナビゲーションは四ページ：概要 / ソース / ローテーション / 一般。"),
                new HelpLine(0, "  • 設定（ソース一覧も同じ）を変えたら、ステータスバー右側の「保存」を一度押してください。未保存の変更があるとバーに表示されます。"),
                new HelpLine(0, "  • タイトルバー右側は、ヘルプ、最小化 / 最大化 / 閉じるの順です。閉じてもトレイに入るだけです。"),
                new HelpLine(0, ""),
                new HelpLine(1, "壁紙ソース"),
                new HelpLine(0, "  • 「ソース」ページで直接管理します。右上の「フォルダーを追加」で新規追加できます。"),
                new HelpLine(0, "  • 各行左のスイッチで有効 / 無効を切り替え、行末のゴミ箱で一覧から削除します。"),
                new HelpLine(0, "  • 無効にしてもローテーションから外れるだけで、画像とチェック済みの壁紙は保持されます。元に戻すのはスイッチを入れるだけです。"),
                new HelpLine(0, "  • 各行にそのソースの壁紙数が表示されます（サブフォルダーを含めて再帰集計、対応形式のみ）。フォルダーが無い場合は「利用不可」と表示されます。"),
                new HelpLine(0, "  • 「すべて有効 / すべて無効」で一括切り替え、「数を再計算」で数え直します。"),
                new HelpLine(0, "  • 複数フォルダーを追加できますが、有効なソースだけが一つのローテーションプールに入ります。"),
                new HelpLine(0, ""),
                new HelpLine(1, "ローテーションと外観"),
                new HelpLine(0, "  • 「ローテーション」ページで、表示方法（全画面 / フィット / 伸縮 / タイル / 中央 / 複数画面）、切り替え間隔（1分〜1日）、ランダム順かリスト順かを決めます。"),
                new HelpLine(0, "  • 「一般」ページには、Windows 起動時の自動開始、ライト / ダークテーマ、表示言語（中文 / English / 日本語）があります。"),
                new HelpLine(0, "  • テーマと言語は選んだ瞬間に反映され設定に書き込まれます。その他の設定は「保存」が必要です。"),
                new HelpLine(0, ""),
                new HelpLine(1, "ホットキー"),
                new HelpLine(0, "  • 既定：Ctrl+9 = 次の壁紙、Ctrl+8 = 前の壁紙（「ローテーション」ページで変更可能）。"),
                new HelpLine(0, "  • メインキーボードとテンキーの数字の両方に対応しています。"),
                new HelpLine(0, "  • 「前の壁紙」は連続して戻れます。起動時に表示されていた壁紙まで戻れます。"),
                new HelpLine(0, ""),
                new HelpLine(1, "手動で壁紙を選ぶ"),
                new HelpLine(0, "  • 入口は「ソース」ページの「壁紙を手動で選ぶ」です。1 枚でもチェックすれば手動モードになり、すべて外すと解除されます。"),
                new HelpLine(0, "  • チェックした壁紙がある間は、定時ローテーションと「次の壁紙」はチェック済みのものからだけ選ばれます。ランダム順の設定はそのまま動作します。"),
                new HelpLine(0, "  • 上部の入力欄でファイル名を絞り込めます。「全選択 / 全解除 / 反転」は現在絞り込まれた画像にのみ作用します。"),
                new HelpLine(0, "  • ソースが複数ある場合は上部にソース絞り込みが出ます。次に開くと、前回チェックした壁紙だけが表示されます。"),
                new HelpLine(0, "  • チェック内容は設定に保存され、再起動後も保持されます。後から追加した画像は既定で未チェックです。"),
                new HelpLine(0, "  • 「前の壁紙」は履歴をたどるもので、チェックの対象外です。"),
                new HelpLine(0, "  • 「保存して閉じる」で保存と終了を一度に行います。未保存の変更がある場合は先に確認されます。"),
                new HelpLine(0, ""),
                new HelpLine(1, "対応画像形式"),
                new HelpLine(0, "  • jpg / png / jfif / bmp / webp / gif / tiff。"),
                new HelpLine(0, "  • 隠しシステムファイル（Thumbs.db など）と破損画像は自動的にスキップされます。"),
                new HelpLine(0, ""),
                new HelpLine(1, "新しい壁紙が反映されるタイミング"),
                new HelpLine(0, "  • フォルダーはリアルタイム監視されません。タイマーの時刻に再スキャンして新しい壁紙を選びます（手動の「次の壁紙」はまず履歴をたどります）。"),
                new HelpLine(0, "  • 追加した画像は自動的にローテーションに加わります。ランダムモードではシャッフルされ、まもなく新しい画像が登場します。"),
                new HelpLine(0, "  • 「前の壁紙」と「次の壁紙」は今回の起動中の履歴を行き来します。戻った後に「次の壁紙」を押すと、まず戻った直前の壁紙が復元され、履歴を使い切ってから新しい画像を選びます。"),
            };
        }
    }
}
