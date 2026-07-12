# TuneLab 快捷键自定义系统 设计

> 目标：把散落在各 `OnKeyDown` 里的 `e.Match(...)` 分支、与菜单 `SetShortcut` 的**双份硬编码**，
> 收敛成**单一命令注册表**——分发（真正生效的手势）与菜单显示（标注的手势）都从它派生，
> 二者漂移的可能性从结构上消除；并在其上叠一层用户可重绑的 override，落成设置窗「快捷键」页。

## 0. 定位与边界

本系统只处理**命令型按键**：离散的「按下某组合键 → 执行某动作」。明确划在系统**之外**的：

- **操作态修饰键**：拖动过程中按住的 Alt（脱磁吸 / 自由）、Shift（约束移动 / 画范围选区），见
  [PianoScrollViewOperation.OnKeyDownEvent](../TuneLab/UI/MainWindow/Editor/PianoWindow/PianoScrollView/PianoScrollViewOperation.cs)。
  这些是**操作语义的一部分**（按住即改变进行中操作的行为），不是「命令」，重绑没有意义，也不进注册表。
- **鼠标 / 滚轮绑定**、**和弦序列**（`Ctrl+K Ctrl+C` 式两击）、**绑定携带参数（args）**——均 v2，见 §10。

> **判据**：一个按键行为是否是「命令」= 它是不是「一次按下触发一次可命名动作」。
> 是 → 进注册表、可重绑；否（持续按住改变态、或鼠标手势的一部分）→ 不进。

---

## 1. 命令模型

核心类型置于 `TuneLab.GUI.Input`。手势用 Avalonia 原生 `KeyModifiers`（物理修饰：Control/Alt/Shift/Meta 各自
独立），按平台走真实键——详见 §1.2。（注意：全应用通用的折叠枚举
[ModifierKeys](../TuneLab.GUI/GUI/Input/ModifierKeys.cs)（把 Meta 归一到 Ctrl）是**另一回事**、供拖动操作等用，
本快捷键系统**不使用**它。）

```csharp
// 一个手势 = 单键 + 物理修饰位（Avalonia KeyModifiers）。v1 一命令至多一手势。
public readonly record struct KeyBinding(Key Key, KeyModifiers Modifiers = KeyModifiers.None);

public enum KeyScope { Global, Editor, TrackWindow, PianoWindow }

public sealed class KeyCommand
{
    public required string Id;                 // 稳定字符串 id，见 §1.1
    public required Func<string> DisplayName;  // 可翻译（.Tr）
    public required KeyScope Scope;
    public KeyBinding? DefaultGesture;         // null = 无默认手势（脚本命令）
    public required Action Execute;            // 场景差异在闭包内部按实时状态分支（保留"一键多义"UX）
}
```

> **不设外部注入的 KeyContext。** 命令由拥有者（Editor/PianoWindow/MainWindow）注册，`Execute` 闭包
> 捕获拥有者、**直接读实时状态**——「同一键的场景自适应」（Delete 删锚点/删音符、Ctrl+A 按工具全选、
> 复制粘贴按范围选区）在闭包内部分支即可，无需把上下文从外部灌进来（单一消费者逻辑内联，不做 bind 糖）。
> 可用性判定（CanExecute）同理留待有真实消费者（设置页/命令面板禁用态）时再补，v1 不引入。

> **实现注**：`KeyBinding` 与 `Avalonia.Input.KeyBinding` 撞名——同时 `using Avalonia.Input` 的文件用
> `using KeyBinding = TuneLab.GUI.Input.KeyBinding;` 消歧（目标类型化 `new()` 处不受影响）。

### 1.1 命令 id 命名规范

`<域>.<动作>`，全小写点分、动作叶 camelCase（`file.saveAs` / `edit.selectAll` / `transport.play` /
`note.transposeUp`）。**id 是 `Keymap.json` 里的持久契约、一经发布不改**——override 存储、菜单绑定、命令面板(v2)、
预设导入导出(v2) 的唯一锚点。显示名走翻译、可随时改；id 不动。

**核心原则：域 = 命令的「功能身份」，不编码「分发作用域」也不编码「UI 位置」。**

- 分发作用域（Global/Editor/Piano/Track，见 §3）**会变**——`copy` 就从 `piano`/`track` 域合并成了焦点路由的
  `edit.copy`。若当初把作用域写进 id（`piano.copy`），这次合并就得改 id、砸掉用户绑定。故作用域只留在
  `KeyCommand.Scope` 运行期字段，**绝不进 id**。这也是 VSCode(`editor.action.*`)、Blender(`object.delete`) 的做法。
- **两级即够**：`域.动作`，不做二级域嵌套（`a.b.动作`）——目前无需要，拍平最可预测。

**域集合（冻结的顶级 key）**：

| 域 | 含 | 例 |
|---|---|---|
| `file` | 工程生命周期 | new / open / save / saveAs / import / export |
| `edit` | 撤销重做 + 剪贴板通用动词 | undo / redo / copy / cut / paste / delete / selectAll |
| `transport` | 走带控制（DAW 惯用语） | play（切换，display "Play/Pause"）/（future: stop / record / loop） |
| `view` | 编辑器视图 | （future: zoom / 面板开关） |
| `tool` | 编辑工具选择 | note / pitch / anchor / lock / vibrato |
| `note` | 音符级操作 | transposeUp/Down / octaveUp/Down |
| `part` | part 操作 | reopenLast /（future: split / merge …） |
| `track` | 轨道操作 | （future: add / delete / mute …） |
| `app` | 应用/窗口级、及无合适域的全局命令（兜底域） | fullscreen /（future: commandPalette / settings） |

> `pitch` **保留给音高曲线参数**，音符移调归 `note`（避免撞义）。
> 第三方命令用**冒号前缀**与内置 `.` 域区分：脚本 `script:<ScriptName>`（见 §6）；插件（v2 预留）`ext:<包id>.<动作>`。

**分组是显示层、与 id 解耦**：设置页按 id 顶级域派生分组（域即组），但**组标签/排序/是否再分子组发布后都可改**
（[KeymapSettingsPage.DomainOrder](../TuneLab/UI/Settings/KeymapSettingsPage.cs) 里的映射，非持久）——只有域 key 冻结。

### 1.2 跨平台修饰键：按平台走真实物理键

跨平台范围：**仅 Windows + Mac**（不考虑 Linux）。

**流派定位：按平台存真实物理键（VSCode / JetBrains / Sublime 流派），而非逻辑折叠（Qt / Electron `CmdOrCtrl`）。**
曾选后者（用一个逻辑 `Ctrl` 同时代表 Win 的 Control 与 Mac 的 Cmd），但它有个**定义性缺陷**：把物理 Control 与
Cmd 折叠成同一个，导致 Mac 的原生组合（凡要区分 ⌘/⌃ 的，如全屏 `⌃⌘F`）**根本表达不出来**。用 Ctrl 冒充 Cmd
终究不安全——不同平台本就是不同的键。故改为**存物理修饰**。

**KeyBinding 用 Avalonia 原生 `KeyModifiers`：`Control / Alt / Shift / Meta` 四位各自独立、不折叠。**
⌘=Meta、⌃=Control 从此可分。分发读原始 `e.KeyModifiers`（滤到这四位）、录制存所按物理键、显示按平台出符号
（Mac `⌃⌥⇧⌘`、Windows `Ctrl+/Alt+/Shift+/Win+`）。

**便利别名 `KeyBinding.PrimaryModifier`（Mac=Meta、Win=Control）避免到处 per-platform 分叉。**
95% 的命令（复制/保存…）默认手势仍是一行 `new(Key.C, KeyBinding.PrimaryModifier)`——自动出 ⌘C / Ctrl+C；
只有真要区分 ⌘/⌃ 的才显式写物理修饰（如全屏 Mac 默认 `new(Key.F, KeyModifiers.Control | KeyModifiers.Meta)`）。
这就是 VSCode 的 `key` + `mac` 覆盖的实质，且更省事——绝大多数键靠 PrimaryModifier 免去分叉。

- **默认按平台**：注册在 C# 代码里，直接按平台算；`PrimaryModifier` 覆盖常见键，个别命令显式分叉
  （见 [MainWindow](../TuneLab/UI/MainWindow/MainWindow.axaml.cs) 的 `app.fullscreen`：Win=F11、Mac=⌃⌘F）。
- **存储按平台**：`Keymap.json` 存 `KeyCodec` 令牌（Mac 用户 ⌘ 存 `cmd`、Win 用户存 `ctrl`；令牌 ↔ 物理修饰见 §1.3）。
- **`Alt` = Mac 的 Option**：Avalonia 把 Option 报成 `KeyModifiers.Alt`，原样透传，零特判。

> **代价（可接受）**：放弃「同一份 `Keymap.json` 跨平台可移植」——Mac 存 `cmd`、Win 存 `ctrl`，直接互换语义不同。
> 但 TuneLab 配置本就在各平台自己的 AppData 目录、同一用户极少跨系统共享 keymap，**不是真需求**。
> 换来的是**忠实表达每个平台的原生快捷键**（含 ⌘/⌃ 之分），对要上 Mac 的 DAW 值得。
> Windows 侧行为零变化（PrimaryModifier=Control，与之前逻辑折叠在 Win 上等价）。

### 1.3 键名标准：自有存储令牌 + 独立显示符号（不依赖 Avalonia 枚举名）

存储与显示**都不得依赖 Avalonia 的 `Key.ToString()`**（那把「存储名」和「显示文本」绑死成同一个英文枚举名——
菜单里方向键显示 "Up" 而非 ↑ 正是此弊）。改由 [KeyCodec](../TuneLab.GUI/GUI/Input/KeyCodec.cs) 统管**两张独立的表**：

- **存储令牌**（我们拥有、稳定、简洁；对齐 W3C UI Events `code` 的精简版）：字母 `a`–`z`、数字 `0`–`9`、
  功能键 `f1`–`f24`、方向 `up`/`down`/`left`/`right`、具名 `space`/`enter`/`tab`/`esc`/`backspace`/`delete`/
  `home`/`end`/`pageup`/`pagedown`/`insert`、标点 `comma`/`period`/`slash`/`minus`/`equal`/`bracketleft`…、
  小键盘 `num0`–`num9`/`numadd`…；修饰 `ctrl`/`alt`/`shift`/`cmd`（`cmd`=Meta）。整条手势序列化成单字符串
  `"ctrl+shift+a"`（修饰规范序 `ctrl+alt+shift+cmd`）。
- **显示符号**（Apple 字形约定）：方向 `↑↓←→`、修饰 Mac `⌃⌥⇧⌘` / Win `Ctrl+/Alt+/Shift+`、Mac 具名键
  `⏎⇥⎋⌫⌦⇞⇟␣` 等。
- **收录即边界**：不在令牌表内的冷僻键**不可绑**（录制侧拦截），绝不回退到 Avalonia 名——存储契约不外泄框架细节。
- 底层参考：W3C 「UI Events KeyboardEvent **code** Values」（物理键、布局无关，源自 USB HID Usage Tables）与
  **key** Values（逻辑值）；Avalonia 11 亦有对应 `code` 的 `PhysicalKey` 枚举，可作将来「按物理位绑定」的桥。

> **菜单显示 ↑ 的收尾（待办）**：设置页手势 chip 已走 `KeyCodec.ToDisplay`（方向键显示 ↑）。但主菜单项的手势文本
> 目前仍由 Avalonia 用 `KeyGesture.ToString()` 渲染（英文名）。要让菜单也出符号，需让菜单手势文本改由 `ToDisplay`
> 供给（Avalonia `MenuItem` 无独立 gesture-text 属性，得把手势并入 Header 自绘或定制模板）——列为后续小项。

---

## 2. 拆命令 还是 内部分支：判定规则

同一按键在不同场景做不同事，有两种落法。选哪种**只看「用户是否想分别重绑各变体」，不看实现上有没有分支**：

- **用户想逐个重绑变体 → 拆成多命令**（v1 不引入 args，故用多命令覆盖）。
  - 工具切换：`tool.note` / `tool.pitch` / `tool.anchor` / `tool.lock` / `tool.vibrato` 各成一条
    （默认 `D1..D5`）。用户会想单独改「切到画笔」的键。（域是功能身份 `tool`，与其在 Editor 域分发无关，见 §1.1。）
  - 移调：`note.transposeUp` / `note.transposeDown` / `note.octaveUp` / `note.octaveDown` 各一条。

- **用户想「一个键、含义随场景自动切」 → 单命令、闭包内部按实时状态分支**。
  - 剪贴板类动词（复制/剪切/粘贴/删除/全选）是**通用动作**：编排区与钢琴窗**共享同一个键**（`edit.copy` /
    `edit.cut` / `edit.paste` / `edit.delete` / `edit.selectAll`），注册在 **Editor 域**、由 `Editor.RouteEdit`
    按当前**键盘焦点**路由到 `PianoWindow.*Selection()` 或 `TrackWindow.*Selection()`。两面为兄弟节点、焦点至多落其一，
    不歧义；各面方法自带「操作进行中」守卫（原本在各自 OnKeyDown 前置，路由后下沉到方法内）。
  - 每个面内部再按场景细分：钢琴窗 `DeleteSelection` 按 Anchor 工具 + 悬停删锚点/否则删所选；`SelectAllInPiano`
    按当前工具全选 Note/Vibrato/Anchor；编排区 `*Selection` 按有无范围选区走选区闸刀/整块 part。
  - **为何不放 Global**：Global = 与上下文无关、任何时候都触发（如全屏）；剪贴板动词必须作用在当前聚焦的编辑面，
    侧栏文本框/对话框里不该触发——故归 Editor 域、按焦点路由，而非 Global。

> 一句话：**变体是「不同动作」就拆命令；变体是「同一动作的场景自适应」就单命令**——后者若跨窗口，则在 Editor 域
> 按焦点路由到各窗口的处理方法（如剪贴板动词），窗口内再按场景分支。

---

## 3. 作用域与分发：scope-stack，借 Avalonia 冒泡实现

不引入中央栈，也不引入 VSCode 式 `when` 表达式引擎。做法：

- 每个能收键的控件（PianoWindow、TrackWindow、Editor、MainWindow）在自己的 `OnKeyDown` 里只做一件事：
  `e.Handled = Keymap.TryHandle(本控件的 KeyScope, e)`（命中即置 Handled）。
- 内层控件（PianoWindow / TrackWindow）在视觉树里先收到键；未命中自然冒泡到外层（Editor → MainWindow/Global）。
  **内层优先**由 Avalonia 冒泡顺序天然给出，无需显式优先级栈。
- 文本框吞键：保留现有 [IsHandledByTextBox](../TuneLab.GUI/Extensions.cs#L153) 前置守卫——聚焦文本
  输入（含 AvaloniaEdit 代码框）时全局命令让路。等价于「文本作用域」吸收一切文本键，置于最内层。

`Global` scope 的命令（如 `app.fullscreen`）由最外层控件（MainWindow /
Editor）兜底分发。

> 这是 Blender 的「按编辑器区域分层」/ Emacs 的「keymap 栈」同族方案——对 DAW 场景成熟、比 when-DSL 轻，
> 且与 TuneLab 现有「各控件各自 OnKeyDown + 事件冒泡」的架构零摩擦。

---

## 4. 注册表 `Keymap`

```csharp
internal static class Keymap
{
    // 注册 / 注销：内置命令在启动期一次性 Register；脚本命令随脚本库变动动态增删（§6）。
    public static void Register(KeyCommand command);
    public static void Unregister(string id);

    // 生效手势 = 用户 override（若有）否则默认。
    public static KeyBinding? Effective(string id);

    // 分发：在 scope 下按 e 的手势找命中命令，过 CanExecute(ctx) 后 Execute(ctx)。命中返回 true。
    public static bool TryHandle(KeyScope scope, KeyEventArgs e, KeyContext ctx);

    // 重绑 / 解绑（gesture==null 解绑）；即时落盘（§7）。触发 Changed 事件供菜单刷新（§5）。
    public static void Rebind(string id, KeyBinding? gesture);
    public static void ResetToDefault(string id);   // 移除 override
    public static void ResetAll();

    // 同一 scope 内手势撞车检测（供 UI 冲突高亮，§9）。
    public static IReadOnlyList<(string id, KeyBinding gesture)> ConflictsOf(string id);

    public static event Action? Changed;             // 任何 override 变更后触发
    public static IReadOnlyCollection<KeyCommand> Commands { get; }
}
```

分发内部维护 `(scope, gesture) → command` 的查找索引，随 Register / Rebind / override 加载增量重建。

---

## 5. 菜单统一：杀双声明

[Extensions.cs](../TuneLab.GUI/Extensions.cs#L110) 现有 `SetShortcut(Key, Mods)` / `SetInputGesture(Key, Mods)`
把手势硬编码进菜单，与 `OnKeyDown` 里另一份硬编码天然会漂移。改为：

```csharp
public static MenuItem SetCommand(this MenuItem item, string commandId);
```

- 菜单**显示手势**实时取 `Keymap.Effective(commandId)`（无绑定则不显示手势）。
- 订阅 `Keymap.Changed`：用户重绑后菜单标注自动跟随。
- 菜单项的 `Action` 仍可直接调命令的 `Execute`，或保留原 Action——关键约束是**显示手势不再由菜单自己
  写死**，而是与分发共用 `Keymap` 这一真相源。

内置命令清单（迁移起点，全部以「当前手势」作为 `DefaultGesture` 注册，行为零变化）。file.* 现经菜单
HotKey 分发，阶段 1 暂不迁移（留待阶段 2 菜单统一），其余已迁：

| id | 默认手势 | scope | 现落点 |
|---|---|---|---|
| `file.new` | Ctrl+N | Editor | 菜单 |
| `file.open` | Ctrl+O | Editor | 菜单 |
| `file.save` | Ctrl+S | Editor | 菜单 |
| `file.saveAs` | Ctrl+Shift+S | Editor | 菜单 |
| `edit.undo` | Ctrl+Z | Editor | 菜单 + OnKeyDown |
| `edit.redo` | Ctrl+Y | Editor | 菜单 + OnKeyDown |
| `edit.copy` | Ctrl+C | Editor | OnKeyDown（焦点路由到钢琴窗/编排区） |
| `edit.cut` | Ctrl+X | Editor | OnKeyDown（焦点路由） |
| `edit.paste` | Ctrl+V | Editor | OnKeyDown（焦点路由） |
| `edit.delete` | Delete | Editor | OnKeyDown（焦点路由，各面内再按场景分支） |
| `edit.selectAll` | Ctrl+A | Editor | OnKeyDown（焦点路由） |
| `transport.play` | Space | Editor | OnKeyDown |
| `part.reopenLast` | Ctrl+Tab | Editor | OnKeyDown |
| `app.fullscreen` | F11（Win）/ ⌃⌘F（Mac） | Global | MainWindow.OnKeyDown |
| `tool.note` | D1 | Editor | OnKeyDown |
| `tool.pitch` | D2 | Editor | OnKeyDown |
| `tool.anchor` | D3 | Editor | OnKeyDown |
| `tool.lock` | D4 | Editor | OnKeyDown |
| `tool.vibrato` | D5 | Editor | OnKeyDown |
| `note.transposeUp` | Up | PianoWindow | OnKeyDown |
| `note.transposeDown` | Down | PianoWindow | OnKeyDown |
| `note.octaveUp` | Shift+Up | PianoWindow | OnKeyDown |
| `note.octaveDown` | Shift+Down | PianoWindow | OnKeyDown |

（编排区的复制/粘贴/删除/剪切/全选不再是独立的 `track.*` 命令——它们与钢琴窗共享上表的 `edit.*`，见 §2。）

> **阶段 2 范围**：只迁移主菜单栏的 file/edit 项（`SetShortcut` 的 HotKey 双声明就此消除，file.* 改由
> Editor.OnKeyDown 经 Keymap 分发，与 undo/redo 同路径）。右键菜单里那些 `SetInputGesture(Key.C, Ctrl)` 是
> **纯显示提示**（无 HotKey、不造成双分发），且其动作是范围选区专属（`CopyRegion` 等，非 `piano.copy` 的
> Execute），不宜直接套 `SetCommand`（会连动作一起换掉）。作为后续小项：这些右键菜单每次打开即重建，
> 届时把 `InputGesture` 就地取 `Keymap.Effective("piano.copy")`（动作不变）即可让提示跟随重绑，无需订阅。

> **迁移中的一处修正**：原 Editor.OnKeyDown 把 `D1..D6` 都当工具键，但只有 5 个工具（Note/Pitch/Anchor/
> Lock/Vibrato），`D6` 会把工具设成越界枚举值（隐性 bug）。阶段 1 只注册 `D1..D5`，`D6` 自然回落为未处理
> ——这是迁移顺带的修正，非行为回归。

---

## 6. 脚本命令（v1 纳入）

脚本工具由 [ScriptToolMenu](../TuneLab/UI/MainWindow/Editor/ScriptToolMenu.cs) **动态发现**（脚本库随时增删），
每个工具有稳定 `ScriptName` 与 `Context`（Global / Note / Part / PartContent / Track / TrackContent）。纳入方式：

- **id** = `script:<ScriptName>`；**无 `DefaultGesture`**（脚本不预设手势，由用户在设置页分配）。
- **scope（v1 简化：全部 Editor 域）**：不分 Context，一律注册为 Editor 域命令——编辑器内全局可达，触发时按
  当前 part / 选区**实时运行**（走与右键菜单同一 `Run` 路径，选区/当前 part 由 live 访问器供给）。之所以不按
  Context 分到 PianoWindow / TrackWindow 域：Editor 是外层伞，一处即让所有脚本立即可键绑可分发，无需在每个
  子窗口各判一次「哪些 Context 归我」。空选区等可用性由脚本自身 `main()` 处理（已有原子回退），不设 CanExecute。
- **动态注册**：随 Scripts 菜单重建（`Editor.Rebuild` → `ScriptToolMenu.SyncKeyCommands`；初次建菜单 + 工程就绪 +
  脚本目录文件监视器变更时触发）同步 Register 新增 / Unregister 消失的脚本命令。
- **孤儿 override 静默保留**：`Keymap.json` 里指向当前不存在脚本的绑定，照
  [ParameterPinning](../TuneLab/Configs/ParameterPinning.cs)「缺键即不显示、不清理」的范式静默留着（缺 id 不进
  分发索引、不触发），脚本回来即复活。

> **扩展点（记录、非 v1）**：`getScriptInfo` 未来可声明 `defaultGesture`，让脚本自带推荐手势。
> id 空间与存储已支持，只需读取端补一个字段，不改数据模型。

配套要补一份脚本命令的专属开发说明（脚本如何被识别为可键绑命令、id 由 `ScriptName` 决定、无默认手势），
并入 [script-tools-design.md](script-tools-design.md) 或其 LLM 参考。

---

## 7. 存储 · `Configs/Keymap.json`

- 新增 `PathManager.KeymapFilePath => Path.Combine(ConfigsFolder, "Keymap.json")`。
- **只存 override 差量**（默认手势留在代码里，可随版本演进；用户没改的命令不占存储、自动继承新默认）。
- 即时落盘 + try/catch 容错，照 [ParameterPinning.Save](../TuneLab/Configs/ParameterPinning.cs#L46) 范式。
- 存储归类：既非窗口布局（不进 `EditorState`）、亦非简单标量可调项（不塞 `Settings.json`），
  与 `ParameterPins.json` / `RecentSoundSources.json` 同级——**结构化可调项走独立 JSON + 专属 UI**。

格式：值 = **单字符串手势**（`KeyCodec` 的自有令牌，见 §1.3），或 `null`（显式解绑、覆盖默认）：

```jsonc
{
  "transport.play":         "ctrl+p",     // Windows；Mac 用户此处会是 "cmd+p"
  "tool.note":              "b",
  "note.octaveUp":          "shift+up",
  "script:MyTool":          "ctrl+f5",
  "edit.redo":              null           // 显式解除默认
}
```

解析失败或指向已不存在命令的条目**静默保留**（缺 id 不进分发索引），脚本/命令回归即复活。

---

## 8. UI · 设置窗新增「快捷键」标签页

[KeymapSettingsPage](../TuneLab/UI/Settings/KeymapSettingsPage.cs) 挂进 [SettingsWindow](../TuneLab/UI/Settings/SettingsWindow.axaml.cs)
的一个 tab（对齐「可调项必有设置 UI」）。

- 可搜索的命令列表（`ListView`），按 scope 分组（Global/Editor/Track/Piano Roll），脚本命令单列 "Scripts" 组；
  各组内按显示名排序。搜索匹配显示名或 id。
- 每行：显示名 | 有 override 时的「重置↺」| 有绑定时的「清除✕」| 手势 chip（点击进入录制）。
- **录制**：点 chip → 页级**隧道阶段**抢先捕获下一次按键（先于 Button 空格/回车激活、先于 Window 的 Esc 关窗）；
  纯修饰键忽略续等，`Esc` 取消，chip 失焦亦视为取消。撞车 → 见 §9。落定即 `Keymap.Rebind`（存盘 + 广播 Changed，
  菜单标注随之刷新）。
- 顶部：搜索框 + 「全部重置默认」（带确认弹窗）。

---

## 9. 冲突处理：检测 + 提示（不静默后胜）

因为有 GUI 编辑器，走 JetBrains / Cubase 派而非 VSCode 的「后定义者静默胜」：

- 录制新手势时，若同 `scope` 内已有命令占用该手势（`Keymap.FindConflict`）→ 弹确认，指明占用者：确认则
  **解除原命令绑定**（`Rebind(oldId, null)`）再绑到新命令，取消则放弃。故冲突**当场消解、不残留**——无需事后
  高亮排查。
- 不同 scope 间的相同手势**不算冲突**（内层优先本就是设计意图，`FindConflict` 只在同 scope 内查）。

---

## 10. 业界坐标与 v2 接口预留

对照 VSCode（命令 + `when` DSL + 差量 override + 和弦 + args + 命令面板）、JetBrains（命名 keymap + 动作自决 +
冲突检测 + Find Action）、Blender/Emacs（区域/scope 分层）、Reaper/Cubase（海量 actions + 上下文 + 宏 + 预设导入导出）：

本设计取「命令一等公民 + scope 分层 + 差量 override + GUI 冲突检测」这条被反复验证的主干；**不取** when 表达式
DSL（过重）、纯 JSON 无 GUI（我们要设置页）、Ableton 的控件映射范式（另一类需求，不在本系统）。

下列 v2 特性的接口**现在就在 id 空间 / 存储里留好**，届时只加 UI/读取端，不改数据模型：

- **命令面板**（Find Action）：对动态脚本命令的可发现性收益最大，是脚本纳入 v1 的天然延伸。
- **keymap 预设导入导出**：迁移场景（用户从别的软件来）。稳定 id + 差量存储已天然支持。
- **绑定携带 args**：让一命令参数化服务多绑定（届时工具/移调可回收为「一命令 × 多 args」）。
- **和弦序列**、**多手势 per 命令**、**鼠标绑定**。

---

## 11. 实施顺序（增量、每段可独立验证）

1. **原语 + 注册表 + 分发器 + 存储**：定义 `KeyBinding/KeyScope/KeyContext/KeyCommand/Keymap`；
   把 §5 表中所有内置命令以「当前手势」注册为默认；各 `OnKeyDown` 改走 `Keymap.TryHandle`。
   **行为零变化**——先验证 parity。
2. **菜单统一**：`SetCommand` 落地，菜单显示手势改从 `Keymap.Effective` 取，删双声明。
3. **脚本命令动态注册**：接 `mRebuildScriptsMenu`，孤儿 override 静默保留。
4. **设置窗「快捷键」页**：列表 + 录制 + 冲突提示 + 重置。
5. **文档 + 独立测试文档**：脚本命令专属开发说明；新建独立测试文档，只测本系统受影响范围
   （分发命中/冲突/override 加载与落盘/脚本增删同步/菜单显示跟随），不污染既有基线测试文档。

---

## 12. 测试要点

- **分发 parity**：迁移后每条内置命令的触发与迁移前逐一对齐（尤其 `piano.delete/copy/selectAll` 的内部分支）。
- **作用域优先**：PianoWindow 命中的键不冒泡到 Editor；文本框聚焦时全局命令让路。
- **override 往返**：重绑 → 落盘 → 重启加载生效；解绑（`null`）确实覆盖默认；未改命令继承新默认。
- **冲突**：同 scope 撞车被检出并提示；跨 scope 同手势不误报。
- **脚本同步**：脚本增删后命令随之增删；孤儿 override 保留且脚本回归后复活。
- **菜单跟随**：重绑后相关菜单项显示手势即时更新。
