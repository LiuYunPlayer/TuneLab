# 命令面 —— 一份末端命令，多个入口（设计）

> **目标形状**：TuneLab 的每个可被外部驱动的动作只实现一次（"末端命令"），
> 上面接多个入口——内置 agent、CLI、MCP server、CI 里的无头进程——都调同一份。
>
> 本文承接 [agent-tools.md](agent-tools.md)（现有 26 个工具及其归属判据）与
> [script-inputs-and-action-surface.md](script-inputs-and-action-surface.md)（统一动作面 / 分级授权）。
> 三者共用一句原则：**SSOT 约束的是执行面，不是入口数**。

---

## 0. 为什么现在做，以及为什么不能"再抄两遍"

`tl` 脚本动作面已经稳了（单一 commit、单一闸门），26 个 agent 工具覆盖了工程编辑、环境感知、
设置/快捷键/路由/扩展设置、探测沙箱。此刻要给外部 agent 开 CLI 和 MCP 两个入口，只有两条路：

- **各自再实现一遍**：三份 handler，必然漂移，且每加一个能力要改三处。
- **提出一层命令面**：末端命令一份，入口是薄适配器。

选后者。代价是一次**对用户不可见的重构**（现有 26 个工具要搬家），收益是此后每个新能力自动出现在
所有入口上。不做这层，CLI 和 MCP 就是工具面原样抄两遍。

**这层不是"给 agent 用的"**：CI 回归测试、批量转换、开发期手工验证都是它的消费者。

---

## 1. 现状盘点（实地核对，不是回忆）

动手前必须知道的既有事实：

| 事实 | 位置 | 对本设计的影响 |
|---|---|---|
| **工具注册表长在 UI 里** | `AgentSideBarContentProvider.SetProject()` 里 `new` 出 26 个工具 | **第一刀就是把它提出来**。CLI/MCP 入口拿不到侧栏，这一刀不做后面全是空谈 |
| **工具结果是给模型读的自然语言** | `IAgentTool.ExecuteAsync` 返回 `string`，如 "The setting x is already 3. Nothing changed." | 命令面要 `{data}`/`{error}`，人类文本由上层渲染。见 §4 |
| **闸门要 UI 回调** | `ToolAuthorization.AuthorizeAsync(request, confirm, ct)`，`confirm == null` 时保守不做 | 闸门要抽成**按入口注入的策略**。见 §5.1 |
| **脚本类工具依赖编辑器态** | `mCurrentPartProvider` / `mQuantizationProvider` / `mSelectionProvider` / `mPianoSelectionProvider` | 外部调进来"当前 part"是什么？见 §5.2 |
| **有一个工具会调模型** | `ListExtensionsTool(SendSideRequestAsync)` —— 补能力位摘要时发旁路请求 | 外部入口没有模型可调，要降级。见 §5.3 |
| **有一个工具是对话内交互** | `AskUserQuestionTool(RequestUserAnswerAsync)` | 它不是末端命令，是**入口能力**。见 §5.4 |
| **已经有一个命名管道** | `App.axaml.cs:130` 开 `NamedPipeServerStream("TuneLab")`，`Program.cs:62` 客户端连它——单实例把命令行参数转发给运行中实例 | bridge **另开一个管道**，不要复用。见 §7 |
| **headless 已有可工作的先例** | `tools/ScreenshotBot/Program.cs` 复用 `TuneLab.Program.InitCoreServices()` + `ConfigureAppCommon()` + `UseHeadless`，数据目录经 `TUNELAB_DATA_DIR` 隔离 | headless 不是新地基，是把它正式化。见 §8 |
| **沙箱已证明无头合成成立** | `SandboxHost` + `PumpableSynchronizationContext`：无头造工程、挂真音源、离线合成、读回真实音素，全程不碰音频设备 | headless 下"跑完整合成再断言"可行 |
| **结果有中央截断** | `AgentRunner.ClampToolResult`（`Settings.AgentMaxToolResultChars`） | 截断是**模型入口的关切**，不属命令面；CLI/CI 不该被截断。见 §4.3 |

---

## 2. 命名裁决：裸名 `Command` 归命令面，撤销栈那族改叫 `*UndoCommand`

"命令"一词此前在本仓指两样东西，都不是这里说的命令：

| 层 | 符号 | 含义 |
|---|---|---|
| 数据层 | `ICommand` / `Command` / `CompositeCommand`（`TuneLab.Hosting.Foundation/Document/`，命名空间 `TuneLab.Foundation`） | 撤销栈里的一次可撤销突变 |
| 输入层 | `KeyCommand`（`TuneLab/Input/KeyCommand.cs`；**issue #150 后已拆成** `EditorAction` + `KeyBindingEntry`） | 可绑快捷键的 UI 动作（带 `Execute` UI 委托） |

`Operation` 也不能用——`AutomationRendererOperation.cs` 等处有几十个 UI 鼠标交互状态机类叫 `*Operation`。

**裁决**：**裸名 `Command*` 归命令面**（`ICommand` / `CommandRegistry` / `CommandResult` /
`CommandArgs` / `CommandContext` / `CommandKind`），数据层那一族**前置改名**。

这不是"为腾地方而牺牲精确性"，而是**顺手修正一个既有的命名不精确**：那个 `ICommand` 的全部内容
就是 `Undo()` / `Redo()` 两个方法——它讲的一直是撤销栈。而"能被入口调用的末端命令"才是 command
在 CLI/MCP 语境下的主流含义。

接口改叫 `IDataCommand`——这是**归队而非另起名**：`Document/` 目录里的类名本来就是 `Data*` 一族
（`IDataList` / `IDataMap` / `IDataObject` / `IDataProperty` / `DataDocument` / `DataObjectList` /
`SortedDataLinkedList`），`ICommand` / `Command` 恰是少数没带前缀的。限定词取**所属层**，
于是三个"命令"按层各就各位，与输入层那族同范式：

| 符号 | 层 | 含义 |
|---|---|---|
| `IDataCommand` | 数据层 | 撤销栈条目：一次可撤销突变 |
| `EditorAction` / `KeyBindingEntry` | 输入层 | 一条**动作**，与引用它的一条可绑条目（issue #150 之前合为 `KeyCommand`） |
| `ICommand` | 宿主入口层 | 末端命令（本文的命令面，占裸名） |

**真冲突的只有 `ICommand` 一个**：`Command` / `CompositeCommand` / `UndoOnlyCommand` /
`RedoOnlyCommand` 与命令面的 `CommandRegistry` / `CommandResult` / `CommandArgs` 都不重名。
故改名只动两个符号——接口，以及跟着接口配对的默认实现（`IDataList`/`DataList` 范式；
留着裸名 `Command` 会让 `new Command(...)` 被误读成命令面的东西）：

| 原名 | 新名 | 说明 |
|---|---|---|
| `ICommand` | `IDataCommand` | 归队 `Document/` 的 `Data*` 命名 |
| `Command` | `DataCommand` | 默认实现：一对 redo/undo 委托，**两支都有动作**（注释点明，与两个 `*OnlyCommand` 对照） |
| `UndoOnlyCommand` / `RedoOnlyCommand` / `CompositeCommand` | 不动 | 不与命令面重名；`Only` / `Composite` 已在承担区分 |

**全族保持 `*Command` 后缀**（`SoundSource.ModifyCommand` 这类外部实现也在其中）——它们都是被
Push 进撤销栈、参与回放的栈条目，后缀反映的就是这个身份。单向那两个当前的用法恰好都是
"插一个方向性通知"（`Push(new UndoOnlyCommand(NotifyRangeModified))`，34 处），但接口契约是
"只在一个方向执行给定动作"，不排除将来出现真正改数据的单向条目——**别按当前采样把它们改叫钩子**。

**改名可行且成本极小**（已实地核对）：

- `TuneLab.Hosting.Foundation` **不在冻结面**——没有 `PublicAPI.*.txt`、没挂 PublicApiAnalyzers，
  只被 TuneLab / TuneLab.GUI / TuneLab.I18N 三个内部项目引用，**没有任何插件引用它**
  （插件引 `TuneLab.SDK` + `TuneLab.Foundation`；legacy 插件有自己独立的 `TuneLab.Base.Data.ICommand`，
  与此无关）。故这次改名**不触发 RS0016/RS0017，也不构成插件 ABI 变更**。
- 引用面：`ICommand` 25 处 / 12 文件，其中 11 个文件就在 `Document/` 自己目录里；
  **`Document/` 之外总共只有 3 行**——`MidiPart.cs:563`、`MidiPart.cs:569`（`new Command(...)`）、
  `SoundSource.cs:149`（`: ICommand`）。`CompositeCommand` 只有 3 处、全内部。

**用户面也叫 command**：CLI 写 `tunelab setting list`、MCP 描述里写 command——与代码同词，
不需要在文档和终端之间做词形转换。落地时在术语表加一行，把三者的分工写清楚。

---

## 3. 命令面契约

### 3.1 一条命令

```csharp
// 一个末端命令：路径 + 参数 schema + 文档 + handler。不含任何入口特有的东西
// （不知道模型、不知道终端、不知道 UI）。
internal interface ICommand
{
    string Path { get; }              // "setting set" —— group + 叶子动词，空格分隔
    CommandKind Kind { get; }     // Read | Edit | Sandbox
    AgentWriteKind? WriteKind { get; }// Edit 时给出，决定闸门维度与卡片文案；Read 恒 null
    string Brief { get; }             // <=60 字，进 MCP 工具描述与 CLI 一行帮助
    string Documentation { get; }     // 完整说明（现有工具的 Description 迁进来）
    string ParametersJsonSchema { get; }

    Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken ct);
}
```

`Kind` 三值而非二值：`Sandbox` 单列，因为 `run_in_sandbox` 既不是只读、也不碰用户数据、
不过闸门——压进 `Edit` 会让所有入口的安全标注说谎。

### 3.2 结果形状

```csharp
// 成功 → Data（结构化事实）；失败 → Error。二者互斥。
internal readonly record struct CommandResult(JsonNode? Data, CommandError? Error);
internal readonly record struct CommandError(string Code, string Message, JsonNode? Details);
```

**渲染不在 handler 里**。每条命令另有一个 `Render(Data) -> string` 产出人类/模型可读文本，
这样同一份措辞同时出现在 CLI stdout 和模型上下文里，不可能分裂。

**唯一例外是 error message**：错误的上下文太杂，模板化会丢信息，handler 直接组合 `Message` 字符串。
现有工具里那些精心调过的引导语（例如 set_setting 那句"no setting with key … Call list_settings to
see the exact keys"）原样保留——它们本来就是给模型看的、实测调过的。
`Message` 本身**不带 `"Error: "` 前缀**：前缀由入口加（agent 入口加，CLI 打 stderr 时不该冗余）。
共用的查找/校验助手同样只给裸 message，故仍留在 agent 侧的那半条命令自己补前缀。

**什么算失败**：判据是"这次调用还有没有产物"，不是"有没有坏事发生"。调用方的用法错误（未知 id、
缺参、写法歧义）→ `Error`；环境的坏消息（某个引擎加载不上、手册没随包、结果超量被截断）→ 仍走
**成功路径**，把它作为事实标进 `Data`、由 `Render` 如实说出——否则 agent 侧会凭空多出一个
`"Error: "` 前缀，等于改了行为。同一件坏事在两种模式下会落到两边：`extension settings` 列清单时
某个扩展声明设置抛错，那是清单里的一格局部事实（其余条目照常列出）→ 成功 + 逐条标注；而单问
那一个扩展时同样的抛错让整条命令没有产物 → `Error`（CLI 也该为此非零退出）。

### 3.3 上下文

```csharp
// 入口注入的一切"环境"。handler 只从这里取，不碰静态单例、不碰 Dispatcher。
internal sealed class CommandContext
{
    public IProject? Project { get; init; }
    public IEditorStateAccess? EditorState { get; init; }    // 当前 part / 量化 / 选区，见 §5.2
    public IAuthorizationPolicy Authorization { get; init; } // 见 §5.1
    public ISideModelAccess? SideModel { get; init; }         // 可选，见 §5.3
    public IUserPrompt? UserPrompt { get; init; }             // 可选，见 §5.4
    public Func<string?> Language { get; init; }
}
```

### 3.4 注册表

`CommandRegistry` 是**宿主级静态注册表**，不依赖 UI、不依赖工程。
命令实例无状态（工程与环境全从 `CommandContext` 走），因此**不需要在工程切换时重建**——
这一点和现状不同：现在 `SetProject()` 每次换工程都要 new 26 个工具。

**单栈红利**：注册表就是唯一真源。CLI 的命令树、MCP 的工具列表、CLI 的 `--help` 全由它自省生成，
三者结构上不可能漂移，不需要额外的一致性测试。

---

## 4. 结果形状怎么迁（最容易低估的工作量）

现有 handler 直接产人类文本。全部改成 `{data}` + renderer 是 ~26 个 handler 的返回值重写。
不建议一次推翻，切法：

### 4.1 read 命令彻底结构化

它们是 CI 断言和 `--json` 最需要的，也最容易切（本来就在拼列表）。
`list_settings` / `list_keybindings` / `list_sound_sources` / `list_effects` / `list_extension_*`
的 `Data` 是数组，字段就是现在文本里那些列。

**一处例外：文档类命令（`docs *`）的 `Data` 以文本为主。** 它们的产物本来就是文本——把 markdown
章节拆成 JSON 不增加任何信息，反而要在 `Render` 里复制一份格式化逻辑、与 `ManualLibrary.BuildToc()`
漂移。故 `Data` 给 `{ text, 元数据 }`（语言、是否 fallback 版、mode、命中与否），只有手册检索的命中
列表结构化——它的源头 `ManualLibrary.Search` 本就是三元组。判据是"这个产物的事实形态是什么"，
不是"read 命令一律拆成字段"。

### 4.2 write 命令的 Data 只回"改了什么"

不需要为 `set_setting` 设计丰满的结构，`{key, oldValue, newValue}` 这一小组就够 CI 断言。
人类文本沿用现有措辞。

### 4.3 截断留在模型入口

`ClampToolResult` 是"防淹没模型上下文"，不是命令面的事。命令面返回完整 `Data`，
**只有 agent 入口**在渲染后 clamp。CLI 和 CI 拿到的永远是全量——否则 `--json` 管进 jq 会拿到截断的坏 JSON。

---

## 5. 四类入口依赖，各自的处置

这四类才是真正决定 scope 的东西。handler 搬家是机械劳动，这四类是设计。

### 5.1 授权闸门 → 按入口注入策略

现在是 `ToolAuthorization` 静态类直读 `Settings.AgentAuthorization` + 一个可空的 UI `confirm` 回调，
`confirm == null` 时保守不做。抽成：

```csharp
internal interface IAuthorizationPolicy
{
    Task<(bool Proceed, string Message)> AuthorizeAsync(AgentAuthorizationRequest request, CancellationToken ct);
}
```

| 入口 | 实现 |
|---|---|
| 侧栏 agent | 现状：读 `Settings.AgentAuthorization`，Confirm 档弹内联卡片 |
| CLI（attach） | `--yes` 全放开 / 默认走 stdin 交互确认（把 `ActionPhrase()` 打给用户）/ `--dry-run` 等价 `ReadOnlyAdvice` |
| MCP | 靠工具 annotation 让客户端自己问（`readOnlyHint` / `destructiveHint`）；服务端侧不再问一遍，因为**批准 UI 在客户端**——服务端再问一遍是双重询问且没有 UI 可用 |
| headless / CI | **必须显式**：`--yes` 或按维度配；没给就拒绝所有 Edit 命令并说明原因（不能静默放开） |

**天花板：经桥进来的一律被 `Settings.AgentAuthorization` 压顶**（`AuthorizationModes.Stricter`）。
上表后两行说的是"这个入口自己想要什么档位"，而实际档位 = 声明 ∧ 用户设定。理由：外部进程不该比用户
给自家侧栏 agent 的权限更大——否则用户把面板设成只读建议时，自家 agent 一个字都改不了，任何本机进程
声明 `auto` 却能随手改工程。压顶之后"桥开着"最坏的后果止于用户设的那一档：默认 Confirm 下，一个无声
连上来的进程只会拿到"需要确认、而这里没法问"，写全部落空。

副作用要在文案上说清：`--yes` 的语义从"照做"变成"在用户允许的范围内尽量做"。宿主在 `hello` 里回它
当前的天花板，CLI 据此在 `--yes` 遇到更严的档位时先说一句实话（不说的话，CI 里只看到"没做"，会去
怀疑命令本身）。**headless 不受天花板约束**：那是调用方自己的进程、自己打开的工程，碰不到用户此刻
开着的会话（它能做的事，直接改文件也能做）。

**与暂缓项的关系**：这件事和"授权按能力分维度"（工程编辑 / 应用配置 / 磁盘文件）是同一件事的两根轴。
本期**只做入口维度**（每入口一个策略），能力维度仍按暂缓处理——但 `IAuthorizationPolicy` 的形状要
容得下它，将来加维度不改调用点（9 个调用点本来就只传 `Kind`）。

### 5.2 编辑器态 → 显式优先，attach 时可继承

`current part` / `quantization` / `selection` / `pianoSelection` 是"侧栏 agent 坐在编辑器里"才有的隐式上下文。

```csharp
internal interface IEditorStateAccess
{
    IMidiPart? CurrentPart { get; }
    IQuantization? Quantization { get; }
    ScriptSelection? Selection { get; }
    ScriptPianoSelection? PianoSelection { get; }
}
```

| 入口 | 实现 |
|---|---|
| 侧栏 agent | 现状：实时访问器（用户切 part 即变） |
| CLI attach | **默认继承运行中实例的真实编辑器态** + 允许命令参数显式覆盖（如 `--part 3`）。理由：用户开着界面让外部 agent 干活时，"当前 part"的自然含义就是他正在看的那个 |
| headless | **没有编辑器态**。依赖它的命令必须显式传参，缺参就报错说明——不能默认成 track 0 part 0，那是在猜 |

### 5.3 调模型的那个工具 → 降级而非禁用

`list_extensions` 会为缺摘要的能力位发一次旁路模型请求（短文档直接用作者原话，长文档才调模型，
按内容哈希缓存）。外部入口没有模型：

- `SideModel == null` 时**缓存命中就用**，未命中就给作者原话/留空并**如实标注**"摘要未生成"。
- 不因此禁用命令——枚举扩展本身是 CLI/CI 最常用的 read 之一。

### 5.4 问用户 → 不是命令，是入口能力

`ask_user_question` 在本轮之内等用户答复。它不改任何状态，纯为 agent 自身决策服务。

**它不进命令树**。改成入口能力：`IUserPrompt?` 在 context 里，
入口声明支不支持，**命令面按能力决定要不要向该入口暴露它**：

| 入口 | 支持 |
|---|---|
| 侧栏 agent | 支持（现状的内联卡片） |
| CLI attach | 可支持（stdin 提问） |
| MCP | 协议有 elicitation，但客户端支持面不齐——**先不做**，MCP 侧不暴露 |
| headless | 不支持，暴露即错误 |

### 5.5 编辑器动作 → 判据下沉，状态随回报走

工程数据那一轴由脚本 API 覆盖，**应用与编辑器自身的状态**（切工具、开合侧栏、播放、缩放、吸附档位…）
从前外部完全够不着。补法见 issue #150，三条要点：

- **穷尽面是另一份注册表**。`ActionRegistry`（`TuneLab/Input/ActionRegistry.cs`）里是"用户够得着的每一件事"；
  快捷键表 `Keymap` 只**引用**其中值得占一个手势的那批。两个集合的判据不同——快捷键是筛选，动作面是穷尽
  ——拿筛选过的集合当穷尽面的真源，结果只会是"要么设置页被几百条没人想绑的东西灌满，要么命令面永远
  停在那二十几条"。
- **可用性判据下沉到动作上**。`EditorAction.Unavailable` 返回"为什么现在跑不动"，`ActionRegistry.Execute`
  先问它再跑。从前这些守卫散在各个 `Execute` 闭包里（没工程就 return、没选中就 return），键盘路径下
  "按了没反应"用户看得见，而外部触发看不见——那就会回报"已执行"而什么都没发生。
- **状态读与触发同源**。`action run` 的回报直接带上执行后的编辑器状态（`editor status` 的同一份数据）：
  动作里有一半是即发即忘的（`transport.play` 是切换，不是"开始播"），没有配套的状态读，这个动作面是瞎的。

`action run` 作为命令恒声明 `CommandKind.Edit`（它可能写，这是唯一诚实的声明），**具体这一次要不要过闸门
由被触发动作的 `ActionKind` 说**：`AppState` 不过（每次播放都弹确认卡片这个面就没法用了）、`ProjectEdit`
与 `Destructive` 各占一档 `WriteKind`，措辞把"撤销栈救得回"与"救不回"分开说。

**按面补注册表时的四条口径**（二期）。动作面是「用户在界面上够得着的事」的**镜像**，故一条一条按入口来，
不按功能想象来：

- **终态优先于切换**。界面上的 toggle 按钮在动作面上分解成**一对终态动作**（侧栏页签 → `sidebar.show*` /
  `sidebar.hide`，播放 → `transport.start` / `transport.pause`），外部驱动因此不必"先读一次现在是什么状态
  才敢按"，两次 show 同一个面与一次的结果相同。反过来，当那个 toggle 本身就是用户心里的**一件事**
  （Space 的播放/暂停、Ctrl+P 的参数面板、F11 的全屏），就保留切换语义——它与按钮、与快捷键是同一件事。
- **闭集逐值收，动态集给选择器参数**。成员固定且有限的（5 支笔、6 个侧栏面、18 档量化）逐值开一条终态
  动作，id 由事实算出而不是抄一张并行的表（`quantization.1_12` = 三连基数 3 × 细分 4）。成员随工程变的
  （参数面板的合成参数轨、参数栏钉选项）**不**逐成员开 id——那些 id 明天就不存在了，"一经发布不改"
  无从保证。它们的形状是**一个动词 + 一个闭集里的成员**：判据是"加一个成员是加一个**值**，不是加一条动作"。
  见下面的 §5.6。
- **可绑是另一层筛选**。18 档量化终态**不进 keymap**，只有「更细 / 更粗」这一对占手势；侧栏、波形带、
  设置窗可绑但**不预占默认键**。判据仍是"值不值得占一个手势"，与"够不够得着"分开（见 `EditorAction`）。
- **只镜像已有的能力**。动作面不是新功能的入口：`AudioEngine` 里没有循环播放，那"循环区间"就不是覆盖率
  的洞、而是功能缺失；视口缩放/滚动与音频导出是**刻意不收**（§11）。

覆盖率清单见 [docs/action-coverage.md](action-coverage.md)：界面上每个入口逐条认领（指向一条动作/命令，
或说清它为什么不该在那儿），并由 `ActionCoverageTests` 钉住——新加一个按钮而没认领就会红，那不是障碍，
是提问「这个入口，外部该不该够得着」。

### 5.6 选择器参数：动作面上唯一的带参形状

`run_action` 收一个可选的 `argument`，被触发动作用 `EditorAction.Parameter`（`ActionParameter`）声明它接什么。
今天有四条：`parameter.showSynthesizedTrack` / `hideSynthesizedTrack`（作用在一条合成参数轨上）与
`parameter.pinProperty` / `unpinProperty`（作用在一个 note / phoneme 属性上）。四条口径：

- **值域必须能自省**。动态成员集意味着调用方不可能预先知道合法值，故 `Values` 是闭包、`list_actions`
  把**此刻的合法值**连同每个值的当下状态（`shown` / `hidden` / `pinned`…）一起报出去（同 `setting list`
  报 allowed values 的理由）。落在集合外的值不跑并回列合法值——猜错在这个面上的表现是静默什么都没发生。
  值太多时截断并如实说是前几个（12 条，同 `setting` 的口径）。
- **值域即判据，不另开逐值的可用性委托**。`EditorAction.Unavailable` 仍只回答"整条动作现在跑不动"
  （没开 part、声明面是空的），而"这个值此刻合不合法"就是值域本身。故既有那批无参动作的签名一个字都没动
  （加性）。真出现"值在集合里、但这一个值还是不能跑"的动作时再加一个接值的判据委托。
- **闸门要点名成员**。授权卡片上"跑一下隐藏合成参数轨"没说清隐藏的是哪一条，故 `WriteKind.EditorAction`
  / `EditorActionDestructive` 的措辞带上那个值的 label（同 `ExtensionActivationChange` 要点名包）。
  今天这四条都是 `AppState`、不过闸门，但机制在那儿——将来带参的 `ProjectEdit` 动作不必回头再补。
- **带必填参数的动作不可绑**。一条绑定只有手势、没有参数，按下去无从知道作用在哪个成员上；v1 刻意不做
  "绑定携带参数"（keybinding-system.md §10 把它留给 v2），故这批动作只从命令面与界面入口触发，
  `list_actions` 里 `bindable` 恒 false。**可选**参数的动作不在此列，见 §5.7。

**已经逐值收了的闭集不回收成带参的一条**（`quantization.*` 那 18 档、`sidebar.show*` 那 6 条）。四条理由：
成员是**静态**闭集，没有"逐成员开 id 会爆"这个问题；那些 id 已经发布，而 id 是 `Keybindings.json` 的键与
菜单绑定的锚点；侧栏那 6 条**可绑**，改成带参就把它们从"可绑"降级成"不可绑"（净损失）；而带参的一条与
逐值的十八条在自省上并无高下——`list_actions` 两种都报得出。判据始终是那一条：**成员会不会随工程变**。

**为什么这不是把视口捞回来的口子**。「滚到某个 tick」要的是一个**连续量**、不是闭集里的一个成员。
选择器参数收的是「界面上本来就摆着一排、成员随工程变」的那类入口，不是任何缺参数的东西的通用入口。
视口定位后来确实做了，但**没有走这条路**：它是一条命令（`editor reveal`，§6.1）——那是这段话的结论
成立的样子，不是它的反例。

---

### 5.7 可选修饰符参数：同一件事，副作用管到哪

四条移调动作（`note.transposeUp` / `transposeDown` / `octaveUp` / `octaveDown`）各带一个**可选**参数
`parameters`（`sync` / `keep`）：移调要不要把音符底下的音高线与自动化曲线一起搬走。它与 §5.6 的选择器
参数是两种东西，判据分得开：

- 选择器参数选的是**作用对象**（哪条合成参数轨、哪个属性），一个动词配一个成员，**必填**——不给值就无从
  知道要作用在哪儿。
- 修饰符参数说的是**同一件事的副作用范围**，**可选**——不给值动作照样跑，走那条缺省行为。

**为什么不按 §5.5 逐值开动作**：修饰符是**乘**上去的，不是加一个成员——四条移调 × 两种副作用 = 八条；
而且这批动作**都带手势**，键盘按下去无从携带参数，缺省行为只能有一条。逐值开八条反而让"按 Shift+↑ 等于
哪一条"变成新的含糊问题。四条口径：

- **缺省行为必须说出来**。`ActionParameter.DefaultBehavior` 必填（注册时断言），`list_actions` 报成
  `default` 字段、文本里是"without a value:"那一行，且**点名是哪个设置**；每个值的 `State` 另报此刻哪个是
  缺省（`current default`），故调用方不必先读一次设置才知道不给值会发生什么。
- **要确定性就显式给值**。外部驱动（CLI / MCP / agent）没有"用户勾了什么"的上下文，同一条命令在两台机器上
  行为不同是不能接受的；给了值就与设置无关。缺省那条路径是留给**手势与界面菜单**的——那里"按我在设置里
  选的来"正是用户的预期。
- **可选参数的动作可以绑手势**。`Keymap.Register` 只拒必填参数的动作，故这四条 `bindable` 仍是 true
  （手势路径不带参数、走 `EditorAction.Execute`）——与 §5.6 那批正相反。
- **加性**。既有动作与那 4 条选择器参数动作的声明一字未改；`ActionRegistry.Register` 的执行路径校验按
  "参数可选与否"分两支：必填 = Execute 与 Parameter.Execute 恰有其一，可选 = 两条都要有。

---

## 6. 命令树与迁移清单

### 6.1 group 划分

9 个 group，noun-first：`project` `script` `docs` `extension` `source` `effect` `setting` `keybinding` `sandbox`
（搬家之后又加了三个：`app`、`editor`、`action`，见下）

**`app` group（搬家之后新加）**：`app info` 报的是**命令跑在哪个装置里**——版本与构建号、用户数据与
日志在哪、有没有编辑器、出了问题去哪说。与 `project` 分开是因为工程是用户的文档，这里说的是那一份安装。
它存在的理由是排障与反馈：外部 agent 查出问题后不该自己去提 issue（结论错了撤不回来），而要把证据交给
用户由他决定——而"你这是哪一版"此前**没有任何命令答得出**。回报里同时给出那份证据清单（版本+构建号 /
日志相关行 / `list_extensions` 输出 / 最小复现脚本），故三个入口看到的是同一份要求。
刻意不含扩展清单、设置、工程信息（那是另外三条命令的事），也不含 legacy 兼容层状态
（`extension list` 已逐包给出 Skipped 与原因）。

**`editor` group（issue #150）**：`editor status` 报**编辑器此刻的界面状态**——在播吗、播到哪、拿着哪支笔、
参数面板开着没、键盘焦点在哪个编辑面、钢琴窗里开着哪个 part。与另外两条刻意不重叠：`app info` 说的是
"命令跑在哪一份安装里"，`project status` 说的是用户的**文档**，这里说的是**会话**（不随工程保存，撤销栈里
也没有它）。它存在的理由是动作面里有一半是即发即忘的，见 §5.5。

`editor status` 还报**此刻有没有东西挡在界面前面**（模态框 / 系统文件选择器），这一条排在回报的最前面。
理由是一个容易被漏掉的事实：模态框期间 Avalonia 跑的是嵌套消息循环，**Dispatcher 照常泵，于是命令桥
照常应答**——外部因此可以在用户屏幕被一个框锁着的时候跑一串命令、条条回报成功，而用户一个字都看不见、
也点不了。那是最难被发现的一种「假装成功」（每一条单独看都没错）。它同时解释了另外两件事：`action run`
的 `allowPrompt` 为什么存在，以及此刻挪过去的视野为什么用户可能没看见。`action run` 的回报与
`editor status` 共用同一份状态文本，故触发完一条动作也会看到这句——那正是它最有用的时刻。

两路来源，因为可见性不同（见 `BlockingUi`）：**自家的模态窗不必登记**，Avalonia 自己数得出
（`Window.IsDialog` 恰好就是"被 ShowDialog 弹出来的"）；**系统文件选择器根本不是 Avalonia 的窗口**，
谁也数不到，只能由调用处声明一段作用域。故有一条宿主纪律：**弹系统选择器一律走
`OpenFilePickerTracked` / `SaveFilePickerTracked`，别直接调 `StorageProvider`**——直接调的那一处，
在它挡着界面的整段时间里状态会报"没东西挡着"。

**只报，不代答**：让外部去点那个框上的按钮是另一件事，判据也不同（界面上的按钮属于某条已经被触发的
动作，不是独立的一件事；何况按钮文案是本地化的，跨语言认不住），故没做。真要做，先做的应该是
「agent 自己捅出来的那个框，让它自己收尾」——那不是替用户做决定，与 `allowPrompt` 是同一条线的延伸。

`editor reveal`（收尾时补的，§11 那条「视口不收」的部分翻案）是它的**写侧对应物**：把用户的视野挪到
你说的那个地方去——你找到了他要的那段，或者你指着某处说这里不对。定位可以是轨、轨里的某个 part、
或一段 tick；编排区与钢琴窗的时间轴一起走（同 `transport.gotoStart`，只挪一条的话用户在另一个窗里
看到的还是原处）。

翻案的边界在 §11：**翻的是定位，不是缩放手势**。手势没有参数、也不指向任何东西，而 reveal 恒要一个
「哪里」——这正是 §11 自己留下的那半句话。缩放只作为后果出现：装不下才缩小，且**永不替用户放大**
（把缩放猛拉到一个音符上，反而让人找不着北）。

**为什么是命令而不是带参动作**：§5.6 自己写着「滚到某个 tick 要的是一个连续量、不是闭集里的一个成员」；
何况带必填参数的动作不可绑手势，把 `ActionParameter` 扩成能接一个数，走完一圈还是只能从命令面调。

**不过闸门**（同 `ActionKind.AppState`）：它只改用户此刻看到什么，不碰工程数据、不动播放头、撤销栈里
也没有它。唯一有后果的是 `open`——把钢琴窗切到目标 part 改的是用户的**编辑目标**（Ctrl+A 选什么、
下一个按键落在哪），故它默认关着。默认路径上不留死角：目标 part 没开时照样滚编排区，并在回报里明说
「钢琴窗现在开着的是 X；要切过去传 open:true」，一个来回就能自纠。回报给的是**挪完之后**实际看得见的
范围，故整段没装下时它读不成「都给你看到了」。

**`action` group（issue #150）**：`action list` / `action run` —— 用户在编辑器窗口里能做的事，外部也够得着。
一条 `action run --id` 而**不是**每个动作一条命令：动作面要穷尽（二期会有几百条），逐动作开命令会立刻
把工具面撑爆，与脚本层收口工具爆炸是同一个道理。

**覆盖率清单补上的两族（issue #150 三期之后）**：

- **`project open`** —— 打开一个工程文件，换掉用户此刻开着的那份。它**只能是命令**：动作面无参，
  `file.open` 只能弹个选择器让人自己挑，而"打开这个路径"要一个参数。三道闸的顺序有意——没有编辑器
  （headless 的工程在启动时就定了）、路径不可用、**有未保存改动就直接拒绝**（早于授权：卡片上写的是
  "打开哪个文件"，用户在那儿点允许并没有同意丢掉自己的活儿）。
- **`extension install` / `uninstall` / `cancel-uninstall`** —— 此前只有 `extension enable`（启停、不动文件）。
  两条宿主硬约束决定了它们的形状：**装可以立刻生效**（解压 + 加载 + 音源引擎急切 Init），而**卸与重装
  必须等重启**（运行中的进程锁着那些 dll），故卸载只是标记、装同名包直接拒绝——重启整个应用是用户的决定，
  命令面不替他做。

**`preset` group（覆盖率清单的最后一个桶）**：`preset list` / `apply` / `save` / `delete` / `rename` ——
Part 面板顶上那一行预设的外部面。一条 part preset 是**声音的快照**（声源 + part 属性 + 各自动化轨的
默认值），刻意不含任何时间轴内容，故"应用一条预设"改的是这个 part 怎么唱，不是它唱什么。
它**只能是命令**（同 `project open` 的判据）：动作面无参，而这一族每件事都要参数——哪条预设、
用在哪个 part、存成什么名字。应用确实写工程数据（那本该归脚本面），但预设的内容在**用户的配置目录里**，
脚本面读不到，故这条通道无可替代——与 `script run-saved` 同一个理由。part 按 1-based 轨号 + part 号定位，
与 `project status` / `editor status` 报的号同一套。

两处**知情差异**（不是漏做，是这个入口没法照抄界面）：`preset rename` 撞上已有的名字**直接拒绝**，
而界面会弹确认框问要不要替换——这里没人可问，而替换掉用户的另一条预设不是能猜着做的事；
`preset save` 只在**替换**已有的那条时过闸门（新建一条不拦，同 `save_script`）。

effect 的实例 / 链 preset 实现暂缓（issue #141）**不构成挡住 part preset 的理由**：三种同住一个
`Presets\` 文件夹、后缀名唯一声明种类，届时给这几条加一个 kind 参数即可（加性），不必回头改形状。

**`project save` / `project save-as`（收尾时补的一族）**：把用户此刻开着的那份文档存回磁盘。

它们**只能是命令**，判据与 `project open` 同一条：动作面无参，`file.save` 只能存回它自己那个路径，
而"存到这个路径"要一个参数。两条命令都不弹任何框——菜单那条 `file.save` 在工程从未保存过时会转去弹
文件选择器，而命令面没人应答那个框，故这里在动手之前就据 `HasSaveTarget` 拒绝并指向 `save_project_as`。

**与 `project export` 的分野是语义，不是路径**。export 写一份**副本**：文件落地，用户的工程照旧指向
老文件、照旧带着未保存标记。save 这两条是**真的保存**：走用户按 Ctrl+S 的同一条下游——改写那个文件、
把工程的保存路径挪过去、清掉未保存标记，撤销栈救不回其中任何一件。

之所以不为命令面另造一份"自己写盘的副本实现"（那看上去更简单、也不必碰 `Editor`），是因为副本会造出
**两份真相**：文件是新的，而用户在标题栏上看到的仍是"未保存"，调用方却以为存过了——那只是把"假装成功"
换了个形式。副本这个语义已经有人承担了，就是 export。

顺带修掉一处旧疾：`Editor.SaveToFile` 从前把序列化失败与写盘失败都咽进日志。对着屏幕的人还能从标题栏的
星号发现没存下，而命令面照搬就会回报"已保存"而文件根本没写成，故它改成返回 `string?`（null=存下了，
否则是为什么没存下）；菜单那条路忽略返回值，行为不变。

headless 里两条都**如实拒绝**并指向 `export_project`：那里没有"用户开着的文档"这个东西
（工程在启动时就由 `--project` 定了，见 `IProjectFileAccess`），而 headless 跑完想留下结果，
要的本来就是 export 那个语义。

**`project export-audio`（收尾时补的一条，§11 那条"音频导出不收"的翻案）**：把工程渲染成一个音频文件。

翻案的理由在 §11，这里只说形状。**它与 `project export` 不是一族**：那条写的是工程文件（.tlpx/.mid…），
这条写的是声音（.wav/.mp3/.flac/.ogg）。格式由扩展名定，采样率/位深/码率取**工程自己的导出设置**
（用户在导出侧栏里设的那一份，脚本面可读可写）——路径是这次调用说了算的东西，其余的是工程的属性。

**真正的难点是"等"，不是"渲染"**。`AudioEngine.ExportMaster` 拉的是 `AudioGraph` 此刻的数据、不等任何人；
界面上的导出之所以行得通，是因为用户看着状态带自己等到全绿才按下去——那个"等"是人做的，不在代码里。
照搬到命令面就会在合成还没跑完时静默产出静音段，而回报仍说"导出成功"。故这条命令自己把合成驱动到落定
（`SynthesisCompletion`），再看链尾上到底是什么：

- **超时**（默认 5 分钟）→ **什么都不写**，如实报还剩多少。半截音频比没有音频更坏：它看起来是成品。
- **有失败段** → 默认**拒绝**并点名坏在哪（那些范围会被导成静音）；要 `allowIncomplete` 才导，且回报里
  仍然逐条列出。
- **有降级段** → 只警告：那是 effect 某级失败后放的未处理音频，能听，只是不是应有的结果。两者后果不同，
  故分开说——混成一句话用户就分不清要不要重做。

**为什么要自己派活**：编辑器里派活的是 `Editor` 那个 50ms 定时器，headless 进程里没有它，不自己派就永远
等不到。故 `SynthesisCompletion.DriveOnce` 做的是与 `SynthesisNext` 同样的事，只是窗口取全轴（要的是
"全都合成完"而不是"播放头附近优先"）；编辑器里与那个定时器并存无害，两者都在数据线程上串行。

一期只导**混音到一个文件**。多轨、选区、分文件是要不要做的问题，不是判据问题——界面上那三个入口
（导出混音 / 单轨导出 / 导出侧栏的按钮）在 action-coverage.md 里已一并改判到这条命令，并注明缺口。

**`project synthesize` / `project synthesis-status`（后来补的两条）**：把合成驱动到落定并等它，以及
只看此刻的状态。

补的是命令面自己的一个**空洞**：脚本面能把合成产物固化成用户数据（`part.lockPitch` /
`lockAutomation` / `note.lockPhonemes`），却没有任何办法让合成**发生**——那三处的文档都在说
"false means there was no synthesis output — usually: not synthesized yet"，而在 headless 里那恒为真。
在这两条之前，跑批脚本唯一能顶起合成的办法是 `export-audio` 导一个根本不要的音频文件（它顺带把合成
驱动到落定），荒谬且昂贵：为了拿音素时间白渲染一遍全曲。

三件事值得记下判据：

- **`synthesize` 只在 headless 可用**，这是命令面上「**有**编辑器 → 不可用」的第一例（`project save` /
  `open` 是反方向：缺了编辑器做不到）。理由不是"危险"，是**写不出一张诚实的授权卡片**：`export-audio`
  在有窗口的进程里能成立，靠的是前面挡着模态框，而锁住界面几分钟的正当性来自"换来一个文件"；这一条
  换不来任何东西。不挡框则更糟——用户边编辑边合成，派活反复作废，很可能永远不 `Done`，最后回报
  `timeout`，而真实原因是用户在动工程，那个结果无从解释。何况有编辑器的进程里合成本来就有主人
  （那个 50ms 定时器），外部再插一手，"谁负责把它跑完"就没人说得清。状态查询没有这个问题
  （纯读、零代价），故那一条两边都给。
- **Kind = Read**。它让引擎跑起来、于是内存里多出合成产物——那与"按一下播放"同级，不是一次编辑：
  不改工程数据、不写盘、不进撤销历史。代价（占住这台机器）写在文档第一句，不靠 kind 去表达。
- **范围过滤把一个变量劈成了两个**。`DriveOnce` 原本用一个 `busy` 同时当派活预算与完成判据；接了
  时间窗 × 轨/part 过滤之后这两件事分家：预算必须数**全工程**的在飞数（`EffectTaskGate.Limit` 是全局
  闸门，只数范围内的话范围外正在跑的活儿看不见，这一轮就会越过上限、资源失控），而 `Done` 只能看
  **范围内**（否则范围外有活儿在跑就永远判不出完成，等到超时为止，而那个超时无从解释）。过滤持有的
  是**对象引用**而非 1-based 编号：编号只活在参数面上，一进来就解析成对象，于是没有哪一层还需要
  重新数一遍"第几个"。

**"落定"不等于"合成好了"**：范围内一个 part 都没有产出时 `Done` 同样为真。那是跑批里最容易被当成成功的
一种失败（音源在这个进程里不可用、或那些 part 根本没有音符），故回报里一并给出"多少个 part 现在有产出"
——这条命令的全部目的就是让产出存在，那它就该回答产出在不在，而不只是"我等完了"。

**故意不塞进普通 group 的三样**（它们和原子命令不是一个物种）：

- `script run` / `script run-saved` —— 图灵完备逃生口，参数是一段 JS
- `sandbox run` —— 探测器，写入不碰用户数据、不过闸门
- `docs *` —— 文档查阅，渐进式披露的出口

### 6.2 逐条迁移

| 现工具 | 命令路径 | Kind | 依赖处置 |
|---|---|---|---|
| `get_project_overview` | `project status` | Read | 纯读 `IProject`，零障碍 |
| `export_project` | `project export` | Edit | 闸门（恒，路径任意）；`ProjectExport(+Overwrite)` |
| `run_script` | `script run` | Edit | 闸门 + 编辑器态；走 `ScriptWriteExecutor` 不变 |
| `run_saved_script` | `script run-saved` | Edit | 同上 + 按名读库 + 入参解析 |
| `list_scripts` | `script list` | Read | 注入 project/编辑器态（为 eval `getScriptInfo`） |
| `read_script` | `script read` | Read | 零障碍 |
| `save_script` | `script save` | Edit | 闸门（仅覆盖已存）；`ScriptOverwrite` |
| `delete_script` | `script delete` | Edit | 闸门（恒）；`ScriptDelete` |
| `get_script_inputs` | `script inputs` | Read | 编辑器态（eval `getInputConfig`） |
| `get_script_api` | `docs script-api` | Read | 纯静态文本，零障碍 |
| `get_manual` | `docs manual` | Read | 纯静态，读 `ManualLibrary` |
| `list_extensions` | `extension list` | Read | **调模型降级**（§5.3） |
| `get_extension_introduction` | `extension introduction` | Read | 零障碍 |
| `list_extension_routing` | `extension routing` | Read | 零障碍 |
| `set_extension_routing` | `extension set-routing` | Edit | 闸门；`RoutingChange`；重启生效 |
| `set_extension_enabled` | `extension enable` | Edit | 闸门；`ExtensionActivationChange`；重启生效 |
| `list_extension_settings` | `extension settings` | Read | 密钥只报有无 |
| `set_extension_setting` | `extension set-setting` | Edit | 闸门；`ExtensionSettingChange`；密钥恒拒 |
| `list_sound_sources` | `source list` | Read | 三层钻取不变 |
| `list_effects` | `effect list` | Read | 分层不变 |
| `list_settings` | `setting list` | Read | 读 `SettingsRegistry` |
| `set_setting` | `setting set` | Edit | 闸门；`SettingChange`；`AgentWritable=false` 三项仍拒 |
| `list_keybindings` | `keybinding list` | Read | 读 `Keymap` |
| `set_keybinding` | `keybinding set` | Edit | 闸门；`KeybindingChange`；冲突在落地那刻重查 |
| `run_in_sandbox` | `sandbox run` | Sandbox | 不过闸门；headless 下天然可用 |
| `ask_user_question` | **不进命令树** | — | 入口能力（§5.4） |

内置 agent 的工具面由注册表**合成**（见 §9），因此这张表不是"工具消失了"，
而是"工具变成了命令的一种投影"。

---

## 7. 传输层

**attach 模式的连法**：宿主起一个 `NamedPipeServerStream`，外部进程连它，
跑 **4 字节长度前缀的 JSON-RPC 2.0**。

几个已定的点：

- **另开管道，不复用 "TuneLab"**。那个管道的语义是"把命令行参数转发给运行中实例"（单实例逻辑），
  载荷是逐行文本；混进 JSON-RPC 会让两个协议共用一个名字。新名字另取。
- **不做 HTTP/端口**。管道天然限本机、天然带 Windows ACL，不需要选端口、不需要处理端口占用，
  也不会被同机其它进程扫到。跨平台上 .NET 的 named pipe 落到 Unix domain socket，语义一致。
- **凭据文件**：宿主把管道名 + 一次性 token 写进用户数据目录下一个文件（`PathManager` 加一项），
  外部进程读它连回来。这样"谁能连"由文件系统权限决定，不需要自己发明认证。
- **设置里有开关**，默认关。开启即写凭据文件，关闭即删。这条进 `SettingsRegistry`
  （它是"换台机器还成不成立"意义上的真设置），并且**必须 `AgentWritable=false`**——
  不能让 agent 自己打开自己的远程通道。
- **一次一个连接够用**（外部 agent 是串行的）。并发连接的隔离留到有需求再说。
- **管道两头都加 `PipeOptions.CurrentUserOnly`**：宿主侧把 ACL 限到本用户（Unix 上落到 0700 的 socket
  文件），客户端侧则在连之前认一下"对面是不是同一个用户的进程"——同机多用户下，别人放一个同名管道等着，
  我们就会把命令（以及那次授权确认）发给它。凭据文件的权限仍是门槛，这一条只是让**连都连不上**比
  "连上了但 token 不对"更早发生。

**已落地**（`TuneLab/Bridge/`）：`BridgeProtocol`（帧 + JSON-RPC 常量，宿主与 CLI 共用同一份定义）、
`BridgeCredentials`（`Configs/CommandBridge.json`：管道名 + 每次开桥现生成的 token + pid）、
`BridgeSession`（握手 / `commands/list` / `command/execute` / 反向的 `authorization/confirm`）、
`CommandBridge`（设置开关的订阅 + 接受循环）。开关是 `SettingsRegistry.CommandBridgeEnabled`，
默认关、`AgentWritable=false`。

两处实现上的要点，改这块代码前先知道：

- **读循环不能等命令跑完**。Edit 命令会反过来问客户端"这次做不做"，而那个回答正是从同一条连接上读回来的
  ——在读循环里 `await` 命令，就是在等一条自己不去读的消息（写这条时真踩了一次，测试里表现为超时）。
  故命令在独立任务上跑，写口上锁。
- **答得含糊一律当拒绝**。授权这件事上，缺字段/写错的回答不能算同意。

---

## 8. headless

headless = **不开窗口、不建音频设备，但插件加载了、工程在内存、命令能跑、合成能跑**。

复用已有的两处机制：

- 启动：`Program.InitCoreServices()` —— 与真实启动同一份初始化（配置/翻译/路由/启停/键位/插件上下文），
  因此"headless 下的行为"和用户看到的一致。
- 合成：`PumpableSynchronizationContext` + 驱动循环 —— 沙箱已证明非 UI 线程上泵 SyncContext、
  真引擎 Init/CreateSession/合成、读回真实音素全部成立。

headless 特有的三件事：

1. **没有编辑器态**（§5.2）——依赖它的命令必须显式传参。
2. **没人点卡片**（§5.1）——授权必须显式给。策略为 null 时一条 Edit 都不做（且说清是"没配授权"）；
   CLI 走的是另一条同样诚实的路：声明 confirm 但 `canAsk=false`，于是命令回报"需要确认但这里没法问"（§8.2）。
3. **数据目录应当可隔离**——CI 里跑要用 `TUNELAB_DATA_DIR` 指向临时沙盒，
   否则会读写开发机/CI 机上真实的用户数据目录（设置、插件、脚本库全在那儿）。
   ScreenshotBot 已经这么做了，直接沿用。

**唯一真正的新工程量**：headless 不加载 UI 层任何东西，但有些 handler 需要 `Dispatcher.UIThread`
（`ScriptWriteExecutor` 就是），headless 下要给它一个可泵的等价物。其余都是既有机制的拼装。

### 8.1 已落地（`TuneLab/Headless/HeadlessHost.cs`）

一条专用线程装上可泵的 `SynchronizationContext`，在它上面依次：`InitCoreServices()` →
`AudioUtils.Init(codec)`（**解码器不是播放设备**，音频 part 要靠它读 wav/mp3）→ `LegacyCompatLoader.Wire()`
→ `ExtensionManager.LoadExtensions()` → 音源引擎急切 `Init`（须早于挂工程，否则 part 一 Activate 就回落到
空会话且无回建路径）→ 建 `ProjectDocument` 并挂工程 → 组出 `CommandContext`（`EditorState` / `SideModel`
为 null，`MainThread` = `PumpDispatcher`）→ 装上 `HostCommandContext.Provider` → 跑调用方给的 body，
其 await 续体由驱动循环泵回来。收尾换一个空工程触发 Detach/Dispose，短暂续泵让在飞的销毁落地。

**反转了原计划里的"起 Avalonia headless 平台"**：那条路（ScreenshotBot 走的）会把 Skia / 字体 / 平台
一整套拉起来，只为一个我们并不需要的窗口系统。实地核对的结果是不必：`UI/` 与 `GUI/` 之外全仓库只有
`UiThreadDispatcher` 碰 `Dispatcher.UIThread`（那正是 §5 抽掉的那一样），扩展管理器与音源管理器零 Avalonia
依赖，`AudioEngine` 里数据层真正用到的（`SampleRate`、`AudioGraph.AddTrack`）都是静态量、不 `Init()` 也成立。
故只装真正需要的那几样。留下的风险是某个第三方插件在 `Init` 里碰 Avalonia——真撞上时升级只动这一个文件。

**工程从哪来**：`--project <file>` 给了就照"打开"那条路装载（`DeserializeNative` + native 元数据），
没给就新建空工程。刻意**不补轨道颜色**（编辑器打开时会补一个呈现层默认色）——headless 不呈现，
补了反而会把呈现层的默认写进随后导出的文件里。

**不给插件的 `Destroy()` 设时限**：收尾跑的是第三方代码，而合理的收尾本来就可能很慢（刷缓存、
等子进程干净退出）。宿主无从分辨"慢"与"卡"，切断反而会坏在插件自己的数据上，而那种损坏是**静默**的
——比挂住更糟。故一直等。能做也该做的是让卡住这件事**可见**：`VoicesManager` / `InstrumentsManager`
的 `Destroy` 逐引擎把"正在拆谁"写进日志（并逐个接住抛错，一个引擎收不了尾不该带走其余引擎），
headless 这边等久了在 stderr 上点一句（**只是提示，什么都不放弃**）。开发期真撞到过一次：某引擎的
`Destroy()` 阻塞等一个永不完成的 `Init()`，进程就停在那儿——根因是**我们自己**漏了一步启动初始化
（见下条），而当时唯一能把矛头指对地方的就是那行日志。

**启动必须整条走 `InitCoreServices()`，不许只挑自己用得着的那几样**。它现在也负责把进程环境
摆正（清掉 `NoDefaultCurrentDirectoryInExePath`——开发环境的终端会注入它，置位后 cmd 不再从工作目录
解析可执行文件，经 cmd 相对路径拉起辅助进程的插件会静默起不来）。无头宿主最初只抄了 `Main` 里
"看着有关"的几行、漏了这一条，后果是：某插件的辅助进程没起来 → 它的 `Init` 永远等不到回答 →
它的 `Destroy()` 阻塞等 `Init` → 整个进程挂死，而日志上什么异常都没有。**同一个宿主的启动步骤只该有
一处定义**，别在各入口各抄一份。

**`keybinding *` 在 headless 下无事可做，且必须【这么说】**：命令目录（id / 默认手势 / 作用域）是
编辑器在构建时连同 `Execute` 委托一起注册的，没有编辑器就没有目录。两条命令按 `ctx.EditorState == null`
如实回答"这个进程没有编辑器"，而不是"还没注册"（暗示等等就有）或"没有这个 id"（暗示名字写错了）——
那两句会让调用方白等，或对着一个必然找不到的 id 反复试。空 `id` 不在此列：那是调用方的笔误，
有没有编辑器都一样。（25 条命令全量跑一遍，这是唯一暴露出来的降级。）

**`ExtensionManager.LaunchPendingUninstalls()` 刻意不跑**：那是给用户装卸扩展收尾的，
无人值守的进程不该替他执行。

**日志不回声到控制台**（`FileLogger` 加了 `echoToConsole`）：这个进程的 stdout 属于命令结果，
`--json` 的输出必须能直接喂给解析器。

**legacy 兼容层要单独搬一次**：主程序把 `TuneLab.Hosting.Compat.Legacy.dll` 散拷进自己的输出目录
（不是 `Content`），故不会随 `ProjectReference` 流到 `TuneLab.Cli`。不补这一步，同一台机器同一个数据目录下，
命令行看到的扩展面会与应用不同——那种差异排查起来极难。

### 8.2 CLI 的 headless 与批量

- `tunelab --headless <group> <verb> …` 单条；`tunelab [--headless] --commands <file|->` 一串。
- **批量是同一个进程、同一个工程**：后一条看得见前一条的编辑。一条一进程做不到这件事
  （进程一退工程就没了），而"开工程 → 跑脚本改 → 导出 → 断言"正是 CI 要的形状。
- 首条失败即止并非零退出：让后面的命令在一个已经不对的状态上接着跑，只会把"哪一步坏了"埋掉。
- 回声（`$ project status`）打 stderr、结果打 stdout，故 `--json` 时 stdout 仍是一串干净的 JSON。
- 一行的切词：空白分隔、双引号成组、组内 `""` 表示一个字面双引号。**不用反斜杠转义**——这些行里
  最常出现的就是 Windows 路径。
- 数据目录仍是 `PathManager.TuneLabFolder`；CI 用 `TUNELAB_DATA_DIR` 隔离。

**顺带修掉的两处②期的话不算数**（都属于"入口说了假话"，不是新功能）：

1. **`canAsk`**（execute 的新参数，布尔，缺省 true）。声明 `confirm` 说的是"要问"，`canAsk` 说的才是
   "问得着"——非交互的 shell 里两者不同。缺了它，命令会把"根本没法问"说成"用户拒绝了"，而当时并没有
   任何用户被问过。补上后走的是早就写好的 `cannot_ask` 那支（"Confirmation is required … but no UI is
   available to ask"）。CLI 另在 stderr 上先说一次，免得 CI 里的人以为写生效了。
2. **`[a] always` 现在真的不再问**。裁决切档"由策略实现方自己完成"，而桥那侧的策略只活一次调用——
   记住这件事的只能是客户端：`AuthorizationState` 一旦被抬到 auto，此后每次 execute 都声明 auto。
   单条命令时这件事看不出来（进程随即退出），批量时才露馅。

### 8.3 CI 用例：`tests/headless/smoke.ps1`

`pwsh tests/headless/smoke.ps1`。临时 `TUNELAB_DATA_DIR` 沙盒 + stdin 一律重定向到空文件
（那正是 CI 里的样子，也让它在开发机的交互终端里跑出同样的结果、不会停在确认提示上）。
八组断言：离线 help / 无授权时读照跑写不落地且**说清是问不着而非被拒** / 给了 `--yes` 整串跑通且后一条
看得见前一条 / 装载刚导出的工程 / `--json` 的 stdout 干净可解析 / 命令失败退 1 / 用法错退 2 /
工程打不开时一条命令都不跑。

（暂不新增跑它的 workflow——本仓目前没有任何跑测试的 CI，那是另一个决定。）

同一套配方也用来测具体特性：`tests/headless/path-picker.ps1` 把脚本夹具直接写进沙盒的
`Scripts/`、把 `.tlx` 解到沙盒的 `Extensions/`，再经 `script inputs` / `script run-saved` / `extension settings`
断言一个新 config 类型（`PathPickerConfig`）的措辞与取值。它是现成的模板：**断言落在“脚本 `main`
收到的那个值”上而不是退出码**——写坏的 `inputs` 曾经静默失效（拿默认值照跑、回报仍说跑成功），
只看退出码的自动化会“通过”而什么都没测到。

---

## 9. 三个入口适配器

### 9.1 内置 agent（第一个消费者）

`IAgentTool` 不再被手写实现，而是**从注册表合成**：一条命令 → 一个工具，
`Name` 由路径转下划线（`setting set` → `set_setting`，与现有名字对齐，模型侧零感知），
`Description` = `Documentation`，`ExecuteAsync` = 调命令 + `Render` + `ClampToolResult`。

**让内置 agent 成为第一个且唯一的消费路径**，是验证这层没做错的唯一可靠办法：
如果合成出来的工具面和现在不等价，实测立刻会发现。

### 9.2 CLI

- `tunelab <group> <verb> [args]`，默认打人类文本，`--json` 打 `Data` 原样。
- 退出码：`0` 成功 / `1` handler 失败 / `2` 用法错 / `3` 连不上宿主。
- `--help` **必须离线可用**（不连宿主也能列出命令树）。这要求命令树的元数据能在不启动
  宿主全套的情况下自省——注册表是纯声明，满足这条。
- `--headless` 切到 §8 那条路。
- **`docs` 组不连宿主也能答**：`ICommand.NeedsHost == false` 声明"答案只取决于程序自带的东西"
  （编译进来的 API 参考常量、可执行文件旁的 `Resources/Manual`），CLI 于是就地跑（`LocalRunner`）——
  外部 agent 第一次接触 TuneLab 时，读 API 参考与查手册**零启动、零前提**，TuneLab 开着没开着都行。
  批量清单里一条都不需要宿主时同样一个进程都不起。就地答只允许 `Read`（封条在 `CommandRegistryTests`）：
  会写的命令要过宿主的闸门与授权，绕过去是不能接受的。
- **`--search <regex>`**：把全部命令的帮助语料（`Brief` + 完整说明）过一遍正则，命中的给命令名加一段
  上下文。同样离线（注册表是纯声明）——"有没有一条命令能干这件事"是外部 agent 最先要问的问题，
  不该以启动为前提。
- **object / array 参数收的是 JSON 文本，解析不了即用法错**（退出码 2），不许降级成"当没给"——那会让
  脚本拿默认值照跑而回报仍是"跑成功了"，无人值守的跑批于是"通过"了却什么都没测到。而把 JSON 塞进
  Windows 命令行有**两个互不相干的坑**，故诊断分两支说话：**收到的实参里一个双引号都没有**时，几乎
  必然是外层 shell 剥掉了它们（PowerShell 的 `Start-Process -ArgumentList` 拼出的是一行命令行，
  `CommandLineToArgvW` 会吃裸引号）——那时报"JSON 语法错、位置 1"是把人往错方向指，故这一支单独说、
  并把收到的实参回显出来当证据；引号活着但**路径里的反斜杠没双写**才是另一支。
  两支的措辞刻意**不重叠**：反斜杠那句提示只在真有反斜杠时附上——无条件附在每条坏 JSON 后面的话，
  它就成了永真的噪音，测试再也钉不住它（`smoke.ps1` 第 7 组就这么假绿过一阵）。

**已落地**（`TuneLab.Cli/`，程序集 `TuneLab.Cli`）：命令树 / 单条命令的 `--help` 全部离线（直接读
`CommandRegistry`，不连宿主）；参数按该命令的 schema 逐个校验并转型，认不出的参数名报用法错而不是
静默忽略；`--json` 打 `Data`、默认打渲染文本；`--yes` / `--dry-run` / 默认 stdin 交互确认；
退出码 0/1/2/3 如约。连不上时区分"桥没开"与"宿主已退出但凭据文件还在"——两者的下一步不同。
`--headless`（§8.1）与 `--commands`（§8.2）也已落地：同一份解析、同一份授权语义，只是命令送去的地方不同。

**入口叫法：打印前把 agent 工具名换成自己的**。命令面的文本第一读者是模型，故引用别的动作时写的是
agent 工具面上的名字（"Call `list_settings` to see the exact keys"、"Change one with `set_setting(key, value)`"）
——那些名字在命令行里根本不存在，照原样打出去等于让人去调一条 `--help` 里查不到的命令。
注册表给出对照表（`CommandRegistry.PathsByAgentToolName`），CLI 在**每个打印点**机械替换
（`TuneLab.Cli.CommandText.ForCli`）：`list_settings` → `tunelab setting list`，调用式的参数表留着但
加一个空格（`tunelab setting set (key, value)`），读成附注而不是函数调用。`--json` 的 `Data` 一个字
不动——那是机器契约。不去改那 180 多处文案，是因为一处文案同时说两套叫法只会更差；机械替换让每个
入口各自看到一致的名字，新加的命令自动跟上（`CliCommandLineTests` 有一条封条盯着"有没有漏"）。

**程序集名不能叫 `tunelab`**：它会与被引用的 `TuneLab.dll` 在同一输出目录里同名（Windows 不区分
大小写）而互相覆盖。命令名 `tunelab` 是安装期的事——装包时给 `TuneLab.Cli.exe` 落一个 `tunelab`
入口即可。

**发得出去才算有**（这三层缺一层，上面所有东西对用户就都不存在）：

1. **随包发**：`CIUtils/pack-installer.ps1` 把 `TuneLab.Cli` 发进与 app 同一份 stage（只多出
   `TuneLab.Cli.exe` 那几个文件，Avalonia/Skia/TuneLab.dll 全共享）；`build-artifacts.yml` 同样把它
   拷进便携产物。
2. **敲得着**：靠**绝对路径**。一期**刻意不做 PATH**——`tunelab` 在不在 PATH 上不是"能不能跑通"的
   问题，只是"要不要多打一串路径"：实测一个只拿到接入说明的陌生 agent 全程用绝对路径跑完七条命令、
   零失败（它自己就把示例里的 `tunelab` 替成了那个路径）。而改 PATH 是**动用户的环境**，代价一侧却是
   实打实的：向导上要给选项、静默自更新时无人可问、卸载要摘干净、读写注册表要绕开变量展开，且必须
   真装一次/卸一次才敢发。收益只是省几个字，故推到后续；接入说明改为把"示例里的 `tunelab` 就是这个
   绝对路径"明写出来（`ExternalAgentOnboarding`）——实测那个 agent 自己推对了，但那是它多想一步，
   不该让它想。
   将来真要做，两条别丢：**不能把安装目录本身挂上 PATH**（那里躺着 Skia/NAudio 一堆原生 dll，而 PATH
   参与 Windows 的 dll 搜索——挂上去会改变别的进程加载 dll 的结果，是能把不相干程序弄坏且极难归因的
   副作用；正解是单独一个只放转发脚本的目录）；**读写 PATH 必须不展开变量**
   （`DoNotExpandEnvironmentNames` + 保持 `REG_EXPAND_SZ`：经 `Environment.SetEnvironmentVariable`
   写回会把用户 PATH 里的 `%USERPROFILE%` 烧成字面路径）。卸载要先摘 PATH 再删目录，否则留下一条
   谁也看不出来源的死路径。
3. **说得清**：设置窗「通用」页末尾一颗"复制接入说明"按钮，把外部 agent 要知道的东西一次给全
   （`ExternalAgentOnboarding`）。文案**在点下去那一刻生成**，因为里面有三样只有此刻才知道的事实：
   命令行的真实绝对路径（文件不在就明说这份安装没有命令行，绝不吐无效路径）、`tunelab` 到底能不能直接敲
   （便携解压的那份没有 PATH 入口）、命令桥此刻开没开（没开就在最前面多一段"只有我能去开它"，且位置与
   字样取**用户界面上看到的译文**）。口吻是用户在对他自己的 agent 说话——那段话是用户当自己的话贴出去的。
   刻意**不复述**"连不上怎么办"：那句 `BridgeClient` 已经会说，且能分清"桥没开 / 宿主没跑 / 桥不应答"
   三种情形；文案是主动快照、命令行的消息是被动兜底，抄一遍只会有一天与它对不上。
   结尾要求 agent 只把**门**记进自己的记忆（有这道门、门在哪、四条铁律），并明确**不要**记命令清单、
   参数与版本号——那些随版本漂移，记住了就会自信地调一条不存在的命令。

### 9.3 MCP server

- **stdio 独立进程**，不由宿主 spawn。理由：宿主没开时，server 仍然活着并能**在对话里**
  告诉 agent"请先启动 TuneLab"。反过来（宿主 spawn、绑端口）的失败模式是工具从
  agent 的工具列表里静默消失，agent 连"怎么修"都说不出来——那是死胡同。
- **工具面按 group × {read, edit} 合成**，不是一命令一工具：
  `setting_read` / `setting_edit` / `extension_read` / … 约 15 个，
  `inputSchema` 是 `{subcommand: <该组该类的叶子枚举>, arguments: <object>}`。
  描述里带每个 subcommand 的 `Brief`，让 `tools/list` 自描述。
  纯读的 group（`source` / `effect` / `docs`）只出 read 工具。
- annotation 如实：`*_read` 标 `readOnlyHint`，`*_edit` 标 `destructiveHint`，`sandbox` 两者都不标。
- **为什么不一命令一工具**：25 条命令各带完整 JSON Schema，会挤占 agent 的工具选择上下文，
  且随能力增长线性膨胀。按 group 合成让工具数随**组**增长而不是随**叶子**增长。
- **为什么不折成单个 invoke_command**：那样 `tools/list` 只剩一个不透明的洞，
  agent 不预先知道就发现不了能力；且安全标注退化成"一律按最坏情况标"，客户端的批准 UI 失去意义。

**已落地**（`tunelab mcp`，`TuneLab.Cli/Mcp/`）。照上面的形状做，另有六处是动手时才定的：

- **住在命令行里，不是又一个二进制**。`tunelab mcp` 接管 stdin/stdout 作协议通道。理由是它要的东西
  （`BridgeClient`、runner、参数校验、文本换名）都已经在那儿，而命令行本身随发布产物一起分发
  （`build-artifacts` 把它 publish 进同一个目录）。客户端那侧的配置就是
  `{ "command": "<...>/TuneLab.Cli.exe", "args": ["mcp"] }`。仍然满足"独立进程、不由宿主 spawn"：
  `tools/list` 由 `CommandRegistry` 就地自省，**列能力不需要 TuneLab 开着**；真要执行才连桥，连不上时
  回的是"请先启动 TuneLab / 去设置里开桥"这句话本身（作为工具结果，不是从工具列表里消失）。
- **一个 group × 一个类别 = 一个工具**（今天 21 个：13 个 group 摊开成 20 个，加一个 `tunelab_help`）。后缀是 `read` / `edit` / `run`
  ——`sandbox` 那条既非只读也不碰用户数据，压进任何一边都会让安全标注说谎（见 `CommandKind`），故它
  自成一个后缀 `sandbox_run`。
- **`tools/list` 带 Brief，全文按需**。33 条命令的 `Documentation` 合起来 24K 字符，全塞进去等于每次会话
  先吃掉几千 token——正是"按 group 合成"想省下的那部分。故摘要进列表、全文走 `tunelab_help`
  （同 `get_script_api` / `get_manual` 的渐进式披露）。**参数说明是例外，它留在 `tools/list` 里**：
  `arguments` 是个自由 object，agent 不知道字段名就根本没法调用——那是"调用所必需"，与"什么时候该用它"
  不是一回事。
- **换名照 CLI 的办法**（`McpText`）：命令面的文本里引用别的命令时用的是 agent 工具面的名字
  （`list_settings`），那些名字在这里不存在，故机械换成本入口的叫法
  （`setting_read with subcommand "list"`）。表在注册表、换名在入口。
- **授权：每次执行声明 auto 且 `canAsk=false`**。这不是提权——天花板是用户在 TuneLab 里设的档位
  （`BridgeAuthorizationPolicy`），声明 auto 只是说"这个入口自己不再加码"；批准 UI 在 MCP 客户端那边，
  annotation 已经如实标了哪些工具会改东西。用户设成 confirm 时，改动被如实拒绝
  （"要确认，但这里没有 UI 可问"）而不是被偷偷做掉，也不会被说成"用户拒绝了"——实测验过这一条。
- **连接懒连、断了重连一次**。server 的寿命是整个对话，而 TuneLab 可能中途才启动、也可能退出又开起来：
  开机就连会把"宿主没开"变成启动失败，一次性连上不管则会在宿主重启后永远失败下去。只在**连接层面**
  出问题时重试，命令自己失败不重跑（那既不会变对，还可能把一次写做成两次）。

封条：`tests/TuneLab.Tests/McpToolsTests.cs`（工具名快照、每条命令恰好够得着一次、annotation 如实、
参数在列表里而全文不在、换名彻底）+ `tests/headless/smoke.ps1` 第 14 段（真起一个 server 进程，
**刻意不开 TuneLab**：initialize 协商、tools/list、`tunelab_help`、只读文档照答、要宿主的命令如实说
"请先开桥"、未实现的方法回协议错误而不是工具结果）。

---

## 10. 分期

| 期 | 内容 | 可验证的产出 |
|---|---|---|
| **⓪** | 前置改名（§2）：数据层 `ICommand` / `Command` / `CompositeCommand` → `*UndoCommand`，腾出裸名 | 编译通过、既有测试全绿（纯改名，`Document/` 外只动 3 行） |
| **①** | `CommandRegistry` + 契约 + 26 个 handler 搬家（结果形状按 §4 切）+ 内置 agent 改成合成消费者 | 内置 agent 实测与现在等价（回归现有测试文档） |
| | **已完成**：25 条命令全部进注册表，`TuneLab/Agent/Tools/` 只剩 `AskUserQuestionTool`（按 §5.4 是入口能力，不进命令树）。四类入口依赖的抽象都已落地：`IAuthorizationPolicy`（§5.1）、`IEditorStateAccess`（§5.2）、`ISideModelAccess`（§5.3） | |
| **②** | 管道 bridge + 凭据文件 + 设置开关 + CLI（全部 read 命令 + 少数 edit） | 开发者能从终端驱动运行中的 TuneLab |
| | **已完成**：桥与 CLI 都在（见 §7 / §9.2 的落地小节）。命令不分 read/edit 地全部可用——闸门按连接声明的档位走，故 edit 不需要另开名单 | |
| **③** | headless + CI 用例 | CI 里无人值守跑一串命令并断言 |
| | **已完成**：`HeadlessHost` + CLI 的 `--headless` / `--project` / `--commands` + `tests/headless/smoke.ps1`（见 §8.1–8.3）。反转了"起 Avalonia headless 平台"的原计划，理由记在 §8.1 | |
| **④** | MCP server 壳 | 外部客户端连上，用已有订阅额度驱动 |
| | **已完成**：`tunelab mcp`（`TuneLab.Cli/Mcp/`），工具面由注册表合成（今天 21 个），宿主没开也列得出能力（见 §9.3 的落地小节） | |

①是大头且用户不可见；②开始有实感。**不建议把①②合并推进**——①的验证靠"内置 agent 行为不变"，
掺进新入口会分不清是搬家搬坏了还是新入口的问题。

---

## 11. 明确不做

- ~~**通用"执行任意 `KeyCommand`"命令**~~、~~**播放/试听**~~ —— **已反转（issue #150）**。原判据是
  "那些 `Execute` 是 UI 委托、依赖焦点/选中态、会弹模态框，结果不可预期"，反转不是因为顾虑消失了，
  而是三条顾虑各自有了对应的闸（§5.5）：后果分档决定过不过授权（不是"一口气全放回来"）、
  `Unavailable` 判据把"依赖焦点/选中态"变成一句可回报的原因（不是空跑一趟）、`Prompts` 判据把会弹模态的
  动作默认拒掉（要显式声明有人在场）。命令面的基调是**全面**：用户能做的事外部都该够得着，够不着的
  地方要有说得出的理由。
- **另存为 / 打开**：不禁止，但**动作面**上的那两条弹的是要人应答的模态，故 `action run` 默认拒绝、
  要 `allowPrompt` 显式说"用户就在机器前"。带路径的「打开」后来成了一条命令（`project open`，见 §6.1）——
  那是同一件事的另一种形状，不是绕过这一条：它不弹框，代价是未保存时直接拒绝。
  框真弹出来之后 `editor status` 会报「有个框挡着」（见 §6.1）——那是「用户其实不在机器前」唯一能被外部发现的地方。
  ~~**导出音频**仍是人在环决定~~ —— **已翻案（收尾）**：见下面单列的那条。
- ~~**导出音频**~~ —— **已翻案（收尾），成了 `project export-audio`**。原判据只有一句：渲染期界面锁住
  好几分钟，「要不要现在把这台机器占住」是人在环的决定。但那句话在 headless 里根本不成立——那儿没有
  界面可锁；而连着窗口跑时，「人在环」恰恰是**授权闸门**该管的事：把代价写进卡片（那一档的措辞明写
  「要跑完整合成与混音、可能几分钟、期间窗口锁住」）让用户自己决定，比替他决定「你不能这么做」要诚实。
  翻案之后才露出真正的难点，而它与界面锁不锁无关：界面上的导出**不等合成**（直接拉 AudioGraph 此刻的
  数据，用户是看着状态带自己等的），命令面照搬会静默产出静音。故那条命令自己把合成驱动到落定，
  超时就什么都不写，有失败段默认拒绝——形状见 §6.1。
- **视口（缩放 / 滚动）**：模拟滚轮缩放仍然**不收**（issue #150）。它是以鼠标位置为轴心的连续手势
  （操作态，见 keybinding-system.md §0），而外部没有鼠标；且**不改变任何结果**，只改变用户此刻在看什么。
  **选择器参数落地之后这一条不变**（§5.6）：那收的是「界面上本来就摆着一排、成员随工程变」的入口
  （一个闭集里的成员），而「滚到某个 tick」要的是一个连续量。
  **但这一条自己留下的那半句已经兑现**（收尾）：它当时写着「真正有用的那件事是让用户看到我改了哪里，
  它要一个 tick 或一个对象作参数」——那就是 `editor reveal`（§6.1）。翻的不是缩放手势，是**定位**；
  两者的分界正是这一条自己划的：手势没有参数、也不指向任何东西，而 reveal 恒要一个「哪里」。
  缩放在 reveal 里只作为**后果**出现：目标装不下才缩小，且永不替用户放大。
- **播放循环与区间**：不是覆盖率的洞——TuneLab 今天没有循环播放这个功能（`AudioEngine` 里没有）。
  动作面只镜像界面已有的能力；要循环，先得在界面上有循环。
- **授权的能力维度拆分**（工程编辑/应用配置/磁盘文件）。本期只做入口维度，但形状要容得下它。
- **MCP elicitation**（客户端支持面不齐）。
- **MCP 的 resources 与 prompts**。`resources` 是给客户端"主动附进上下文"的静态可寻址内容，而我们手上
  够格当资源的只有文档，它们**本来就是检索式**的（手册有 `query` / `section`，十几章）：做成 resource
  要么整份塞进上下文（正是渐进式披露想避免的），要么另造一套 URI 空间表达"第几章"，凭空多一层。
  真正诱人的是把**当前工程**当 resource 让客户端自动跟随，但那要 `resources/updated` 推送，而命令桥是
  纯请求-响应——要接就得先在桥上加一条变更通道，成本远大于收益。`prompts` 是"用户可选的提示模板"，
  我们没有这类资产：内置 agent 的系统提示是宿主内部的，现造几条模板就是在入口层发明产品形态，
  与"只镜像已有的能力"这条基调相反。
  故 `initialize` **只声明 `tools` 能力**，客户端问 `resources/list` 拿到的是 `-32601`（而不是空列表——
  空列表读起来像"有这个能力但没有内容"）。
- **MCP 的取消 / 进度 / 并发**：知道，暂不做，三条各有代价但都不咬人。①客户端的
  `notifications/cancelled` 收下即忽略——一条跑很久的 `script run` 中止不了（CLI 那侧有 Ctrl+C，这里没有
  等价物）；②没有 `notifications/progress`，长命令期间客户端看不到动静；③请求**串行**处理，一个慢命令
  会把后面的（含 `ping`）挡在后面，客户端若据此判超时就会断连。要接的话三条是一件事——per-request 的
  取消令牌 + 并发派发，届时一起做。
- **并发多连接 / 多窗口寻址**。等有需求。

---

## 12. 风险

1. **①期期间内置 agent 会短暂不稳**。缓解：分批搬（先 read 后 edit），每批跑对应的既有测试文档。
2. **结果形状改造会误伤调过的措辞**。缓解：renderer 里逐字沿用现有文本，只把结构化 `Data` 加上去；
   error message 不动。
3. **headless 下 `Dispatcher.UIThread` 的替代**是唯一真正的新代码，也是最可能出并发 bug 的地方。
   缓解：复用沙箱已实测的泵，不另造。
4. **凭据文件 + 管道开关是新的攻击面**。缓解：默认关、`AgentWritable=false`、
   凭据文件靠文件系统权限、不开网络端口。
