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

            m["main.source.group"] = "壁纸源（通过按钮添加，不可手动输入）";
            m["main.source.add"] = "添加...";
            m["main.source.remove"] = "删除选中";
            m["main.source.clear"] = "清空全部";
            m["main.manual.btn"] = "手动壁纸选择…（勾选后仅切换选中壁纸）";
            m["main.settings.group"] = "轮换设置";
            m["main.settings.style"] = "壁纸样式:";
            m["main.settings.interval"] = "切换频率:";
            m["main.settings.random"] = "随机图片顺序";
            m["main.settings.next"] = "下一张:";
            m["main.settings.prev"] = "上一张:";
            m["main.settings.autostart"] = "开机自动启动（启动文件夹快捷方式）";
            m["main.settings.language"] = "语言:";
            m["main.btn.next"] = "下一张壁纸";
            m["main.btn.prev"] = "上一张壁纸";
            m["main.btn.save"] = "保存设置";
            m["main.btn.help"] = "帮助";
            m["main.hotkey.none"] = "无快捷键";

            m["status.saved"] = "设置已保存";
            m["status.folder.dup"] = "该文件夹已经在壁纸源列表里";
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
            m["picker.master"] = "启用手动选择功能";
            m["picker.master.hint.on"] = "已启用：仅勾选的壁纸参与切换";
            m["picker.master.hint.off"] = "未启用：下方勾选只保存、不生效（保存时会询问是否启用）";
            m["picker.all"] = "全选";
            m["picker.none"] = "全不选";
            m["picker.invert"] = "反选";
            m["picker.filter.hint"] = "筛选文件名（不区分大小写）…";
            m["picker.count"] = "已选 {0} / 共 {1}";
            m["picker.save"] = "保存";
            m["picker.close"] = "关闭";
            m["picker.scanning"] = "正在扫描图片…";
            m["picker.nofolders"] = "没有找到可用壁纸，请先在主窗口的壁纸源里添加图片文件夹";
            m["picker.nofiltermatch"] = "没有匹配的文件名";
            m["picker.saved.on"] = "已保存：手动壁纸选择已启用，切换范围为已勾选的 {0} 张壁纸";
            m["picker.saved.off"] = "已保存：手动壁纸选择已关闭（勾选集合已保留，{0} 张）";
            m["picker.confirm.close"] = "有未保存的勾选更改，关闭前要保存吗？";
            m["picker.bottom.hint"] = "未勾选的壁纸不参与自动 / 手动切换；随机顺序开关不受影响，仍在勾选池内打乱";
            m["picker.enable.prompt"] = "已勾选 {0} 张壁纸，但\"启用手动选择功能\"总开关未开启，勾选不会生效。\n要现在启用吗？";
            m["picker.caption"] = "手动壁纸选择";

            m["help.title"] = "使用帮助";
            m["help.gotit"] = "知道了";
            return m;
        }

        // ================= English =================
        private static Dictionary<string, string> BuildEn()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["tray.tip"] = "WallpaperChanger - wallpaper rotator";

            m["main.source.group"] = "Wallpaper sources (add via buttons, no manual input)";
            m["main.source.add"] = "Add...";
            m["main.source.remove"] = "Remove selected";
            m["main.source.clear"] = "Clear all";
            m["main.manual.btn"] = "Manual wallpaper selection... (switch only checked ones)";
            m["main.settings.group"] = "Rotation settings";
            m["main.settings.style"] = "Style:";
            m["main.settings.interval"] = "Interval:";
            m["main.settings.random"] = "Random order";
            m["main.settings.next"] = "Next:";
            m["main.settings.prev"] = "Previous:";
            m["main.settings.autostart"] = "Start with Windows (shortcut in Startup folder)";
            m["main.settings.language"] = "Language:";
            m["main.btn.next"] = "Next wallpaper";
            m["main.btn.prev"] = "Previous wallpaper";
            m["main.btn.save"] = "Save settings";
            m["main.btn.help"] = "Help";
            m["main.hotkey.none"] = "No hotkey";

            m["status.saved"] = "Settings saved";
            m["status.folder.dup"] = "This folder is already in the source list";
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
            m["picker.master"] = "Enable manual selection";
            m["picker.master.hint.on"] = "Enabled: only checked wallpapers take part in switching";
            m["picker.master.hint.off"] = "Disabled: checks below are saved but have no effect (you will be asked on save)";
            m["picker.all"] = "All";
            m["picker.none"] = "None";
            m["picker.invert"] = "Invert";
            m["picker.filter.hint"] = "Filter by file name (case-insensitive)...";
            m["picker.count"] = "{0} of {1} checked";
            m["picker.save"] = "Save";
            m["picker.close"] = "Close";
            m["picker.scanning"] = "Scanning images...";
            m["picker.nofolders"] = "No wallpapers found. Add a picture folder in the main window first";
            m["picker.nofiltermatch"] = "No file name matches the filter";
            m["picker.saved.on"] = "Saved: manual selection enabled, rotation limited to {0} checked wallpapers";
            m["picker.saved.off"] = "Saved: manual selection off (checks kept, {0} images)";
            m["picker.confirm.close"] = "There are unsaved changes. Save before closing?";
            m["picker.bottom.hint"] = "Unchecked wallpapers never rotate; the random-order option keeps working inside the checked set";
            m["picker.enable.prompt"] = "You checked {0} wallpapers, but the \"Enable manual selection\" master switch is off, so the checks have no effect.\nEnable it now?";
            m["picker.caption"] = "Manual wallpaper selection";

            m["help.title"] = "Help";
            m["help.gotit"] = "OK";
            return m;
        }

        // ================= Japanese =================
        private static Dictionary<string, string> BuildJa()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["tray.tip"] = "WallpaperChanger - 壁紙ローテーター";

            m["main.source.group"] = "壁紙ソース（ボタンで追加、手入力不可）";
            m["main.source.add"] = "追加...";
            m["main.source.remove"] = "選択を削除";
            m["main.source.clear"] = "すべてクリア";
            m["main.manual.btn"] = "手動壁紙選択…（チェックした壁紙のみ切り替え）";
            m["main.settings.group"] = "ローテーション設定";
            m["main.settings.style"] = "表示スタイル:";
            m["main.settings.interval"] = "切り替え間隔:";
            m["main.settings.random"] = "ランダム順序";
            m["main.settings.next"] = "次へ:";
            m["main.settings.prev"] = "前へ:";
            m["main.settings.autostart"] = "Windows 起動時に自動開始（スタートアップのショートカット）";
            m["main.settings.language"] = "言語:";
            m["main.btn.next"] = "次の壁紙";
            m["main.btn.prev"] = "前の壁紙";
            m["main.btn.save"] = "設定を保存";
            m["main.btn.help"] = "ヘルプ";
            m["main.hotkey.none"] = "ホットキーなし";

            m["status.saved"] = "設定を保存しました";
            m["status.folder.dup"] = "このフォルダーはすでにソース一覧にあります";
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
            m["picker.master"] = "手動選択機能を有効化";
            m["picker.master.hint.on"] = "有効：チェックした壁紙のみが切り替え対象になります";
            m["picker.master.hint.off"] = "無効：チェックは保存されるだけで反映されません（保存時に確認します）";
            m["picker.all"] = "全選択";
            m["picker.none"] = "全解除";
            m["picker.invert"] = "反転";
            m["picker.filter.hint"] = "ファイル名で絞り込み（大文字小文字を区別しない）…";
            m["picker.count"] = "選択 {0} / 全 {1}";
            m["picker.save"] = "保存";
            m["picker.close"] = "閉じる";
            m["picker.scanning"] = "画像をスキャン中…";
            m["picker.nofolders"] = "利用できる壁紙がありません。先にメインウィンドウの壁紙ソースにフォルダーを追加してください";
            m["picker.nofiltermatch"] = "一致するファイル名がありません";
            m["picker.saved.on"] = "保存しました：手動壁紙選択が有効です。チェックした {0} 枚が切り替え対象です";
            m["picker.saved.off"] = "保存しました：手動壁紙選択は無効です（チェック内容は保持、{0} 枚）";
            m["picker.confirm.close"] = "未保存の変更があります。閉じる前に保存しますか？";
            m["picker.bottom.hint"] = "未チェックの壁紙は切り替え対象外です。ランダム順序の設定はそのまま動作します";
            m["picker.enable.prompt"] = "{0} 枚の壁紙をチェックしましたが、「手動選択機能を有効化」がオフのため、チェックは反映されません。\n今すぐ有効にしますか？";
            m["picker.caption"] = "手動壁紙選択";

            m["help.title"] = "ヘルプ";
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
                new HelpLine(1, "■ 壁纸源管理"),
                new HelpLine(0, "  • 只能点击\"添加...\"按钮选择文件夹加入列表，不允许手动输入路径。"),
                new HelpLine(0, "  • 选中列表中的一项后点\"删除选中\"即可移除该壁纸源。"),
                new HelpLine(0, "  • \"清空全部\"一键移除所有壁纸源。"),
                new HelpLine(0, "  • 可添加多个文件夹，程序会合并扫描所有来源的图片。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 保存与后台运行"),
                new HelpLine(0, "  • 修改任意设置后，请点击\"保存设置\"按钮，才会写入配置文件。"),
                new HelpLine(0, "  • 关闭窗口只是最小化到托盘，程序仍在后台轮换；右键托盘图标可暂停 / 退出。"),
                new HelpLine(0, "  • 左上角语言下拉可随时切换 中文 / English / 日本語，立即生效。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 快捷键"),
                new HelpLine(0, "  • 默认：Ctrl+9 = 下一张，Ctrl+8 = 上一张（可在设置中修改绑定）。"),
                new HelpLine(0, "  • 主键盘与小键盘数字键都支持。"),
                new HelpLine(0, "  • \"上一张\"可连续回退，最远回到本次启动时显示的那张壁纸。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 手动壁纸选择"),
                new HelpLine(0, "  • 点击\"手动壁纸选择\"打开勾选窗口；左上角\"启用手动选择功能\"是总开关，不开启时勾选不生效。"),
                new HelpLine(0, "  • 总开关未开启时勾选并保存，程序会主动询问是否立即启用，避免误以为已生效。"),
                new HelpLine(0, "  • 启用后，定时轮换与\"下一张\"只从勾选的壁纸里挑（未勾选的不参与切换）；随机顺序开关不受影响。"),
                new HelpLine(0, "  • 顶部输入框可按文件名筛选，\"全选 / 全不选 / 反选\"只作用于当前筛选出的图片。"),
                new HelpLine(0, "  • 勾选集合与总开关都保存在配置里，重启后保持；之后新增的图片默认未勾选。"),
                new HelpLine(0, "  • \"上一张\"属于历史回退、不受勾选限制；修改后请点窗口右下角\"保存\"才会生效。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 支持的图片格式"),
                new HelpLine(0, "  • jpg / png / jfif / bmp / webp / gif / tiff。"),
                new HelpLine(0, "  • 自动跳过系统隐藏文件（如 Thumbs.db）与损坏的图片。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 新增壁纸何时生效"),
                new HelpLine(0, "  • 程序不实时监控文件夹；定时到点会重新扫描并挑一张新壁纸（手动\"下一张\"则先走历史）。"),
                new HelpLine(0, "  • 新增图片会自动纳入轮换；随机模式下会重新洗牌，之后很快就能轮到新图。"),
                new HelpLine(0, "  • \"上一张\" / \"下一张\"在本次启动的历史里来回走：回退后按\"下一张\"会先恢复刚才回退掉的那张，"),
                new HelpLine(0, "    历史走完才会重新扫描挑新图；\"上一张\"最远回到本次启动时显示的那张壁纸。"),
            };
        }

        private static HelpLine[] BuildHelpEn()
        {
            return new HelpLine[]
            {
                new HelpLine(1, "■ Wallpaper sources"),
                new HelpLine(0, "  • Use the \"Add...\" button to pick a folder; typing paths manually is not allowed."),
                new HelpLine(0, "  • Select an item in the list and click \"Remove selected\" to drop that source."),
                new HelpLine(0, "  • \"Clear all\" removes every source at once."),
                new HelpLine(0, "  • Multiple folders are fine; images from all sources are scanned together."),
                new HelpLine(0, ""),
                new HelpLine(1, "■ Saving & background operation"),
                new HelpLine(0, "  • After changing any setting, click \"Save settings\" to write it to the config file."),
                new HelpLine(0, "  • Closing the window only minimizes to the tray; rotation keeps running. Right-click the tray icon to pause or exit."),
                new HelpLine(0, "  • The language combo switches 中文 / English / 日本語 at any time, applied immediately."),
                new HelpLine(0, ""),
                new HelpLine(1, "■ Hotkeys"),
                new HelpLine(0, "  • Defaults: Ctrl+9 = next wallpaper, Ctrl+8 = previous (rebindable in settings)."),
                new HelpLine(0, "  • Both the main-row and the numeric keypad digits work."),
                new HelpLine(0, "  • \"Previous\" steps back repeatedly, as far as the wallpaper shown when the program started."),
                new HelpLine(0, ""),
                new HelpLine(1, "■ Manual wallpaper selection"),
                new HelpLine(0, "  • Click \"Manual wallpaper selection\" to open the picker; the \"Enable manual selection\" checkbox in its top-left corner is the master switch - checks have no effect while it is off."),
                new HelpLine(0, "  • If you save checks while the master switch is off, the app asks whether to enable it right away, so nothing is silently ignored."),
                new HelpLine(0, "  • While enabled, timed rotation and \"Next\" draw only from the checked wallpapers; the random-order option keeps working."),
                new HelpLine(0, "  • The top input box filters by file name; \"All / None / Invert\" affect only the currently filtered images."),
                new HelpLine(0, "  • The checked set and the master switch persist in the config; newly added images start unchecked."),
                new HelpLine(0, "  • \"Previous\" is a history walk and ignores the checked set; click \"Save\" in the picker to apply changes."),
                new HelpLine(0, ""),
                new HelpLine(1, "■ Supported image formats"),
                new HelpLine(0, "  • jpg / png / jfif / bmp / webp / gif / tiff."),
                new HelpLine(0, "  • Hidden system files (e.g. Thumbs.db) and corrupted images are skipped automatically."),
                new HelpLine(0, ""),
                new HelpLine(1, "■ When new wallpapers take effect"),
                new HelpLine(0, "  • Folders are not watched in real time; each timer tick rescans and picks a new wallpaper (manual \"Next\" walks the history first)."),
                new HelpLine(0, "  • Newly added images join rotation automatically; in random mode the list is reshuffled and new images come up soon."),
                new HelpLine(0, "  • \"Previous\" / \"Next\" walk through this session's history: after stepping back, \"Next\" first restores the wallpaper you stepped away from,"),
                new HelpLine(0, "    and only picks fresh images once the history is exhausted; \"Previous\" goes back at most to the wallpaper shown at startup."),
            };
        }

        private static HelpLine[] BuildHelpJa()
        {
            return new HelpLine[]
            {
                new HelpLine(1, "■ 壁紙ソース"),
                new HelpLine(0, "  • 「追加...」ボタンでフォルダーを選択して追加します。パスの手入力はできません。"),
                new HelpLine(0, "  • リストの項目を選んで「選択を削除」をクリックすると、そのソースを削除できます。"),
                new HelpLine(0, "  • 「すべてクリア」で全ソースを一括削除します。"),
                new HelpLine(0, "  • 複数フォルダーを追加でき、すべてのソースの画像をまとめてスキャンします。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 保存とバックグラウンド動作"),
                new HelpLine(0, "  • 設定を変更したら「設定を保存」をクリックすると設定ファイルに書き込まれます。"),
                new HelpLine(0, "  • ウィンドウを閉じてもトレイに最小化するだけで、ローテーションは継続します。トレイアイコンの右クリックで一時停止 / 終了できます。"),
                new HelpLine(0, "  • 左上の言語ドロップダウンで 中文 / English / 日本語 をいつでも切り替えられます（即時反映）。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ ホットキー"),
                new HelpLine(0, "  • 既定：Ctrl+9 = 次の壁紙、Ctrl+8 = 前の壁紙（設定で変更可能）。"),
                new HelpLine(0, "  • メインキーボードとテンキーの数字の両方に対応しています。"),
                new HelpLine(0, "  • 「前の壁紙」は連続して戻れます。起動時に表示されていた壁紙まで戻れます。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 手動壁紙選択"),
                new HelpLine(0, "  • 「手動壁紙選択」で選択ウィンドウを開きます。左上の「手動選択機能を有効化」がマスタースイッチで、オフの間はチェックが反映されません。"),
                new HelpLine(0, "  • マスタースイッチがオフのままチェックして保存すると、すぐ有効にするか確認されます。気づかずに保存されることはありません。"),
                new HelpLine(0, "  • 有効にすると、定時ローテーションと「次の壁紙」はチェック済みの壁紙のみから選ばれます。ランダム順序の設定はそのまま動作します。"),
                new HelpLine(0, "  • 上部の入力欄でファイル名を絞り込めます。「全選択 / 全解除 / 反転」は現在絞り込まれた画像にのみ作用します。"),
                new HelpLine(0, "  • チェック内容とマスタースイッチは設定に保存され、再起動後も保持されます。追加した画像は既定で未チェックです。"),
                new HelpLine(0, "  • 「前の壁紙」は履歴をたどるもので、チェックの対象外です。変更は右下の「保存」で反映されます。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 対応画像形式"),
                new HelpLine(0, "  • jpg / png / jfif / bmp / webp / gif / tiff。"),
                new HelpLine(0, "  • 隠しシステムファイル（Thumbs.db など）と破損画像は自動的にスキップされます。"),
                new HelpLine(0, ""),
                new HelpLine(1, "■ 新しい壁紙が反映されるタイミング"),
                new HelpLine(0, "  • フォルダーはリアルタイム監視されません。タイマーの時刻になると再スキャンして新しい壁紙を選びます（手動の「次の壁紙」はまず履歴をたどります）。"),
                new HelpLine(0, "  • 追加した画像は自動的にローテーションに加わります。ランダムモードではシャッフルされ、まもなく新しい画像が登場します。"),
                new HelpLine(0, "  • 「前の壁紙」/「次の壁紙」は今回の起動中の履歴を行き来します。戻った後に「次の壁紙」を押すと、まず戻った直前の壁紙が復元され、"),
                new HelpLine(0, "    履歴を使い切ってから新しい画像を選びます。「前の壁紙」は起動時に表示されていた壁紙まで戻れます。"),
            };
        }
    }
}
