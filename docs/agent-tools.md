# Agent 工具集设计

TuneLab 内置 AI Agent 通过"工具"读取与编辑当前工程。**核心理念：单一动作面（CodeAct）**——编辑工程一律由模型写 JavaScript 经 `run_script` 表达（对象式 `tl` API），读取只保留一个"定向总览"，其余读取也走脚本。曾经的细粒度读写工具（`transpose_notes`/`apply_edits`/`get_part_notes`…）与其门面 `IAgentProjectEditor` 已全部退役——同一件事多条路只会降模型选择准确率、堆 prompt。本文面向维护者，也作为编写工具描述（喂模型）时的一致性参考。

**归属判据（脚本面 vs 工具面）——按「谁需要」分**：分界不是"读/写"（`tl` 本就有大量读，如 `part.notes()`），而是**"这是不是用户会要的能力"**：
- **用户会要的能力**（工程编辑、可复用命令、快捷键、未来 MCP）→ 走 `tl` 脚本动作面。它同时是用户的脚本面与 agent 的写路径，故"帮用户写脚本+绑快捷键"和"agent 顺手做一件事"是同一件事。
- **只有 agent 自身推理需要、用户不会去脚本化的**（枚举环境、读元数据/schema/readme、定位总览）→ 加薄只读 `IAgentTool`。
- **护栏**：一切**工程状态的修改**天然属"用户会要的"，恒走 `tl`，绝不因"只 agent 需要"另开专用工程写工具——否则碎掉单一撤销单位 + 授权闸门 + 模型动作词汇。
- **SSOT 约束的是执行面、不是工具数**：多道工具门可以（`run_script` 内联、`run_saved_script` 按名），只要都汇进同一受闸门执行面（`ScriptWriteExecutor` → `ScriptContext` 那次 `Commit`）。库管理工具（`save/list/read/delete_script`）不改工程状态、也非 `tl` 可脚本，故为工具。

## 工具全集（12 个）

三个面：**操作工程** + **管理脚本库** + **环境感知（只读）**。

| 工具 | 面 | 作用 |
|---|---|---|
| `get_project_overview` | 操作 | 唯一只读"定向"：PPQ、tempo、拍号、各轨(1-based 编号/名/静音独奏/增益声像/part 数/音符数)。直接读 `IProject`，不经门面。 |
| `run_script` | 操作 | 写一段 JS（对象式 `tl`）做任意读/算/改，整段 = 一个可撤销单位、出错原子回退。 |
| `get_script_api` | 操作 | `run_script` 的按需文档（渐进式披露）：完整 `tl` API + 句柄/tick/收口规则 + 工具脚本约定。写第一段脚本前调一次。 |
| `save_script` | 库 | 把功能写成**工具脚本**(定义 getScriptInfo+main)存库 → 自动注册进菜单复用。只存不执行；声明了 getScriptInfo 则先预校验。 |
| `list_scripts` | 库 | 列库内脚本，标出工具(+context)/普通，并标注哪些**带入参**(定义了 getInputConfig)。 |
| `read_script` | 库 | 读某脚本源码（编辑前）。 |
| `delete_script` | 库 | 删某脚本（同时从菜单移除）。 |
| `get_script_inputs` | 库 | 读某脚本的入参 schema（逐字段名/类型/默认/范围·选项）+ 用户**上次输入值**。只读（只 eval getInputConfig，无副作用）。run_saved_script 前调。 |
| `run_saved_script` | 库 | 按库名跑已存脚本（= 替用户按那个菜单项），`inputs` 可省：给了覆盖在上次值上再补默认，没给用上次/默认。走 run_script 同一授权闸门。 |
| `list_extensions` | 感知 | 列用户已装扩展：名/id/版本/作者/类别(format/voice/instrument/effect/agent-model)/加载状态/有无 readme。直接读 `ExtensionManager.LoadResults`。 |
| `get_extension_readme` | 感知 | 按 id 或名读某扩展 README（markdown 原文，按语言解析、上限截断）。按需拉取（渐进式披露）。 |
| `list_sound_sources` | 感知 | 分层枚举音源：不给 `engine` → 列引擎(type id/显示名/提供包，不 Init)；给 `engine` → 列该引擎音源(id/名/描述，仅 Init 它)。`kind` 可选过滤。 |

```
模型 ──tool call(JSON)──► IAgentTool 实现
        ├─ get_project_overview ───────────────► IProject（直接读）
        ├─ run_script ─────────┐
        │                      ├─► ScriptWriteExecutor（授权闸门/预览/收口）──► ScriptRunner ──► Jint + 对象式 API ──► IProject
        ├─ run_saved_script ───┘         （run_saved_script 先按名读库源码 + 解析入参再进闸门）
        ├─ get_script_api ─────────────────────► ScriptApiReference.Text
        ├─ get_script_inputs ──────────────────► ScriptRunner.GetInputConfig + ScriptInputMemory（只读）
        ├─ save/list/read/delete_script ───────► ScriptLibrary / ScriptTools
        ├─ list_extensions / get_extension_readme ─► ExtensionManager.LoadResults / ExtensionReadme（只读）
        └─ list_sound_sources ─────────────────► VoicesManager / InstrumentsManager（只读，给 engine 才 Init）
```

- **IAgentTool**（`TuneLab/Agent/IAgentTool.cs`）：工具对模型的声明（名称/描述/参数 JSON Schema）+ 执行入口。实现薄：解析参数 JSON、干活、把结果/错误格式化回灌。
- **AgentRunner**（`TuneLab/Agent/AgentRunner.cs`）：provider 无关的多轮工具循环，只依赖 `IAgentModelSession`。模型适配器是宿主内部模块（不开放为插件类型），接入新 LLM 提供方见 [agent-model-adapters.md](agent-model-adapters.md)。
- 工具在 `AgentSideBarContentProvider.SetProject` 处用当前 `IProject` + "当前 part/量化/语言"访问器实例化并注册（工程切换即重建）。

## 寻址与单位约定

- **`get_project_overview` 用 1-based 序号**（"第 1 轨"即首轨，贴合用户认知）展示轨道。这是模型与用户对话里指代轨道的口径。
- **脚本（`tl` API）用句柄 + 绝对 tick**：集合方法（`part.notes()` 等）返回临时句柄数组，按引用施改、无 1-based 编号；位置/时长一律**绝对（全局）tick**（`tl.ppq` 取 PPQ=480），音高 MIDI。句柄仅当次运行有效（数据层对象无持久 id），脚本源码不得内嵌句柄字面量。坐标换算在各句柄内（落数据减 part 锚点 Pos、读时加回；part 只对脚本暴露 `startPos`/`endPos` 真实几何、不暴露锚点），脚本作者不碰。
- **每次写 = 一个可撤销单位**：`run_script` 整段、`save_script` 保存的工具脚本每次 `main()` 运行，都是一个 `Commit`；出错则 `DiscardTo(startHead)` 原子回退（工程不变）。收口纪律见下与 `docs/script-tools-design.md`。

## run_script：脚本逃生口（= 唯一编辑面）

让模型写 **JavaScript** 表达任意编辑——"5-8 小节每音符升八度再加三度和声" = 一个循环，一轮搞定。整段运行 = 一个可撤销单位。

**脚本引擎是独立模块**（`TuneLab/Scripting/`，命名空间 `TuneLab.Scripting`，只依赖数据层、不依赖 agent）——`run_script` 只是它的消费者（将来 MCP server / 用户手写脚本宏复用同一动作面）。

```
RunScriptTool (Agent 层，薄) ──► ScriptRunner ──► Jint 引擎 + 对象式 API（根 tl + 句柄）
                                     │                     │
                              沙箱/限制/收口          数据层 (IProject/...)
```

- **引擎 = Jint**（纯 C# 托管 ECMAScript 解释器，零原生依赖）。沙箱：不暴露 CLR；限递归(64)/语句数/内存(64MB)/超时。资源上限**按触发源分流**（`ScriptLimits`）：agent=紧(5s/5M 当失控保险丝)、用户交互=放宽(60s/200M)。
- **入口写守卫（`Pushable`）**：别处 UI 操作中途（有未提交命令）禁止脚本写——否则 `Commit` 会吞并其未提交改动。当前为入口拒绝；规划改为"守卫下沉到首次写"（只读脚本不受限）+ wait-retry 自动重跑（脚本同步跑、运行期 `Pushable` 不变，故安全），见 `docs/script-tools-design.md`。
- **对象式 + 句柄**：全局 `tl`（编辑器）入口，轨/part/note 是带字段和方法的句柄。**裸属性** = 可读写标量字段（`note.pitch += 12`、`track.isMute = true`）；**带括号方法** = 查询/创建/删除/计算（`part.notes()`、`track.addPart({...})`）。集合方法一律返回**普通 JS 数组**（for-of/下标、有 `.length`、每次新快照）。⚠️ 喂模型的描述**绝不说"链表"**（会诱导 `.first/.next` 误用）。
- **危险包裹对脚本不可见**：`Commit` / part 的 `BeginMergeDirty·EndMergeDirty` / `Notes.BeginMergeNotify·EndMergeNotify` 全由宿主收口——`ScriptContext` 惰性按 part 开 merge 括号，`ScriptRunner` 最外层统一收口：成功且有改动 `Commit`、否则 `DiscardTo(startHead)`。脚本作者（含模型）只写纯语义动作、从不 `commit`。
- **错误回灌**：抛错把信息（JS 错误常带行号；API 用法错误带说明）回模型自纠；`print(...)`/`console.log(...)` 输出捕获回灌。

对象式 API（完整权威文本见 `ScriptApiReference.cs`，`get_script_api` 与 Script 栏 Doc 面都从那里取）：

| 宿主 | 成员（裸属性 = 可读写标量字段；带括号 = 查询/动作。增删一律挂父，无 `x.remove()`） |
|---|---|
| `tl`（编辑器） | `tl.ppq`、`tl.language`、`tl.currentProject()`、`tl.currentPart()`、`tl.selectedParts()`、`tl.playhead()`、`tl.snap(tick)` |
| `project`（`tl.currentProject()`） | `tracks()`、`addTrack(name?)`、`removeTrack(track)`、`tempos()`、`timeSignatures()`、`setTempo(bpm,atTick?)`、`setTimeSignature(num,den,atBar?)` |
| `track` | 字段(读写) `name/isMute/isSolo/gain(dB)/pan`；`parts()`、`addPart({startPos,endPos,name?})`、`removePart(part)`、`set({...})` |
| `part` | 字段(读写) `name/startPos/endPos`(可见窗口绝对 tick；写 startPos 平移整段、写 endPos 缩放右边缘)、(只读) `type`；`soundSource()→{type,id,name,kind,defaultLyric}`、`notes()`、`selectedNotes()`、`notesInRange(s,e)`、`addNote({pos,dur,pitch,lyric?})`、`removeNote(note)`、`samplePitch(s,e,n)`、`setPitchLine(s,e,pts)`、`clearPitch(s,e)`、`automationIds()`、`sampleAutomation(id,s,e,n)`、`setAutomation(id,s,e,pts,default?)`、`clearAutomation(id,s,e)`、`vibratos()`、`addVibrato({...})`、`removeVibrato(vib)`、`set({...})` |
| `note` | 字段(读写) `pos/dur/pitch/lyric`、(只读) `pitchName`；`note.set({...})` |
| `vibrato` | 字段(读写) `pos/dur/frequency/amplitude/phase/attack/release`；`vibrato.set({...})` |

裸属性实时读底层、改完即见新值。**pitch 与 automation 分开**（pitch 对齐 C# `midi.Pitch`；automation 对齐 `midi.Automations`，不含 pitch）。`points` 形如 `[{tick,value}]`。JS camelCase 经 Jint 大小写不敏感映射到 C# PascalCase（含可写属性赋值）。

## 脚本库管理工具（让 agent 造"可复用工具"）

`run_script` 是"现在做一次"；用户要**可复用的功能/命令**（"加个菜单项做……"、"给我做个工具……"）时，模型应把它写成**工具脚本**存库——库里定义了 `getScriptInfo()` 的脚本即"工具"，按 `context` 自动注册进菜单，用户日后点菜单复用：

- **`save_script(name, code)`**：存（新建/覆盖）到库（`%APPDATA%/TuneLab/Scripts`）。**只持久化、不执行**。若 `code` 声明了 `getScriptInfo` 先**预校验**（沙箱 eval 顶层 + 调 `getScriptInfo`，复用 `ScriptTools.InspectSource`，改动原子回退，**先于授权**——不为坏脚本弹卡片）——失败不保存、回灌错误；成功回报注册到哪个菜单。无 `getScriptInfo` 则存为普通一次性脚本（仅 Script 侧栏）。**覆盖已存脚本**是破坏用户外部文件 → 过授权闸门（见下）；新建是加性、不拦。
- **`list_scripts`** / **`read_script(name)`** / **`delete_script(name)`**：列出(标工具+context/带入参/plain) / 读源码 / 删除。**`delete_script` 恒过授权闸门**（删外部文件不可撤销）。

**破坏性外部文件操作的授权（`ToolAuthorization`）**：历史记录管理器只保工程数据、**保不了外部文件**，故 `delete_script`（恒）与 `save_script` 覆盖已存（仅覆盖）也走 `Settings.AgentAuthorization` + 同一确认卡片。与工程写的区别是**无预览-回退**（文件操作不能试运行）：`Auto` 直接做；`ReadOnlyAdvice` 不做、只回报会做什么 + 提示手动/提权；`Confirm` 经卡片裁决（应用本次/始终允许切 Auto/拒绝）。确认回调统一为 `Func<AgentAuthorizationRequest, …>`（`AgentWriteKind` = ProjectEdit / ScriptDelete / ScriptOverwrite），卡片按种类出不同文案。只读工具（含环境感知三件）永不过闸门。

工具脚本约定（喂 LLM 全文在 `ScriptApiReference.cs` 的 "TOOL SCRIPTS" 节）：顶层**只定义函数、无副作用**；`getScriptInfo()` 返回 `{name, category?, author?, version?, context}`（`name` 里读 `tl.language` 本地化）；`main()` 是动作。`context` = `global`（顶部 Scripts 菜单，按 category 分组）/ `note`（钢琴命中音符，目标 `selectedNotes()`）/ `partContent`（钢琴空白，目标 `currentPart()`）/ `part`（编排命中 part，目标 `selectedParts()`）/ `track`（轨道头，目标 `selectedTracks()`）/ `trackContent`（编排空白泳道，目标 `selectedTracks()`）。注册/菜单注入由 `TuneLab.Scripting.ScriptTools` + `TuneLab.UI.ScriptToolMenu` 完成（设计见 `docs/script-tools-design.md`）。

### 脚本闭环：读参数 + 代跑（`get_script_inputs` / `run_saved_script`）

存库的工具脚本可定义 `getInputConfig`（运行前向用户征集参数，见 `script-inputs-and-action-surface.md`）。这让 agent 能**读某脚本要哪些参数、再按名代跑一次**，无需重写代码：

- **`get_script_inputs(name)`**（只读）：eval 顶层 + 调 `getInputConfig`（约定无副作用，误改原子回退），把 `ObjectConfig` 逐字段文本化——名(+标签)、类型/范围/选项、默认值、以及该脚本的**用户上次输入值**（`ScriptInputMemory`）。无 `getInputConfig` 的脚本回报"无入参"。
- **`run_saved_script(name, inputs?)`**（写）：按名读库源码运行。入参解析 = **用户上次值 ← agent 给的 `inputs` 覆盖（稀疏叠加）**，再按当刻 `getInputConfig` 重算 schema 补默认成全量喂 `main`。`inputs` 可整省（用上次/默认）。走 **run_script 同一授权闸门**（`ScriptWriteExecutor`）。
- **参数来源政策**：agent 代跑**不回写** `ScriptInputMemory`——用户手动运行的"上次值"是用户意图，agent 的选择留在其对话历史里，不污染它。
- **稳定 id**：入参记忆键 = `ScriptTools.StableId`（声明 id 合法则用之，否则文件名，与快捷键锚点同一套），UI 侧 `ScriptToolMenu` 与 agent 侧共用同一派生，重命名/重装不丢。

`run_script`（内联）与 `run_saved_script`（命名）到了写这一步是同一件事——都经 `ScriptWriteExecutor` 过分级授权 + 预览 + 写守卫 wait-retry + 结果回报（单一动作面 SSOT）；二者只在"代码/入参从哪来"不同。

## 环境感知（只读工具，让 agent 看见宿主装了什么）

编辑面只让 agent 改工程，但要「推荐插件/音源、指导用户在哪用某能力」，agent 得先**看见宿主环境**。这几个是纯只读环境查询——按架构原则（单一动作面）**只有只读环境查询才新增薄 IAgentTool**，写仍走 `run_script`：

- **`list_extensions`**：读 `ExtensionManager.LoadResults`（已本地化摊平的结构化加载结果），逐条列名/id/版本/作者/类别/加载状态/错误/有无 readme。是「诉求 3」的地基。
- **`get_extension_readme(name)`**：按 id 或名匹配 `ExtensionLoadResult`，`ExtensionReadme.Resolve(dir, lang)` 解析 `README.<lang>.md → README.md` 得路径 → `File.ReadAllText`。readme 可能很长 → 独立按需工具（渐进式披露，同 `get_script_api`），回灌上限 2 万字符截断。
- **`list_sound_sources(kind?, engine?)`**：分两层避免一次性 Init 全部引擎——不给 `engine` 用 `GetAllVoiceEngines/GetProviders/GetDisplayName`（不 Init）列引擎；给 `engine` 用 `GetAllVoiceInfos/GetAllInstrumentInfos`（**仅 Init 该引擎**）列其音源。是「诉求 5」的地基。音源枚举会跑插件代码，故在 **UI 线程**（`Dispatcher.UIThread.InvokeAsync`）执行，对齐宿主其余引擎操作。空引擎 `type=""`（无音源回退）在列表里跳过。

「当前 part 用哪个音源」不在这些工具里，走 `run_script` 的 `part.soundSource()`（只读快照）；「切换 part 音源」是写操作、属后续写通道切片。这些只读工具都不落 undo、不受分级授权闸门约束。

## 维护

- **新增/改脚本 API（≈ 给 agent 加能力）**：在对应句柄类或根（`ScriptApp`/`ScriptProject`/`ScriptHandles`）加 public 成员——标量字段用可读写属性（getter 实时读；setter 内 `ctx.EnsureBracket(midi)` + 改 + `ctx.Bump()`），查询/动作用方法（返回句柄经 `ctx.WrapXxx` 缓存保身份）。增删挂父、不自行 `Commit`、绝对 tick。新成员 PascalCase（脚本里写 camelCase）。收口服务（`EnsureBracket`/`Bump`/`WrapXxx`/`Project`）是 `ScriptContext` 的 internal 成员，只暴露 public 给脚本。
- **脚本模块三层**：`ScriptContext`（收口内核，脚本不可见、全 internal）/ `ScriptRoot.cs`（`ScriptApp`=注入的 `tl`、`ScriptProject`=`tl.currentProject()`）/ `ScriptHandles.cs`（句柄 + 只读快照）。`ScriptTools.cs` = 工具脚本枚举/预校验，`TuneLab/UI/.../ScriptToolMenu.cs` = 菜单注入。
- **新增一个 agent 工具**：写 `IAgentTool`（`Name`/`Description`/`ParametersJsonSchema` + `ExecuteAsync`，用 `ToolJson` 解析参数、catch 转错误文本），在 `AgentSideBarContentProvider.SetProject` 注册。但优先反思：能不能用 `run_script` 表达？能就别加工具。
- **文档权威源 = `Resources/ScriptDoc/en-US.md`**：先改它，再同步 `zh-CN.md`、`ScriptApiReference.cs`（喂 LLM 英文精简）、本文件、`ScriptSideBarContentProvider.FallbackDoc`。
