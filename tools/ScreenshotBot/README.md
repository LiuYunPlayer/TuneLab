# ScreenshotBot —— 用户手册的插图生成器

把**真实的 TuneLab 界面**渲染成 PNG，供 [docs/user-manual.zh-CN.md](../../docs/user-manual.zh-CN.md) 使用；
顺带从运行中的命令注册表导出快捷键表。手册里的插图不是手工截屏，发版前统一重跑一次即可（开发期改了界面不必每次跑，记下哪些图过时了就行）。

```powershell
pwsh tools/ScreenshotBot/shoot.ps1
# 输出：docs/images/manual/*.png  与  docs/generated/keybindings.md

pwsh tools/ScreenshotBot/shoot.ps1 -Out C:\tmp\shots   # 换个输出目录（试拍别污染仓库）
```

脚本依次做三件事：构建 `demo-plugins/` 下的两个文档示例插件 → 构建本工具 → 渲染。
本工程不在 `TuneLab.sln` 里（同 `tools/` 下其它工具），按需构建。

## 它凭什么能自动截图

| 取舍 | 为什么 |
|------|--------|
| **Avalonia headless 窗口系统 + 真 Skia 渲染**（`UseHeadlessDrawing = false`），经 `HeadlessWindowExtensions.CaptureRenderedFrame` 抓帧 | 跑的是真实的 `App` / `MainWindow` / 真实控件与样式，只是不开真窗口：不抢前台、不受屏幕分辨率与 DPI 影响，TuneLab 正开着也能跑，也能进 CI |
| **数据目录整体隔离**：`Sandbox.Prepare` 建临时沙盒，靠 `TUNELAB_DATA_DIR` 让 `PathManager` 改指到它 | 插图不能带上开发机的个人设置、背景图与已装的第三方插件（其名称也不该出现在文档里）；也不会写坏你的 `%APPDATA%\TuneLab` |
| **界面状态用编辑器/数据层 API 摆好**，不模拟点击坐标 | 重跑结果稳定；`ShotPlan` 读得懂、改得动 |
| **标注锚点取控件实测边界**（`Camera.BoundsIn` + 按类型在可视树里找控件） | 没有一处写死像素，控件挪位置了标注跟着走 |
| **样例工程由代码生成**（`DemoProject`） | 歌词、拼音发音、音高曲线、颤音、参数曲线、乐器和声、参考音频每次都一样 |

## 文件

| 文件 | 职责 |
|------|------|
| `Program.cs` | 入口：备沙盒 → 复用宿主的 `Program.InitCoreServices()` / `ConfigureAppCommon()` → 起 headless app → 跑拍摄计划 |
| `Sandbox.cs` | 沙盒数据目录、装文档示例插件、生成演示音频与示例脚本 |
| `DemoProject.cs` | 样例工程（三条轨道：歌声 / 乐器和声 / 参考音频） |
| `ShotPlan.cs` | 逐张拍摄计划：取景、摆状态、裁剪、标注 |
| `Camera.cs` | 渲染 / 裁剪 / 画编号气泡与框 / 落盘；控件定位辅助 |
| `Dump.cs` | 从 `Keymap` 注册表导出快捷键表 |
| `demo-plugins/` | 文档用的示例声源与示例乐器（独立编译，输出到 `demo-plugins/out/`，不入库） |

## 加一张新插图

在 `ShotPlan.RunAsync` 里按现有写法插一段：摆状态 → `await Camera.Settle()` → `camera.Shoot(...)`。

```csharp
tabBar.Restore([SideBarTab.Export]);   // 侧栏可多面并排，插图只开要拍的那一面
await Camera.Settle(14);
camera.Shoot(window, "sidebar-export", crop: B(Camera.Find<SideBar>(window)));
```

几条经验：

- **等画面稳**：headless 的渲染定时器要手动推帧，`Camera.Settle(frames)` 同时把 dispatcher 队列跑空。合成产物（音素、波形）要多等几十帧才回报。
- **裁剪用实测边界**：`B(control)`；钢琴区专用 `PianoArea()`（滚动视图总高减去参数面板，否则会把参数面板一起裁进来）。
- **小目标的编号用 `CalloutAt.OutsideCorner`**：气泡缩小并骑在框角上，不会盖住图标本身（工具按钮就是这么标的）。
- **弹出层拍不到**：右键菜单、扩展详情窗这类 popup 在 desktop 窗口系统下是独立窗口，不在主窗口的帧里。独立 `Window`（设置窗、歌词输入窗）可以直接 `camera.Shoot(thatWindow, ...)`。
- **别让个人信息进画面**：凡是显示路径的地方（导出路径、音频片段标签）都要摆成中性值，见 `ShotPlan` 里 `ExportPath` 与 `Sandbox.PickAudioDir` 的处理。

## 插图里的每个值都必须是真的

最容易犯、也最难被发现的一类错：为了让拍摄顺利，顺手改了某个**会被拍进画面**的值，插图于是印着一个
用户永远看不到、甚至与手册正文自相矛盾的数。审的时候按这三问过一遍：

1. **这个值用户会不会看到？** 沙盒里没有 `Settings.json`，所以设置各页照出的就是出厂默认——这正是手册要的。
   凡是 `Settings.*.Value = ...`，都要问一句"它会不会出现在某张插图里"。
2. **它和手册正文对得上吗？** 手册的设置表列了量程与默认值；插图里的值必须落在其中。
   （反面教材：`AutoSaveInterval = 3600` 超出量程 10–60，滑条顶死在右端。）
3. **它是宿主自己会产生的吗？** 演示内容（歌词、速度、颜色）要走宿主的正常路径，别自创一套。
   轨道颜色就该取 `Style.GetNewColor(序号)`——用户新建轨道拿到的是那几个色，不是插图作者的审美。

有意为之的替换只有两处，因为真值会把开发机的用户名印进插图，且它们都不与手册的任何说法冲突：
**导出路径/文件名**（`ShotPlan`）与**演示音频的存放目录**（`Sandbox.PickAudioDir`，放公共音乐目录）。

## 几个坑

- **别为了"图个清静"去改会被拍进插图的设置**：沙盒里没有 `Settings.json`，各页照出的就是出厂默认——这正是手册要的。曾把 `AutoSaveInterval` 设成 3600，于是「通用」页插图印着 3600（还超出滑条量程 10–60、滑条顶死在右端），与手册表格自相矛盾。
- **`Settings.AutoSaveInterval` 尤其不能设 0**：自动保存的 `DispatcherTimer` 会零间隔空转、把 UI 线程饿死（表现为一张图都拍不出来，也不报错）。
- **退出用 `Environment.Exit`**：走正常关闭会触发音频引擎销毁，此刻 SDL 的设备回调可能已在半路（宿主既有的关停竞态），截图早已落盘，没必要在这上面折腾。

## 对宿主的依赖

工具复用了 `TuneLab` 与 `TuneLab.GUI` 的内部类型（`MainWindow` / `Editor` / 数据层 / `Keymap`），两处 csproj 各有一条
`InternalsVisibleTo: TuneLab.ScreenshotBot`；`PathManager.TuneLabFolder` 认 `TUNELAB_DATA_DIR`。
改这些地方时留意别把工具弄哑。
