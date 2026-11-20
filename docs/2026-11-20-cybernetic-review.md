# WallpaperChanger 工程控制论代码审查

- 审查对象：`H:\Project\WallpaperChanger`（v2.0.12，HEAD `06423be`，main）
- 审查日期：2026-11-20
- 方法：钱学森《工程控制论》——软件 = 受控系统，可靠性来自负反馈回路的闭合
- 范围：全部 15 个顶层 `.cs`（约 15,000 行），只读审查，未改任何代码
- 分工：8 个模块级审查员并行 + 汇总者复核全部 P0 证据行号

---

## 1. 总评

**回路拓扑是"动作段极强、收敛段极弱"。** 这个项目把"发命令"写得很好：壁纸写入严格留在后台线程、下载先写 `.part` 再校验再原子改名、DPI 只有一条缩放路径、主题三处机械对齐、三语字典 219 键完全一致——这些都实测成立。

但它有一个贯穿全项目的结构性缺陷：**每条回路都在执行器侧写了代码，却在传感器侧留了空**。返回值不检查、失败不上报、产物不自检，于是所有失稳点收敛成同一个用户症状：**"我明明看到它成了，它其实没成"**——而这个项目唯一的兜底观测器（`Log.Write`）恰好也被设计成静默吞错。

失稳点分布（按模块）：

| 模块 | P0 | P1 | 核心定性 |
|---|---|---|---|
| 壁纸引擎 | 1 | 3 | 生效判定用了一个与调用毫无因果关系的旁证，四处 fail-open |
| 配置持久化 | 1 | 4 | 写盘非原子 + 全路径吞错，失败仍宣布成功 |
| 入口/诊断 | 2 | 4 | 隔离靠手写枚举，漏一处就改真机 |
| 轮换调度 | 0 | 6 | 记账（Prev 不写 last_applied）与回收（位图/栈/timer）无回路 |
| 自绘控件库 | 0 | 2 | "改了会影响布局的量"没有失效通道 |
| 选择器/文案 | 0 | 4 | 文件系统边界把失败伪装成默认值 |
| 自动更新 | 0 | 2 | "结果已经有了，却没有人记账" |
| 主窗构造/DPI | 0 | 2 | 尺寸由内容决定这一类传感器开环 |

**最高优先级建议**：先修 P0-1（Apply 的 return true），因为它是所有"换壁纸没反应"问题的共同根源，并且它的假成功会顺着闭合良好的下游回路（状态栏、日志、历史栈、`last_applied=`）被永久固化。

---

## 2. 系统级回路闭合性地图

| # | 回路 | 传感器 | 控制器 | 执行器 | 闭合 | 断点 |
|---|---|---|---|---|---|---|
| 1 | 设壁纸→确认生效 | `TranscodedWallpaper` 的 mtime 新鲜度 | `Apply` | SPI / COM | **否** | 判据与本次调用无因果绑定，异常全部放行 |
| 2 | 应用→UI 收尾 | `bool ok` | `ApplyOnBackground` | `SafeUi(FinishApply)` | 是 | 承接的 `ok` 已失真 |
| 3 | 壁纸落地→续走记录 | `FinishApply`/历史跳转 | `RememberApplied` | `Config.Save` | **否** | "上一张"路径漏写，重启续走偏位 |
| 4 | 设置持久化 | 无（`Save` 无返回值） | — | `File.WriteAllText` | **否** | 先截断后写，失败不可逆 |
| 5 | 配置回读 | `catch{}` | `Load` | 静态字段 | **否** | 零字节与残缺都读成默认值，随后被回写固化 |
| 6 | 自启登记 | 仅启动时读一次 | 复选框 | `.lnk` 创建 | **否** | 点击后不复核，开关与真实状态可背离 |
| 7 | 热键注册 | `Set` 返回的问题串 | `ApplyHotkey` | `RegisterHotKey` | **否** | 先注销再校验，失败无回滚 |
| 8 | 扫描→池子 | `Scan` 返回值 | `PickNext` | 文件枚举 | 部分 | 无环防护、无深度预算、仅源计数有代际 |
| 9 | 缩略图生成 | `File.Exists` | `Get/Generate` | `Bitmap.Save` | **否** | 半成品 PNG 永久命中，无自检 |
| 10 | 文案变化→布局 | 无 | `ApplyTexts` | 仅 `Invalidate` | **否** | note 换行改变 header 高度，无人重排 |
| 11 | DPI 迁移→重排 | `DeviceDpi` | `SyncToCurrentDpi` | `BeginInvoke` | 基本闭合 | 回调只投一次，句柄重建无补投 |
| 12 | 切页→重排 | `Visible` | `ShowPage` | `RefreshLayout` | 是 | 顺序正确，与 ContentHeight 硬绑定 |
| 13 | 主题/字体 | `Current` | `ApplyTo` + 缓存 | `ApplyTheme` | 是 | 48 色三处实测零遗漏；DPI 已烘进字体键 |
| 14 | 更新检查 | `QueryLatest` 返回值 | `StartUpdateCheck` | `HttpClient` | **否** | 读响应体无超时，`Checking` 无出口 |
| 15 | 下载校验发布 | `Content-Length`/SHA256 | 镜像循环 | `.part`→`File.Move` | 是 | 四出口对称清理，双闸门 fail-closed |
| 16 | 诊断开关隔离 | `DiagnosticRun` | `Main` | 构造 `MainForm` | **否** | 只守 2 个副作用，换壁纸/写 ini 在守卫外 |
| 17 | 静默拒绝 | 无 | `if (busy) return` | — | **否** | 被拒输入零输出，用户分不清"忙"与"坏" |
| 18 | 异常兜底 | `ThreadException`/`UnhandledException` | — | `Log.Write` | 部分 | 后台异常仍静默退进程 |

---

## 3. P0 — 失稳 / 数据错 / 契约破坏

### P0-1 · `Apply` 有四处 fail-open，返回 true 只等于"没抛异常"

**位置**：`WallpaperEngine.cs:114-121`、`:168-175`、`:177-196`、`:331-348`

```csharp
// :114-121 —— COM 不可用时只发命令，不验结果
IDesktopWallpaper dw = CreateDesktopWallpaper();
if (dw == null)
{
    FallbackApply(imagePath, style, trace, sw);
    Mark(trace, sw, "fallback");
    Finish(trace);
    return true;                    // 无人验结果
}
```
```csharp
// :331-348 —— 唯一的"传感器"，判据与本次调用无因果绑定
if (!System.IO.File.Exists(tp)) return true;   // 没有可比对的东西 → 判成功
DateTime stamp = System.IO.File.GetLastWriteTimeUtc(tp);
return stamp >= DateTime.UtcNow.AddSeconds(-8);   // 只要 8 秒内有人写过就成功
```
```csharp
// :177-196 —— COM 抛异常后走回退，仍无条件成功
catch (Exception ex)
{
    Mark(trace, sw, "EX:" + ex.GetType().Name + ...);
    try { FallbackApply(...); Finish(trace); return true; }
    catch (Exception ex2) { ... return false; }
}
```

**控制论定性**：开环控制（发命令不验结果）+ 观测失效伪装成成功。`DesktopMatches` 的 `imagePath` 参数全方法未使用——注释 :329 声称"comparing its hash before and after"，代码里不存在。异常分支 `return true`、`:338` 文件缺失 `return true`、`:346` 异常 `return true`。

**触发场景**：
1. 用户当前壁纸是纯色 → 无 `TranscodedWallpaper` → `:338` 直接判成功，SPI 是否真的换了永远不知道；
2. 源里混入 shell 解码器不支持的格式（白名单含 `tif`/`webp`）→ SPI 静默失败，但 8 秒内系统里可能恰好有别的写入 → 判成功；
3. 组策略裁剪 COM 注册 → `dw == null` → 只发命令就报成功。

**用户可见后果**：桌面根本没变，但状态栏显示"当前：xxx.jpg"、日志写 `applied:`、`last_applied=` 落盘、历史栈入栈。下次重启"接着上次"会接到一张**从没显示过的图**。`/apply` 的对外契约同时被破坏（见 P0-2）。

**修复方向**：调用前取 mtime 快照，判据改成"本次调用后 mtime 前进"；三处 `return true` 后面各补一次真实校验，不过就 `return false`。

---

### P0-2 · `/apply` 传"存在但不是图片"的文件时退出码是 0（对外契约失真）

**位置**：`Program.cs:1755-1777`

```csharp
else if (File.Exists(target))
{
    path = target;                  // 只验存在，不验是不是图片
}
...
bool ok = WallpaperEngine.Apply(path, Config.Style);
if (ok) Log.Write("silent apply: " + path);
return ok ? 0 : 1;
```

**控制论定性**：传感器本身不可信。文件夹分支走 `ImageScanner` 的魔数白名单（`:1751`），**单文件分支什么都不验**；再叠加 P0-1 的 fail-open，于是"应用失败"和"应用成功"在出口上不可区分。

**触发场景**：`WallpaperChanger.exe /apply C:\...\说明.txt`（计划任务/第三方脚本最常见的误用）。

**用户可见后果**：调用方拿到 0，认为壁纸已换；桌面没变。契约 `0/1/2/3` 里的 `1` 对最常见的错误输入永远走不到。

**修复方向**：单文件分支补一次魔数校验，不通过返回 2；`Apply` 的回退路径把真实结果作为返回值。

---

### P0-3 · `Config.Save` 先截断后写，失败不可逆地丢失全部设置

**位置**：`Config.cs:365`、`:367-369`

```csharp
File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
}
catch
{
}
```

**控制论定性**：对无法保证原子性的动作做盲目覆盖，并丢掉全部反馈信号。`File.WriteAllText` 内部以 `FileMode.Create` **先把正本截断为 0 字节**再写——截断成功、写入失败的那一刻，原配置已销毁。全项目实测**没有任何一处** `File.Replace`、`.bak` 或 `.tmp`（grep 确认）。

**实测体量**：本机正本 24,224 字节 / 404 行，含 2 个 `folder=`、**384 行 `picked=`**、`hotkey=`、`last_applied=`。一次写失败 = 384 个勾选全丢。

**触发场景**：① 磁盘空间不足；② 写盘瞬间进程被结束（托盘退出 / 更新程序 `/CLOSEAPPLICATIONS` / 杀毒）；③ 数据目录被组策略改只读。

**用户可见后果**：所有来源、勾选、热键、语言、顺序模式回到出厂默认，`last_applied=` 丢失。而页脚当时提示的是"已保存"（`MainForm.cs:1589` 无条件 `SetStatus(Loc.T("status.saved"))`），`dirty` 也被置 false。

**修复方向**：写 `*.tmp` → Flush → `File.Replace`（留 `.bak`）；`Save()` 返回 bool，失败时页脚报错并保持 `dirty`。

---

### P0-4 · 窗口类诊断开关改掉用户真壁纸并重写真实 ini

**位置**：`Program.cs:1581-1585`（同类：`:387` `/layout`、`:437` `/ddtest`、`:547` `/probe`、`:621` `/keytest`、`:714` `/previewtest`、`:1654` `/dpitest`）

```csharp
MainForm form = new MainForm();     // 无临时目录隔离、无清空 Folders
form.StartPosition = FormStartPosition.Manual;
form.Location = new Point(target.Bounds.X + 60, target.Bounds.Y + 60);
form.Show();
```

配套的强副作用在构造函数里（`MainForm.cs:312-315` 与 `:269`）：

```csharp
if (HasValidFolders() && !CurrentWallpaperInSource())
{
    NextWallpaper();                 // 真的换壁纸
}
...
SaveFromUi();                        // 无条件把 UI 状态写回 ini
```

**控制论定性**：设定值（"只是拍张图/量一次布局"）与执行器实际动作不一致。`Program.DiagnosticRun` 守卫只覆盖了 2 个副作用（`MainForm.cs:290` 的 `SetAutoStart`、`:321` 的 `StartUpdateAutoCheck`），换壁纸与写 ini 这两个更强的执行器完全开环。

**对照证据**：`/updatewindow`（`:2032-2033`）和 `/langtest`（`:1360-1365`）**做对了**——先 `AppPaths.DataDirOverride = dir` 再 `Config.Folders = new List<string>()`。7 个窗口类开关没照抄。

**触发场景**：装好版（ini 在 `%LocalAppData%`）→ 跑 `WallpaperChanger.exe /dpiwindow 144 test.png`。

**用户可见后果**：只想截图检查 150% 布局，桌面壁纸被换掉；真实 ini 被 `SaveFromUi()` 重写（未知键、手写注释丢失，手改的 `interval_minutes=7` 会被归一到 10）。

**修复方向**：7 个入口统一 `DataDirOverride = 临时目录` + `Config.Folders = new List<string>()`（照抄 `/langtest`）；更好的做法是在 `Main` 里把所有窗口类诊断开关归入同一处守卫，不再靠每个函数自觉。

---

### P0-5 · `/updatedownload <已有目录>` 递归删除用户整个目录

**位置**：`Program.cs:1963-1973`（参数解析）、`:2008-2011`

```csharp
else if (dir == null) dir = args[i];      // 用户传什么就认什么
...
if (!keep)
{
    try { System.IO.Directory.Delete(dir, true); } catch { }   // 递归删除
}
```

**控制论定性**：执行器分不清"自己建的临时目录"与"用户交来的目录"，收尾动作作用在错误对象上。`catch{}` 让失败也无任何提示。

**触发场景**：`WallpaperChanger.exe /updatedownload D:\Downloads`——参数说明写的是 `[dir]`，任何人第一次试都会传一个已有目录。

**用户可见后果**：`D:\Downloads` 连同里面的无关文件被递归删除。这是本次审查范围内最硬的数据丢失路径。

**修复方向**：只有"本次自动创建的临时目录"才自动删；用户传入的目录一律保留并打印"文件留在 <path>"。

---

### P0-6 · 禁用的源会静默丢掉全部已勾选壁纸（且界面承诺相反）

**位置**：`ManualPickerForm.cs:389`（扫描只取启用源）、`:441-451`（勾选重建）、`:908-916`（保存）

```csharp
List<string> folders = Config.EnabledFolders();   // :389 只扫"启用"的源
...
picked.Clear();                                   // :441
foreach (string p in allPaths)                    // :448 只认本次扫到的
    if (saved.Contains(Normalize(p))) picked.Add(Normalize(p));
...
foreach (string p in allPaths)                    // :911 Save() 同样只看 allPaths
    if (picked.Contains(Normalize(p))) picks.Add(p);
Config.ManualPicked = picks;  Config.Save();      // :915 缺失即视为"用户取消勾选"
```

而界面文案（`Loc.cs:319`）明确承诺：

> 禁用的源会暂时移出轮换，图片与已勾选记录全部保留。

**控制论定性**：设定值被脏传感器覆盖。`picked` 的唯一来源是"本次扫描结果"，而扫描集是 `EnabledFolders()` 的子集——"配置里的旧勾选"这条信息在回路里**没有回流通道**，写盘动作直接把缺失当成取消。

**触发场景**：两个源 A/B 都有勾选 → 在壁纸源页禁用 B（或 U 盘拔掉 / 网络盘掉线）→ 打开选择器，**什么都不改**，点"保存并关闭"。`SaveAndClose`（`:928-933`）不看 `dirty`，必然写盘。

**用户可见后果**：B 的勾选永久消失；重新启用 B 也回不来。界面的承诺被代码违反。

**修复方向**：`Save()` 改为"本次扫到的勾选 ∪ 配置里未被本次扫描覆盖的旧勾选"合并写回。

---

## 4. P1 — 功能失效 / 状态漂移

| ID | 位置 | 定性 | 用户看到什么 |
|---|---|---|---|
| P1-1 | `MainForm.cs:2640-2650` | "上一张"成功分支漏写 `lastApplied`/`RememberApplied`（对比 `:2224-2225`、`:2586-2587` 都写了） | 回退三步后退出重开，"下一张"从退出前那张续走，回退白做 |
| P1-2 | `MainForm.cs:2352` | `NextInOrder` 的 `(i+1) % Count` 在只有一张时恒返回同一张 | 只勾一张图 + 间隔 1 分钟 ⇒ 每分钟重放一次 2-5 秒的 SPI + 全局广播，桌面闪 |
| P1-3 | `MainForm.cs:2293-2295` | `lastApplied` 在 `Apply` **之前**由后台线程改写，失败不回滚 | 顺序模式"偶尔跳一张"，日志里 `apply failed` 与 `applied` 交替 |
| P1-4 | `Updater.cs:104-111` + `MainForm.cs:985` | `ResponseHeadersRead` 后读体不受超时约束，且传 `CancellationToken.None` | 代理只给响应头就断 → 卡片永久"正在检查更新"，按钮全灰，无重试入口 |
| P1-5 | `MainForm.cs:2038/2061/2552/2611` | 四处 `if (busy) return` 裸返回，零提示 | 连点"下一张"无声；断网 UNC 源把 2-5 秒放大成几十秒，像卡死 |
| P1-6 | `UiKit.cs:389-390` | `CardPanel.Note/Title` 的 setter 只 `Invalidate`，而 `HeaderPx` 在读取时实时算 | 切语言后 note 第二行压在下方的版本行上，卡片不长高、末行按钮被圆角裁掉 |
| P1-7 | `UiKit.cs:1134/1586` | 焦点环只在 paint 时读 `Focused`；`SegmentedControl`/`ToggleSwitch`/`WindowBtn` 没有 `OnGotFocus/LostFocus → Invalidate` | 纯键盘 Tab 过去看不到焦点环，动一下鼠标才凭空冒出来 |
| P1-8 | `HotkeyManager.cs:53-59` | 先 `Unregister()` 再校验/注册，失败不回滚；且 `Config.Save()` 已先把坏值写盘 | 设了冲突热键 ⇒ 旧绑定已注销、新绑定失败，页脚闪 8 秒后彻底失去 Ctrl+数字，重启不自愈 |
| P1-9 | `ThumbCache.cs:57/66-69` | 用 `File.Exists` 充当"产物完整"，无自检；`Get` 解码失败只返回 null | 生成期间被杀 ⇒ 半个 PNG 永久命中 ⇒ 某几张瓦片永久空白 |
| P1-10 | `Updater.cs:397-421` | 写盘无上限，`info.AssetSize` 已知却不作预算；只有 30 秒空闲超时 | 被接管的镜像无限推流 ⇒ `.part` 写满磁盘，最后报"sha256 mismatch" |
| P1-11 | `ImageScanner.cs:27-28` | `SearchOption.AllDirectories` 跟随重解析点，无深度预算、无 CT | 源里含 junction/网盘挂载 ⇒ 每次换图前的全量扫描从毫秒涨到分钟，"到点不换图" |
| P1-12 | `MainForm.cs:1165-1169` + `Updater.cs:308-314` | UAC 取消进入 Failed 后重试 ⇒ 无条件从第一个镜像重新下载 66 MB，不复用已校验产物 | 每次点"否"都重等几分钟、重吃流量，进度从 0 开始 |
| P1-13 | `ManualPickerForm.cs:113` | `CancelButton = btnSave`（Esc = 保存并关闭），绕过脏检查 | 误点一张瓦片后按 Esc 想放弃 ⇒ 错误勾选直接落盘 |
| P1-14 | `PickerCanvas.cs:66/287` | 缩略图缓存的解码侧对**全库**排队（声称只渲染可视区），`thumbCache` 无淘汰，`SetThumb` 线性扫 | 2000 张的库打开选择器 ⇒ 整库读盘，内存 0.3-0.7 GB |
| P1-15 | `MainForm.cs:2884-2892` | `CaptureWindowSize` 把"被工作区截断后的尺寸"当"用户选择"锁存；`MinimumSize` 用设计像素当物理像素 | 小屏跑过一次后，换大屏也回不到设计高度，且不可缩放、无重置入口 |
| P1-16 | `Program.cs:1023-1032` + `:1079-1081` | `/pickertest` 的隔离窗口两端漏了：前段在 `DataDirOverride` 生效前写真实 ini，后段复位后又写 `sort=0` | 跑一次检测 ⇒ 用户选的 picker 排序被冲成 0 |

---

## 5. P2 — 品质（择要）

- `MainForm.cs:2685-2697`：通知严重性靠文案子串猜（`"失败"/"无法"/"failed"`…）。`status.novalidfolder` 一个字都不命中 ⇒ 用绿色 `Theme.Ok` 画出"请先添加文件夹"，读起来像成功。
- `MainForm.cs:893-940`：`RenderUpdateCard` 每 150 ms 被调一次，且是"先全关再按状态开"，`Visible` 反复翻转 ⇒ 下载中卡片底部每秒闪约 6 次。
- `MainForm.cs:3284-3286`：3 个 `Timer` 只 Dispose 一个（`uiTick`/`noticeTimer` 漏），`trayMenu` 也漏。
- `MainForm.cs:2485`：`RebuildHistory` 每次新建 5 张 `Bitmap` 从不 Dispose；`PushForward` 无上限（`history` 有 300 上限），而 `RequestMissingThumbs` 对永不显示的项也生成缩略图。
- `MainForm.cs:1926`：`Config.IntervalMinutes * 60000` 无上限，`interval_minutes > 35791` 溢出为负 ⇒ `Timer.Interval` 抛异常 ⇒ 构造期崩溃、进程消失（`Config.cs:242` 只校验 `n > 0`）。
- `UiKit.cs:1395-1399`：`KitDropdown` 的 Enter 分支直接 `open.Close()`，绕过 `CloseList()` 的 `ForgetList` ⇒ `open` 指向已销毁窗体，下次点击只关不弹。
- `UiKit.cs:119-135`：`Gfx.Fit`（专为消除省略号中间硬切而写）**零调用点**，调用方仍用 `Gfx.Ellipsis` ⇒ 长文件名末字被切半截且无"…"。
- `UiKit.cs:2853/1616`：`GapPx/PadPx` 与进度条 `Height = 6` 绕过 `Gfx.S` ⇒ 150% 屏上间距仍 8px（应 12px）。
- `Loc.cs:217/452/687`：`src.summary` 三语**各定义两次**，前者永远是死代码（实测确认 220 条赋值 / 219 个唯一键）。
- `Loc.cs`：219 键中 **55 个（25%）无人引用**（`win.min/max/restore/close`、`main.settings.*`、`src.col.*`、`help.*` 等已删功能的遗留）。
- `Theme.cs:748`：每次切主题 `menu.Renderer = new DarkMenuRenderer()`，旧实例不 Dispose。
- `PickerCanvas.cs:537`：`x = pt.X - spacing` 为负时 C# 整除向零截断 ⇒ 点左上 8px 空白边距会勾中第 0 张（配合 P1-13 直接落盘）。
- `Program.cs:1112` 等：`Console.WriteLine` 在 `WinExe` 里落到无效句柄，断言失败只有退出码，FAIL 行无处可去。
- `Program.cs:356-370`：唤起监听线程 `catch { return; }` ⇒ 一次投递失败就永久退出循环，`ev.Set()` 仍返回成功，于是连 `Process`/`ShowWindow` 回退都不跑 ⇒ 双击图标彻底没反应。
- `Config.cs:210-215`：整行 + 值两端 `Trim()`，尾部带空格的目录名（Windows 合法）往返后指向不存在的路径 ⇒ 该源静默消失。
- `ThumbCache.cs:23-25` + `Helpers.cs:149-158`：缩略图缓存实测 5,509 文件 / 242.5 MB / 2.5 个月（约 95 MB/月，无 TTL 无 LRU）；日志 420 KB，均只进不出。

---

## 6. 系统性规律（比逐条 bug 更有价值）

**规律一：传感器失真会被下游闭合回路固化。**
P0-1 让 `Apply` 把失败报成成功，而下游四段回路（状态栏、日志、历史栈、`last_applied=`）全部闭合良好——它们忠实地记录了一个假事实。**下游越可靠，上游的观测失真被钉得越牢。** 这就是为什么"换壁纸没反应"在这个项目里极难排查：所有证据链都指向"成功了"。

**规律二：执行器写了，传感器留空——这是全项目最统一的失效模式。**
`Config.Save` 不检查写盘结果、`DesktopMatches` 不看返回值、`PersistStyleRegistry` 静默吞错、`SendMessageTimeout` 返回值丢弃、缩略图不自检、`SafeUi` 空 catch、`Log.Write` 吞异常。**八个模块各自独立地犯了同一个错**，说明它不是疏忽而是习惯。

**规律三：对称路径不对称。**
同一动作有多条出口，清理与记账只覆盖其中一条：
- 记账：`FinishApply`/历史跳转都写 `last_applied`，Prev 不写（P1-1）；
- 清理：3 个 Timer 只 Dispose 1 个、`forward` 无上限而 `history` 有；
- 隔离：`/langtest`、`/updatewindow` 做了临时目录隔离，7 个窗口类开关没做（P0-4）；
- 退出码：`/apply` 的 4 条分支里 `1` 永远走不到（P0-2）。

**规律四：隔离靠手写枚举，而不是机制。**
每加一个诊断开关，作者必须记得手写三次：`DiagnosticRun` 守卫、`DataDirOverride` 隔离、`Config.Folders` 清空。17 处构造窗口的调用点中只有 3 处清空了 Folders。**缺的是机制，不是纪律**——这类缺陷会随开关数量线性增长。

**规律五："算不出来"与"本来就该是 0"混为一谈。**
`Math.Max(0, h - barH - footH)`（布局饱和）、`stamp >= UtcNow.AddSeconds(-8)`（宽松判成功）、`File.Exists(cacheFile)`（存在即完整）、`File.Exists(tp) → return true`（无观测即通过）——全部把"无法观测"降级成"状态正常"，于是故障只剩一个用户症状："界面有点怪"。

---

## 7. 值得保持的设计（防下一轮重构改坏）

1. **`.part` → SHA256 → `File.Move` 的原子发布 + 四条出口对称清理**（`Updater.cs:302/314/326/341`）——取消、校验失败、异常、成功都有收尾，`DataDir` 不会被半成品污染。这是全项目最规范的一段。
2. **digest 双重 fail-closed 闸门**（`MainForm.cs:879` + `Updater.cs:289`）——UI 侧拒绝无摘要的 release，数据侧再拒一次。UI 状态机怎么漂移都绕不过摘要求证。`DigestHex` 正确剥离 `sha256:` 前缀、归一大小写、拒绝非 64 位。
3. **壁纸写入绝不回 UI 线程的三出口结构**（`ApplyOnBackground` → 后台 `Apply` → `SafeUi(FinishApply)`，`busy` 在 `finally` 复位）——这是 2-5 秒写入与 UI 响应性能同时成立的原因，别把 `Apply` 挪回 UI 线程。
4. **`ShowPage` 严格"先设 Visible、后 RefreshLayout"**，且延迟重排只排可见页——与 `CardPanel.ContentHeight` 跳过不可见子控件硬绑定。这条注释里写着 2026-10-20 的 80px 空条事故，是活文档。
5. **主题 48 个颜色属性三处机械对齐**（实测：声明 48 / Dark 48 / Light 48，集合逐一比对零遗漏，`Set()` 没有第三条路径）。
6. **字体缓存把 DPI 烘进缓存键**（`"u" + px + "|" + weight`，`px = pt*dpi/96`）——天然免疫"旧 DPI 命中"，比维护一个"DPI 变了要清缓存"的标志位可靠。
7. **`Gfx.S` 单一缩放入口 + 命中测试与绘制共用同一份 `List<Rectangle>`**——这是本项目在 150% 屏上没塌、以及没有"点了没反应"类 bug 的根本原因。
8. **三语字典 219 键三向完全一致**（本次机械实测：三向 diff 全空，`Loc.F` 占位符与实参个数零不匹配）。"三语齐全"这一项今天是真的。
9. **`AppPaths` 的写探测用 `FileOptions.DeleteOnClose`**——探针文件不可能残留，失败后两级兜底。
10. **`countGeneration` 代际丢弃 + `force` 才清计数**——快速连点"刷新"不会让旧扫描覆盖新结果。

---

## 8. 建议修复顺序

1. **P0-1 + P0-2**（Apply 的 fail-open + `/apply` 退出码）——它是所有"没换壁纸"问题的共同根源，且假成功会污染全部下游状态。
2. **P0-4 + P0-5**（诊断开关改真机 + 递归删目录）——零成本、纯数据安全，且 7 个入口照抄已有的 `/langtest` 即可。
3. **P0-3**（写盘原子性）——一次 `.tmp` + `File.Replace` 就消除最硬的配置丢失路径。
4. **P0-6**（禁用的源丢勾选）——这是用户数据丢失，且有界面承诺作为验收判据。
5. **P1-1 / P1-2 / P1-6 / P1-8**（记账、重放、布局失效、热键回滚）——四条都只需几行。

**需要用户决定的**：`HelpForm.cs`（死代码）、55 个死文案、`StyleOptionGrid`/`ThemeToggleButton`（死类）是否删除——按项目 AGENTS.md §7 与 §8 的规矩，删除前先问。

---

## 附：审查方法与已知债务对账

本次审查用 8 个模块级并行审查员，每个都自带完整控制论框架（映射表 + 七维度 + 该模块重点怀疑清单 + 故意的设计清单以防误报），汇总者亲自复核每个 P0 的行号与代码证据。

**与项目 AGENTS.md §7「已知债务」的对账结果**：
- ✅ 确认仍存在：`PickerCanvas` 未实现 `IThemed`、零 `Gfx.S`、字体硬编码 9F；`ThumbCache` 目录硬编码且不跟随便携模式；`ManualPickerForm.sf` 只在 `OnLoad` 取一次；`HelpForm` 零引用（实测 `new HelpForm` 出现 0 次）；`Theme.WindowW/H = 980x668` 与设计稿冲突；`HelpForm` 接线后无危险副作用。
- ⚠️ **文档失实**：§7 称"`ReadBackWallpaper` 的 LPWStr 未释放"——实测 `WallpaperEngine.cs:222-223` 与 `:373-374` 都调了 `Marshal.FreeCoTaskMem`，三条取 monitorID 的路径都释放了。建议订正，否则下一轮会去"修"一个不存在的泄漏。
- ⚠️ **文档高估**：§7 的 COM 不释放债务，实际一次 Apply ≤1 个 RCW 且非句柄型，量级远小于描述。
- 🔍 **文档未记录的新发现**：`CardPanel.HeaderPx` 的失效缺失（P1-6）、焦点环开环（P1-7）、`NextInOrder` 单张池重放（P1-2）、Prev 漏写 `last_applied`（P1-1）。前两条尤其值得补进 §3 铁律——它们是"改了会影响布局/视觉状态的东西必须有失效通道"的实例。
