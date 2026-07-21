# Agent 工具集设计

TuneLab 内置 AI Agent 通过"工具"读取与编辑当前工程。**核心理念：单一动作面（CodeAct）**——编辑工程一律由模型写 JavaScript 经 `run_script` 表达（对象式 `tl` API），读取只保留一个"定向总览"，其余读取也走脚本。曾经的细粒度读写工具（`transpose_notes`/`apply_edits`/`get_part_notes`…）与其门面 `IAgentProjectEditor` 已全部退役——同一件事多条路只会降模型选择准确率、堆 prompt。本文面向维护者，也作为编写工具描述（喂模型）时的一致性参考。

## 工具全集（7 个）

两个面：**操作工程** + **管理脚本库**。

| 工具 | 面 | 作用 |
|---|---|---|
| `get_project_overview` | 操作 | 唯一只读"定向"：PPQ、tempo、拍号、各轨(1-based 编号/名/静音独奏/增益声像/part 数/音符数)。直接读 `IProject`，不经门面。 |
| `run_script` | 操作 | 写一段 JS（对象式 `tl`）做任意读/算/改，整段 = 一个可撤销单位、出错原子回退。 |
| `get_script_api` | 操作 | `run_script` 的按需文档（渐进式披露）：完整 `tl` API + 句柄/tick/收口规则 + 工具脚本约定。写第一段脚本前调一次。 |
| `save_script` | 库 | 把功能写成**工具脚本**(定义 getScriptInfo+main)存库 → 自动注册进菜单复用。只存不执行；声明了 getScriptInfo 则先预校验。 |
| `list_scripts` | 库 | 列库内脚本，标出工具(+context)/普通。 |
| `read_script` | 库 | 读某脚本源码（编辑前）。 |
| `delete_script` | 库 | 删某脚本（同时从菜单移除）。 |

```
模型 ──tool call(JSON)──► IAgentTool 实现
        ├─ get_project_overview ───────────────► IProject（直接读）
        ├─ run_script ──► ScriptRunner ──► Jint + 对象式 API（根 tl + 轨/part/note 句柄）──► IProject
        ├─ get_script_api ─────────────────────► ScriptApiReference.Text
        └─ save/list/read/delete_script ───────► ScriptLibrary / ScriptTools
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

- **`save_script(name, code)`**：存（新建/覆盖）到库（`%APPDATA%/TuneLab/Scripts`）。**只持久化、不执行**。若 `code` 声明了 `getScriptInfo` 先**预校验**（沙箱 eval 顶层 + 调 `getScriptInfo`，复用 `ScriptTools.InspectSource`，改动原子回退）——失败不保存、回灌错误；成功回报注册到哪个菜单。无 `getScriptInfo` 则存为普通一次性脚本（仅 Script 侧栏）。
- **`list_scripts`** / **`read_script(name)`** / **`delete_script(name)`**：列出(标工具+context/plain) / 读源码 / 删除。

工具脚本约定（喂 LLM 全文在 `ScriptApiReference.cs` 的 "TOOL SCRIPTS" 节）：顶层**只定义函数、无副作用**；`getScriptInfo()` 返回 `{name, category?, author?, version?, context}`（`name` 里读 `tl.language` 本地化）；`main()` 是动作。`context` = `global`（顶部 Scripts 菜单，按 category 分组）/ `note`（钢琴命中音符，目标 `selectedNotes()`）/ `partContent`（钢琴空白，目标 `currentPart()`）/ `part`（编排命中 part，目标 `selectedParts()`）/ `track`（轨道头，目标 `selectedTracks()`）/ `trackContent`（编排空白泳道，目标 `selectedTracks()`）。注册/菜单注入由 `TuneLab.Scripting.ScriptTools` + `TuneLab.UI.ScriptToolMenu` 完成（设计见 `docs/script-tools-design.md`）。

## 维护

- **新增/改脚本 API（≈ 给 agent 加能力）**：在对应句柄类或根（`ScriptApp`/`ScriptProject`/`ScriptHandles`）加 public 成员——标量字段用可读写属性（getter 实时读；setter 内 `ctx.EnsureBracket(midi)` + 改 + `ctx.Bump()`），查询/动作用方法（返回句柄经 `ctx.WrapXxx` 缓存保身份）。增删挂父、不自行 `Commit`、绝对 tick。新成员 PascalCase（脚本里写 camelCase）。收口服务（`EnsureBracket`/`Bump`/`WrapXxx`/`Project`）是 `ScriptContext` 的 internal 成员，只暴露 public 给脚本。
- **脚本模块三层**：`ScriptContext`（收口内核，脚本不可见、全 internal）/ `ScriptRoot.cs`（`ScriptApp`=注入的 `tl`、`ScriptProject`=`tl.currentProject()`）/ `ScriptHandles.cs`（句柄 + 只读快照）。`ScriptTools.cs` = 工具脚本枚举/预校验，`TuneLab/UI/.../ScriptToolMenu.cs` = 菜单注入。
- **新增一个 agent 工具**：写 `IAgentTool`（`Name`/`Description`/`ParametersJsonSchema` + `ExecuteAsync`，用 `ToolJson` 解析参数、catch 转错误文本），在 `AgentSideBarContentProvider.SetProject` 注册。但优先反思：能不能用 `run_script` 表达？能就别加工具。
- **文档权威源 = `Resources/ScriptDoc/en-US.md`**：先改它，再同步 `zh-CN.md`、`ScriptApiReference.cs`（喂 LLM 英文精简）、本文件、`ScriptSideBarContentProvider.FallbackDoc`。
