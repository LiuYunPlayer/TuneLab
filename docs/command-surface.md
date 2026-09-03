# 命令面 —— 一份末端动作，多个入口（设计）

> **目标形状**：TuneLab 的每个可被外部驱动的动作只实现一次（"末端动作"），
> 上面接多个入口——内置 agent、CLI、MCP server、CI 里的无界面进程——都调同一份。
>
> 本文承接 [agent-tools.md](agent-tools.md)（现有 26 个工具及其归属判据）与
> [script-inputs-and-action-surface.md](script-inputs-and-action-surface.md)（统一动作面 / 分级授权）。
> 三者共用一句原则：**SSOT 约束的是执行面，不是入口数**。

---

## 0. 为什么现在做，以及为什么不能"再抄两遍"

`tl` 脚本动作面已经稳了（单一 commit、单一闸门），26 个 agent 工具覆盖了工程编辑、环境感知、
设置/快捷键/路由/扩展设置、探测沙箱。此刻要给外部 agent 开 CLI 和 MCP 两个入口，只有两条路：

- **各自再实现一遍**：三份 handler，必然漂移，且每加一个能力要改三处。
- **提出一层命令面**：末端动作一份，入口是薄适配器。

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
| **有一个工具是对话内交互** | `AskUserQuestionTool(RequestUserAnswerAsync)` | 它不是末端动作，是**入口能力**。见 §5.4 |
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
| 输入层 | `KeyCommand`（`TuneLab/Input/KeyCommand.cs`） | 可绑快捷键的 UI 命令（带 `Execute` UI 委托） |

`Operation` 也不能用——`AutomationRendererOperation.cs` 等处有几十个 UI 鼠标交互状态机类叫 `*Operation`。

**裁决**：**裸名 `Command*` 归命令面**（`ICommand` / `CommandRegistry` / `CommandResult` /
`CommandArgs` / `CommandContext` / `CommandKind`），数据层那一族**前置改名**。

这不是"为腾地方而牺牲精确性"，而是**顺手修正一个既有的命名不精确**：那个 `ICommand` 的全部内容
就是 `Undo()` / `Redo()` 两个方法——它讲的一直是撤销栈。而"能被入口调用的末端动作"才是 command
在 CLI/MCP 语境下的主流含义。

接口改叫 `IDataCommand`——这是**归队而非另起名**：`Document/` 目录里的类名本来就是 `Data*` 一族
（`IDataList` / `IDataMap` / `IDataObject` / `IDataProperty` / `DataDocument` / `DataObjectList` /
`SortedDataLinkedList`），`ICommand` / `Command` 恰是少数没带前缀的。限定词取**所属层**，
于是三个"命令"按层各就各位，与 `KeyCommand` 同范式：

| 符号 | 层 | 含义 |
|---|---|---|
| `IDataCommand` | 数据层 | 撤销栈条目：一次可撤销突变 |
| `KeyCommand` | 输入层 | 可绑快捷键的 UI 命令 |
| `ICommand` | 宿主入口层 | 末端动作（本文的命令面，占裸名） |

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
// 一个末端动作：路径 + 参数 schema + 文档 + handler。不含任何入口特有的东西
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
| MCP | 靠工具 annotation 让客户端自己问（`readOnlyHint` / `destructiveHint`）；服务端侧默认放开，因为**批准 UI 在客户端**——服务端再问一遍是双重询问且没有 UI 可用 |
| headless / CI | **必须显式**：`--yes` 或按维度配；没给就拒绝所有 Edit 命令并说明原因（不能静默放开） |

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

---

## 6. 命令树与迁移清单

### 6.1 group 划分

9 个 group，noun-first：`project` `script` `docs` `extension` `source` `effect` `setting` `keybinding` `sandbox`

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

---

## 8. headless

headless = **不开窗口、不建音频设备，但插件加载了、工程在内存、命令能跑、合成能跑**。

复用已有的两处机制：

- 启动：`Program.InitCoreServices()` + `ConfigureAppCommon(AppBuilder.Configure<App>().UseHeadless(...))`
  —— ScreenshotBot 走的就是这条路，与真实启动同一份初始化，因此"headless 下的行为"和用户看到的一致。
- 合成：`PumpableSynchronizationContext` + 驱动循环 —— 沙箱已证明非 UI 线程上泵 SyncContext、
  真引擎 Init/CreateSession/合成、读回真实音素全部成立。

headless 特有的三件事：

1. **没有编辑器态**（§5.2）——依赖它的命令必须显式传参。
2. **没人点卡片**（§5.1）——授权必须显式给，缺则拒绝 Edit。
3. **数据目录应当可隔离**——CI 里跑要用 `TUNELAB_DATA_DIR` 指向临时沙盒，
   否则会读写开发机/CI 机上真实的用户数据目录（设置、插件、脚本库全在那儿）。
   ScreenshotBot 已经这么做了，直接沿用。

**唯一真正的新工程量**：headless 不加载 UI 层任何东西，但有些 handler 需要 `Dispatcher.UIThread`
（`ScriptWriteExecutor` 就是），headless 下要给它一个可泵的等价物。其余都是既有机制的拼装。

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

---

## 10. 分期

| 期 | 内容 | 可验证的产出 |
|---|---|---|
| **⓪** | 前置改名（§2）：数据层 `ICommand` / `Command` / `CompositeCommand` → `*UndoCommand`，腾出裸名 | 编译通过、既有测试全绿（纯改名，`Document/` 外只动 3 行） |
| **①** | `CommandRegistry` + 契约 + 26 个 handler 搬家（结果形状按 §4 切）+ 内置 agent 改成合成消费者 | 内置 agent 实测与现在等价（回归现有测试文档） |
| **②** | 管道 bridge + 凭据文件 + 设置开关 + CLI（全部 read 命令 + 少数 edit） | 开发者能从终端驱动运行中的 TuneLab |
| **③** | headless + CI 用例 | CI 里无人值守跑一串命令并断言 |
| **④** | MCP server 壳 | 外部客户端连上，用已有订阅额度驱动 |

①是大头且用户不可见；②开始有实感。**不建议把①②合并推进**——①的验证靠"内置 agent 行为不变"，
掺进新入口会分不清是搬家搬坏了还是新入口的问题。

---

## 11. 明确不做

- **通用"执行任意 `KeyCommand`"命令**。`Keymap` 每条都带 `Execute`，通用执行器会一口气把
  已裁定不给 agent 的播放/传输/视野/工具切换全放回来，且那些 `Execute` 是 UI 委托、
  依赖焦点/选中态、会弹模态框，结果不可预期。命令面是**精准开孔，不是开闸**。
- **播放/试听**。已裁定：agent 只操作工程数据，播放是实时人在环动作。CLI/MCP 不改变这个判断。
- **另存为**（选路径是人的决定）；**导出音频**（人在环决定）。
- **授权的能力维度拆分**（工程编辑/应用配置/磁盘文件）。本期只做入口维度，但形状要容得下它。
- **MCP elicitation**（客户端支持面不齐）。
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
