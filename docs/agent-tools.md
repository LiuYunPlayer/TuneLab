# Agent 工具集设计

TuneLab 内置 AI Agent 通过"工具"读取与编辑当前工程。**核心理念：单一动作面（CodeAct）**——编辑工程一律由模型写 JavaScript 经 `run_script` 表达（对象式 `tl` API），读取只保留一个"定向总览"，其余读取也走脚本。曾经的细粒度读写工具（`transpose_notes`/`apply_edits`/`get_part_notes`…）与其门面 `IAgentProjectEditor` 已全部退役——同一件事多条路只会降模型选择准确率、堆 prompt。本文面向维护者，也作为编写工具描述（喂模型）时的一致性参考。

**归属判据（脚本面 vs 工具面）——按「谁需要」分**：分界不是"读/写"（`tl` 本就有大量读，如 `part.notes()`），而是**"这是不是用户会要的能力"**：
- **用户会要的能力**（工程编辑、可复用命令、快捷键、未来 MCP）→ 走 `tl` 脚本动作面。它同时是用户的脚本面与 agent 的写路径，故"帮用户写脚本+绑快捷键"和"agent 顺手做一件事"是同一件事。
- **只有 agent 自身推理需要、用户不会去脚本化的**（枚举环境、读元数据/schema/introduction、定位总览）→ 加薄只读 `IAgentTool`。
- **护栏**：一切**工程状态的修改**天然属"用户会要的"，恒走 `tl`，绝不因"只 agent 需要"另开专用工程写工具——否则碎掉单一撤销单位 + 授权闸门 + 模型动作词汇。
- **SSOT 约束的是执行面、不是工具数**：多道工具门可以（`run_script` 内联、`run_saved_script` 按名），只要都汇进同一受闸门执行面（`ScriptWriteExecutor` → `ScriptContext` 那次 `Commit`）。库管理工具（`save/list/read/delete_script`）不改工程状态、也非 `tl` 可脚本，故为工具。

## 工具全集（25 个）

三个面：**操作工程** + **管理脚本库** + **环境感知（只读为主，含设置/快捷键助手的写口）**（外加一个**探测沙箱** `run_in_sandbox`，可丢弃工程里探静态读够不着的东西）。

| 工具 | 面 | 作用 |
|---|---|---|
| `get_project_overview` | 操作 | 唯一只读"定向"：PPQ、tempo、拍号、各轨(1-based 编号/名/静音独奏/增益声像/part 数/音符数)。直接读 `IProject`，不经门面。 |
| `run_script` | 操作 | 写一段 JS（对象式 `tl`）做任意读/算/改，整段 = 一个可撤销单位、出错原子回退。 |
| `get_script_api` | 操作 | `run_script` 的按需文档（渐进式披露）：完整 `tl` API + 句柄/tick/收口规则 + 工具脚本约定。写第一段脚本前调一次。 |
| `export_project` | 操作 | 把当前工程写成一个文件（`importTracks` 的对偶）。格式由扩展名定（tlp/tlpx 全保真；mid/midi 内置但只承载 musical 部分；再加已装格式插件）。**恒过授权闸门**（路径任意 = 能写用户磁盘任何地方）；**不是"保存"**（不改工程保存路径、不清未保存态）；**不能导出音频**。 |
| `save_script` | 库 | 把功能写成**工具脚本**(定义 getScriptInfo+main)存库 → 自动注册进菜单复用。只存不执行；声明了 getScriptInfo 则先预校验。 |
| `list_scripts` | 库 | 列库内脚本，标出工具(+context)/普通，并标注哪些**带入参**(定义了 getInputConfig)。 |
| `read_script` | 库 | 读某脚本源码（编辑前）。 |
| `delete_script` | 库 | 删某脚本（同时从菜单移除）。 |
| `get_script_inputs` | 库 | 读某脚本的入参 schema（逐字段名/类型/默认/范围·选项）+ 用户**上次输入值**。只读（只 eval getInputConfig，无副作用）。run_saved_script 前调。 |
| `run_saved_script` | 库 | 按库名跑已存脚本（= 替用户按那个菜单项），`inputs` 可省：给了覆盖在上次值上再补默认，没给用上次/默认。走 run_script 同一授权闸门。 |
| `list_extensions` | 感知 | 列用户已装扩展：包级(名/id/版本/作者/类别/加载状态/包描述) + 逐能力位(身份/**一句话摘要**/**本次结局 DISABLED·FAILED·SKIPPED**/冲突态)。返回前补齐缺的摘要（**短文档直接用作者原话，长文档才调一次模型**、按内容哈希缓存），补不完如实回报。 |
| `get_extension_introduction` | 感知 | 读**某个能力位**的 introduction（作者写的 markdown，manifest 声明路径、上限截断）；没写则**标注式降级**给包级 description 并声明它是二手参考；同身份跨包时要 `packageId` 消歧。按需拉取（渐进式披露）。 |
| `list_sound_sources` | 感知 | 三层钻取：不给 `engine` → 列引擎(不 Init)；给 `engine` → 列该引擎音源(id/名/描述)；给 `engine`+`source` → 读该音源参数 schema(part/note/自动化/音素级，各带类型/范围/默认)。后两层仅 Init 该引擎。`kind` 可选过滤。 |
| `list_effects` | 感知 | 分层枚举效果器：不给 `engine` → 列 effect 引擎(不 Init)；给 `engine` → 用 part-free 空 context 纯静态读其参数 schema(静态属性 + 自动化轨，各带类型/范围/默认，仅 Init 它)。 |
| `list_settings` | 感知 | 列宿主应用设置（设置窗那些）：键/标签(含本地化)/所在页/允许值(类型·范围·选项)/当前值/默认值/是否需重启/agent 可否改。用于「在哪调怎么调」与 set_setting 前置。直接读 `SettingsRegistry`。 |
| `set_setting` | 感知(写) | 按键改一项应用设置 + 落盘。值按该条目声明校验（范围/下拉成员/布尔/路径存在）；**过授权闸门**（改用户应用配置、非工程数据、历史记录救不回）；部分项声明为 agent 不可写。 |
| `list_keybindings` | 感知 | 列可绑定命令：id/本地化名/作用域/生效手势(存储令牌+显示形)/是默认还是用户改过/同域冲突，附手势语法。直接读 `Keymap`。 |
| `set_keybinding` | 感知(写) | 改一条绑定：绑手势 / `gesture:""` 解绑 / `reset:true` 恢复默认。同域冲突默认拒绝（要 `replaceConflict:true` 才夺键并解除原命令）；**过授权闸门**；即时生效无需重启。 |
| `list_extension_routing` | 感知 | 列被多包争用的扩展身份：各候选包 / 当前生效 / 是用户选定还是默认规则。**排障用**（"插件不生效"常是被顶替，非没装好）。读 `ExtensionRouting.GetConflicts`。 |
| `set_extension_routing` | 感知(写) | 为某争用身份选定提供包（空=清除回默认规则）。**过授权闸门**；即时落盘但**重启后生效**。 |
| `set_extension_enabled` | 感知(写) | 把某个已装包（或包内某一个能力位）**关掉但不卸载**；`capability` 省略=整包。**过授权闸门**；即时落盘但**重启后生效**。读面在 `list_extensions` 的 DISABLED 注记。 |
| `list_extension_settings` | 感知 | 列哪些扩展声明了**自己的设置**（设置窗「扩展」页），给定一个则列其字段：键/标签/类型·范围·选项/默认/当前值。**密钥字段只报 (set)/(not set)、绝不回灌明文**。schema 取自插件 `GetSettingsConfig`。 |
| `set_extension_setting` | 感知(写) | 改某扩展一个设置字段 + 落盘 + `ApplyOne` 立即回喂。按字段 config 校验；**密钥字段一律拒写**；**过授权闸门**。 |
| `run_in_sandbox` | 探测 | 在一个**可丢弃无头工程**里跑 JS（同一 `tl` 面 + `sandbox` 全局），够到静态读够不着的东西（尤其**真实音素**：挂真音源+合法歌词+触发合成后才存在）。写入不碰用户数据、**不过授权闸门**。见下「探测沙箱」节。 |
| `ask_user_question` | 交互 | 问用户一个问题并**在本轮之内等到答案**再继续（不切两轮、不丢已有进展）。选项 + 自由文本框，单选/多选。**不改工程状态、不过授权闸门**（它不是写操作，是问）。见下「问用户」节。 |

```
模型 ──tool call(JSON)──► IAgentTool 实现
        ├─ get_project_overview ───────────────► IProject（直接读）
        ├─ run_script ─────────┐
        │                      ├─► ScriptWriteExecutor（授权闸门/预览/收口）──► ScriptRunner ──► Jint + 对象式 API ──► IProject
        ├─ run_saved_script ───┘         （run_saved_script 先按名读库源码 + 解析入参再进闸门）
        ├─ get_script_api ─────────────────────► ScriptApiReference.Text
        ├─ export_project ─────────────────────► FormatsManager.SerializeNative ──► ToolAuthorization ──► FileStream（序列化先于闸门：失败不打扰用户）
        ├─ get_script_inputs ──────────────────► ScriptRunner.GetInputConfig + ScriptInputMemory（只读）
        ├─ save/list/read/delete_script ───────► ScriptLibrary / ScriptTools
        ├─ list_extensions / get_extension_introduction ─► ExtensionManager.LoadResults（含逐条目 Entries，只读）
        │        └─ 返回前：ExtensionSummaryFiller（短文档原话直采 / 长文档串行+预算旁路请求）──► ExtensionSummaryCache（内容寻址）
        ├─ list_sound_sources ─────────────────► VoicesManager / InstrumentsManager（只读；给 engine 才 Init；给 engine+source 合成 context 求参数 schema）
        ├─ list_effects ───────────────────────► EffectManager（只读，给 engine 才 Init + 空 context 求 schema）
        ├─ list_settings ──────────────────────► SettingsRegistry（只读；声明即枚举源）
        ├─ set_setting ────────────────────────► ToolAuthorization ──► SettingItem.TrySetValue + Settings.Save
        ├─ list_keybindings ───────────────────► Keymap（只读：Commands/Effective/HasOverride/SameScopeConflictPeers）
        ├─ set_keybinding ─────────────────────► ToolAuthorization ──► Keymap.Rebind / ResetToDefault（自带落盘 + Changed 广播）
        ├─ list_extension_routing ─────────────► ExtensionRouting.GetConflicts / GetSelected（只读）
        ├─ set_extension_routing ──────────────► ToolAuthorization ──► ExtensionRouting.SetSelected（落盘，重启生效）
        ├─ set_extension_enabled ──────────────► ToolAuthorization ──► ExtensionActivation.SetPackageEnabled / SetEntryEnabled（落盘，重启生效）
        ├─ list_extension_settings ─────────────► ExtensionSettingsManager.GetEntries + 插件 GetSettingsConfig（只读；密钥只报有无）
        ├─ set_extension_setting ──────────────► ToolAuthorization ──► ExtensionSettingsStore.Save + ExtensionSettingsManager.ApplyOne
        └─ run_in_sandbox ─────────────────────► SandboxHost（专用线程 + 可泵 SyncContext + 可丢弃 IProject；tl + sandbox 全局；不过授权闸门）
```
（共享格式在 `AgentToolFormat.cs`：`EngineCatalog.AppendEngineList` 三类引擎列表共用；`ConfigText.Describe/FormatValue` 各 config 类型一致措辞（复用进脚本入参 `SavedScriptSupport`）；`SchemaText.AppendProperties/AppendAutomations/AppendPhonemes` 把引擎声明的参数组文本化，effect 与音源参数 schema 共用。）

- **IAgentTool**（`TuneLab/Agent/IAgentTool.cs`）：工具对模型的声明（名称/描述/参数 JSON Schema）+ 执行入口。实现薄：解析参数 JSON、干活、把结果/错误格式化回灌。
- **AgentRunner**（`TuneLab/Agent/AgentRunner.cs`）：provider 无关的多轮工具循环，只依赖 `IAgentModelSession`。模型适配器是宿主内部模块（不开放为插件类型），接入新 LLM 提供方见 [agent-model-adapters.md](agent-model-adapters.md)。
- 工具在 `AgentSideBarContentProvider.SetProject` 处用当前 `IProject` + "当前 part/量化/语言"访问器实例化并注册（工程切换即重建）。

## 寻址与单位约定

- **`get_project_overview` 用 1-based 序号**（"第 1 轨"即首轨，贴合用户认知）展示轨道。这是模型与用户对话里指代轨道的口径。
- **脚本（`tl` API）用句柄 + 绝对 tick**：集合方法（`part.notes()` 等）返回临时句柄数组，按引用施改、无 1-based 编号；位置/时长一律**绝对（全局）tick**（`tl.ppq` 取 PPQ=480），音高 MIDI。句柄仅当次运行有效（数据层对象无持久 id），脚本源码不得内嵌句柄字面量。坐标换算在各句柄内（落数据减 part 锚点 `pos`、读时加回），脚本作者不碰。part 自身的几何按数据层原形暴露：可写的三原始字段 `pos`/`startOffset`/`endOffset` + 只读派生 `startPos`/`endPos`/`dur`——`pos` 既是锚点也是内容坐标原点，故改它就是"平移整段、内容跟随"。
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
| `project`（`tl.currentProject()`） | 字段(读写，**导出设置**) `exportPath/exportFileName/exportFormat(wav|mp3|flac|ogg)/exportSampleRate/exportBitDepth/exportBitrate/masterExportEnabled/masterExportChannels`；`tracks()`、`addTrack(info?,index?)`、`insertTrack(track,index?)`、`removeTrack(track)→track`、`importTracks(path)→[track]`(从文件导入全部轨、加法式并进、保留当前时基/原始 tick、返回新轨；格式 tlp/tlpx/mid/midi+插件)、`tempos()`、`timeSignatures()`、`setTempo(bpm,atTick?)`、`setTimeSignature(num,den,atBar?)`、`removeTempo(atTick)`、`removeTimeSignature(atBar)`(该处无标记则报错；首个标记 = 基准速度/拍号，不可删) |
| `track` | 字段(读写) `name/isMute/isSolo/gain(dB)/pan/asRefer/color`、(读写，**导出设置**) `exportEnabled/exportChannels(1\|2)`；`getInfo()`(不含导出开关，见下)、`parts()`、`addPart(info)`、`insertPart(part)`(**可跨轨** = 迁移)、`removePart(part)→part` |
| `part` | 几何(读写) `pos`(锚点绝对 tick，也是内容坐标原点 → 赋值即平移整段)、`startOffset`、`endOffset`；(只读派生) `startPos/endPos/dur`；其它(读写) `name/gain`(dB,part 级)、(只读) `type`；`getInfo()`、`track()→track`(向上取所属轨，只读)、`soundSource()→{type,id,name,kind,defaultLyric}`、`setSoundSource({kind,type,id})`(切音源；未知报错、空清源)、`effects()`、`addEffect(info,index?)`、`insertEffect(effect,index?)`、`removeEffect(effect)→effect`、`moveEffect(effect,index)`(效果链增删排)、`getProperty(key)→值\|null`、`setProperty(key,value)`(per-part 声明参数，键/范围见 list_sound_sources)、`notes()`、`selectedNotes()`、`addNote(info)`、`insertNote(note)`、`removeNote(note)→note`、`samplePitch(s,e,n)`、`setPitchLine(s,e,pts)`、`clearPitch(s,e)`、`automationIds()`(连续轨)、`sampleAutomation(id,s,e,n)`、`setAutomation(id,s,e,pts,default?)`、`clearAutomation(id,s,e)`、`piecewiseAutomationIds()`(分段轨)、`samplePiecewiseAutomation(id,s,e,n)`、`setPiecewiseAutomationLine(id,s,e,pts)`、`clearPiecewiseAutomation(id,s,e)`、`vibratos()`、`addVibrato(info)`、`insertVibrato(vib)`、`removeVibrato(vib)→vibrato` |
| `note` | 字段(读写) `pos/dur/pitch/lyric/pronunciation`(显式发音覆盖，空串=无覆盖、歌词原文直达引擎由其自行 G2P)、(只读) `pitchName/hasLockedPhonemes`、(读写) `bodyOffset`(秒，写自动固定)；`getInfo()`、`part()→part`(向上取所属 part，只读；`vibrato.part()`/`effect.part()` 同理)、`getProperty(key)→值\|null`、`setProperty(key,value)`(per-note 声明参数，见 list_sound_sources)、`phonemes()→[phoneme]`、`addLeadingPhoneme(info)`、`addBodyPhoneme(info)`(引导/主体是两个独立列表，故两个方法)、`removePhoneme(ph)`、`lockPhonemes()`、`clearPhonemes()`(音素只读来自引擎，首次写自动固定；lock 与曲线侧 `part.lockPitch/lockAutomation` 同一动词同一范式) |
| `phoneme`（`note.phonemes()` 的一项，voice 专属） | 字段(只读) `leading`(bool)、(读写) `symbol/duration(秒)/stretchWeight`(0=刚性辅音/>0=可伸元音，写任一自动钉死)；`getInfo()`、`getProperty(key)→值\|null`、`setProperty(key,value)`(per-phoneme 声明参数，键见 list_sound_sources 音素 slot)。**按位置定址**：增删改变其后下标，结构变更后重取 `note.phonemes()` |
| `vibrato` | 字段(读写) `pos/dur/frequency/amplitude/phase/attack/release`；`getInfo()`、`affectedAutomations()→{轨id:振幅}`、`affectedEffectAutomations()→{effect id:{轨id:振幅}}`、`setAmplitude(id,amp,effect?)`、`removeAmplitude(id,effect?)`(影响表：本颤音把振幅施加到哪些参数轨；省略 effect = 音源级轨) |
| `effect`（`part.effects()` 的一项） | 字段(读写) `isEnabled`(false=旁路)、(只读) `type/name/id/index`(链中 0-based 位)；`getInfo()`、`getProperty(key)→值\|null`、`setProperty(key,value)`(number/bool/string，键/范围见 list_effects)；`automationIds()`、`sampleAutomation(id,s,e,n)`、`setAutomation(id,s,e,pts,default?)`、`clearAutomation(id,s,e)`、`piecewiseAutomationIds()`、`samplePiecewiseAutomation(id,s,e,n)`、`setPiecewiseAutomationLine(id,s,e,pts)`、`clearPiecewiseAutomation(id,s,e)`(本 effect 的参数自动化曲线，形状同 part 级) |

**导出设置是「设置项」、不入撤销栈**：`project.export*` / `masterExport*` 与 `track.exportEnabled/exportChannels` 在数据层是普通属性、写它不产生命令，故 `Ctrl+Z` 不会把导出路径退回去（与在导出侧栏里改它们一致）。但「整段脚本原子」仍成立——**出错或 preview 时由 `ScriptContext` 写前留底、回退时还原**。归属理由按本文开头的判据：「跑一段脚本把导出各项设成我的预设」是**用户会要的可复用命令**（还会绑快捷键），故在脚本面；而**真正写出音频文件**是另一件事，仍走 `export_project` 工具。它们刻意**不进 `track.getInfo()`**——设置项不属于"轨的内容"，复制一条轨不带导出开关。

裸属性实时读底层、改完即见新值。**pitch 与 automation 分开**（pitch 对齐 C# `midi.Pitch`；automation 对齐 `midi.Automations`，不含 pitch）。`points` 形如 `[{tick,value}]`。JS camelCase 经 Jint 大小写不敏感映射到 C# PascalCase（含可写属性赋值）。

#### info 层（复制的正解）与游离句柄（移动的正解）

脚本面的形状与数据层的**三段式**对齐：`Info`（纯数据，改它不进撤销栈）→ `CreateX(info)`（建游离实体）→ `InsertX(entity)`（入树，这步才进回退栈）。于是有两条**语义不同、都必需**的落地路：

| 路径 | 语义 | 中间物 | 能落地几次 |
|---|---|---|---|
| `addX(info[, index])` | **复制 / 新建**（新身份） | info（纯数据，随便改） | 任意多次 |
| `insertX(entity[, index])` | **移动**（保持身份） | 游离实体（只读） | 一次（一个对象一个父） |

- **每个句柄都有 `getInfo()`**，产出**普通 JS 对象**（嵌套：part 的 info 带着音源/音符/音高线/各条自动化/颤音/effect 链/两级属性/音素）；每个父的 `addX(info)` 收同一形状。故「复制这一轨」= `project.addTrack(t.getInfo())`，**一个字段都不用手搬**——这是它替代逐字段重建的唯一原因：逐字段搬必然漏，而漏了还会以为复制成功了（静默丢保真）。
- **契约独立于 SDK DTO**：脚本面 schema 是自己的（camelCase + 绝对 tick），由 `ScriptInfo.cs` 显式桥接到 `TuneLab.SDK` 的 `*Info`。SDK DTO 是 `PublicAPI.Shipped.txt` 守着的冻结 ABI、且用锚点三元组 + PascalCase，直接暴露会让脚本面变成 ABI 的一部分。
- **`removeX` 返回游离句柄**：对象仍活着、仍可读（`getInfo()` 照用），只是没有父。「删除」= 摘出后不插回；「移动」= 摘出后 `insertX`。游离态**不可写**（数据层纪律：未 Attach 的对象属性 Set 不记录命令，改了回退不掉），写入在 accessor 处拦下并指路"先插回"。
- **只有 part 能换父**（`IPart.Track` 可写）：`track.removePart(p)` + `另一轨.insertPart(p)` = 跨轨迁移。note/颤音/effect 的所属 part 在数据层由构造决定，`insertX` 只能插回原父，跨父走 info 路。
- **校验的位置**：info 阶段零校验（纯数据随便乱来），**落地那刻**才校验，且只校验内部不变式（`dur>0`、pitch 值域、part 类型判别）。**存在性校验只在"按名字指定一个引擎"的显式入口**（`part.setSoundSource`、`part.addEffect` 的顶层 `type`）；嵌在 info 树里的 `soundSource`/`effects` 刻意不校验——那条路要能忠实搬运孤儿数据（引擎卸载后工程照样能开、复制照样保真）。

## 脚本库管理工具（让 agent 造"可复用工具"）

`run_script` 是"现在做一次"；用户要**可复用的功能/命令**（"加个菜单项做……"、"给我做个工具……"）时，模型应把它写成**工具脚本**存库——库里定义了 `getScriptInfo()` 的脚本即"工具"，按 `context` 自动注册进菜单，用户日后点菜单复用：

- **`save_script(name, code)`**：存（新建/覆盖）到库（`%APPDATA%/TuneLab/Scripts`）。**只持久化、不执行**。若 `code` 声明了 `getScriptInfo` 先**预校验**（沙箱 eval 顶层 + 调 `getScriptInfo`，复用 `ScriptTools.InspectSource`，改动原子回退，**先于授权**——不为坏脚本弹卡片）——失败不保存、回灌错误；成功回报注册到哪个菜单。无 `getScriptInfo` 则存为普通一次性脚本（仅 Script 侧栏）。**覆盖已存脚本**是破坏用户外部文件 → 过授权闸门（见下）；新建是加性、不拦。
- **`list_scripts`** / **`read_script(name)`** / **`delete_script(name)`**：列出(标工具+context/带入参/plain) / 读源码 / 删除。**`delete_script` 恒过授权闸门**（删外部文件不可撤销）。

**工程之外的写的授权（`ToolAuthorization`）**：历史记录管理器只保工程数据、**保不了外部文件与应用配置**，故 `delete_script`（恒）、`save_script` 覆盖已存（仅覆盖）、`set_setting`（恒）、`set_keybinding`（恒）、`set_extension_routing`（恒）、`set_extension_setting`（恒）、`set_extension_enabled`（恒）也走 `Settings.AgentAuthorization` + 同一确认卡片。与工程写的区别是**无预览-回退**（这些操作不能试运行）：`Auto` 直接做；`ReadOnlyAdvice` 不做、只回报会做什么 + 提示手动/提权；`Confirm` 经卡片裁决（应用本次/始终允许切 Auto/拒绝）。确认回调统一为 `Func<AgentAuthorizationRequest, …>`（`AgentWriteKind` = ProjectEdit / ScriptDelete / ScriptOverwrite / SettingChange / KeybindingChange / RoutingChange / ExtensionSettingChange / ProjectExport(+Overwrite) / ExtensionActivationChange；`NewValue` 供文案点名新值、`SecondaryTarget` 是定位/说明本次改动所需的第二个对象[夺键时=被顺带解绑的那个命令；启停单个能力时=它所属的包名]），卡片按种类出不同文案（改设置/改快捷键的卡片显示**本地化行标/命令名**、模型侧才用键与 id）。只读工具（含环境感知的枚举件）永不过闸门。

工具脚本约定（喂 LLM 全文在 `ScriptApiReference.cs` 的 "TOOL SCRIPTS" 节）：顶层**只定义函数、无副作用**；`getScriptInfo()` 返回 `{name, category?, author?, version?, context}`（`name` 里读 `tl.language` 本地化）；`main()` 是动作。`context` = `global`（顶部 Scripts 菜单，按 category 分组）/ `note`（钢琴命中音符，目标 `selectedNotes()`）/ `partContent`（钢琴空白，目标 `currentPart()`）/ `part`（编排命中 part，目标 `selectedParts()`）/ `track`（轨道头，目标 `selectedTracks()`）/ `trackContent`（编排空白泳道，目标 `selectedTracks()`）。注册/菜单注入由 `TuneLab.Scripting.ScriptTools` + `TuneLab.UI.ScriptToolMenu` 完成（设计见 `docs/script-tools-design.md`）。

### 脚本闭环：读参数 + 代跑（`get_script_inputs` / `run_saved_script`）

存库的工具脚本可定义 `getInputConfig`（运行前向用户征集参数，见 `script-inputs-and-action-surface.md`）。这让 agent 能**读某脚本要哪些参数、再按名代跑一次**，无需重写代码：

- **`get_script_inputs(name)`**（只读）：eval 顶层 + 调 `getInputConfig`（约定无副作用，误改原子回退），把 `ObjectConfig` 逐字段文本化——名(+标签)、类型/范围/选项、默认值、以及该脚本的**用户上次输入值**（`ScriptInputMemory`）。无 `getInputConfig` 的脚本回报"无入参"。
- **`run_saved_script(name, inputs?)`**（写）：按名读库源码运行。入参解析 = **用户上次值 ← agent 给的 `inputs` 覆盖（稀疏叠加）**，再按当刻 `getInputConfig` 重算 schema 补默认成全量喂 `main`。`inputs` 可整省（用上次/默认）。走 **run_script 同一授权闸门**（`ScriptWriteExecutor`）。
- **参数来源政策**：agent 代跑**不回写** `ScriptInputMemory`——用户手动运行的"上次值"是用户意图，agent 的选择留在其对话历史里，不污染它。
- **稳定 id**：入参记忆键 = `ScriptTools.StableId`（声明 id 合法则用之，否则文件名，与快捷键锚点同一套），UI 侧 `ScriptToolMenu` 与 agent 侧共用同一派生，重命名/重装不丢。

`run_script`（内联）与 `run_saved_script`（命名）到了写这一步是同一件事——都经 `ScriptWriteExecutor` 过分级授权 + 预览 + 写守卫 wait-retry + 结果回报（单一动作面 SSOT）；二者只在"代码/入参从哪来"不同。

## 环境感知（让 agent 看见宿主装了什么）

编辑面只让 agent 改工程，但要「推荐插件/音源、指导用户在哪用某能力」，agent 得先**看见宿主环境**。这几件除 `set_setting` 外都是纯只读查询——按架构原则（单一动作面）**工程状态的修改恒走 `run_script`，绝不另开工程写工具**；`set_setting` 改的是**宿主应用配置**（不是工程状态、没有也不该有 `tl` 面），故是工具面的一个写口，并自带授权闸门：

- **`list_extensions`**：读 `ExtensionManager.LoadResults`（已本地化摊平的结构化加载结果），按包一条列名/id/版本/作者/类别/加载状态/错误/包级 description，并内嵌逐 **能力位** 行（`kind:identity` 身份清单、显示名、有无 introduction、routing 冲突态）。两层粒度各有其用：**包**承载排障与管理事实（加载状态/sdk 门/卸载单位/routing 的选择值也是包 id），**能力位**才是推荐与使用时真正引用的东西。是「诉求 3」的地基。
- **`get_extension_introduction(capability, packageId?)`**：在各包 `Entries` 里按 `kind:identity` / 裸 identity / 显示名匹配条目（三种写法的匹配与消歧收口在 `ExtensionCapabilityLookup`，与 `set_extension_enabled` 共用一份——它们认的写法必须完全一致，那是对模型的契约，一处多认一种、另一处不认，模型就会在工具间来回试错） → 读其 `IntroductionPath`（加载期已按 manifest 声明 + `localizations` 语言覆盖解析成绝对路径）→ `File.ReadAllText`。**粒度是能力位而非包**（一个包多个能力各有各的介绍）；同身份跨包并存时**不猜**、列候选要求传 `packageId`（同 `list_extension_settings` 的规矩）。介绍可能很长 → 独立按需工具（渐进式披露，同 `get_script_api`），回灌上限 2 万字符截断。**宿主只认 manifest 声明的 introduction**：包里的 README 是作者面向仓库读者的自留文件，不再当元数据；回报里点明该文本出自作者、非宿主保证。
- **`list_sound_sources(kind?, engine?, source?)`**：三层钻取避免一次性 Init 全部引擎——不给 `engine` 用 `GetAllVoiceEngines/GetProviders/GetDisplayName`（不 Init）列引擎；给 `engine` 用 `GetAllVoiceInfos/GetAllInstrumentInfos`（**仅 Init 该引擎**）列其音源；给 `engine`+`source` 读该音源**参数 schema**（诉求 4/5 的地基）。音源枚举/schema 求值跑插件代码，故在 **UI 线程**执行；空引擎 `type=""` 在列表里跳过。
  - **音源参数 schema（第三层，A4）**：config 是 **voiceId 的函数**（不同音源可声明不同参数），且 `VoicesManager.Declare` 对**未知 id 静默回退空引擎**给出误导性空 schema——故必须按「引擎 + 真实音源 id」读、先 `TryGetVoiceInfo` 校验 source 存在。`VoicesManager`/`InstrumentsManager` 的 `Get*Config` 是 public（但 `GetInitedEngine` 是 private，故走 manager 方法而非直取引擎，与 effect 不同）。用 Agent 层自建的 **part-free 合成 context**（真 `VoiceId` + 空 `Notes`/`PartProperties`/`Automations`，见 `SoundSourceInfoTools` 的 `StaticVoicePartContext` 等）纯静态调 5 个（voice）/4 个（instrument，无音素/歌词）声明方法。schema 是**默认值版**（条件化 schema 只呈现默认分支，同 effect 上限）。**phoneme 是静态读的天花板**：其 slot 来自 note 里**真实音素**（数据驱动），空 note 恒空——除非引擎恰好静态声明了 slot schema，否则如实标注"需合成后才可见"、**不造假 note**（phoneme 的真发现走「探测沙箱」`run_in_sandbox`，见下节：agent 在可丢弃工程里挂源/造合法歌词/触发合成/读回显）。
- **`list_effects(engine?)`**：`EffectManager` 严格镜像音源管理器（`GetAllEffectEngines/GetProviders/GetDisplayName/GetInitedEngine`），故列引擎层与音源同格式（共用 `EngineCatalog`）。与音源不同——effect **无「音源目录」**（一个引擎 = 一种效果器类型），第二层列的是**参数 schema**：给 `engine` 时 `GetInitedEngine`（**仅 Init 它**）→ 传一个 **part-free 的空 `IEffectSynthesisPropertyContext`**（空 `IEffectSynthesisView`：无改过的值 → 各参数取引擎默认）→ 调引擎三个纯函数声明方法 `GetPropertyConfig`（静态属性 `ObjectConfig`）/`GetAutomationConfigs`（可编辑自动化轨）/`GetSynthesizedParameterConfigs`（只读回显轨），逐参数输出类型/范围/默认。是「诉求 6」的地基。要点：宿主自带的 `EffectPropertyContext` 绑 part 且 private，不可复用，故 Agent 层自建极简空 context；effect 无内建引擎（全来自插件）；条件化 schema 只能拿「默认值版」（静态枚举固有上限）；读 schema 必须 Init 引擎（跑插件代码）→ UI 线程。

「当前 part 用哪个音源」不在这些工具里，走 `run_script` 的 `part.soundSource()`（只读快照）；**「切换 part 音源」已落地**为写原语 `part.setSoundSource({kind,type,id})`（过分级授权闸门、含存在校验，见 run_script 面）；**「读写 part 的 effect 链」也已落地**=`part.effects()/addEffect(type)/removeEffect/moveEffect` + `effect` 句柄（`isEnabled` bypass、`getProperty/setProperty` 改参；类型/参数 schema 仍从 `list_effects` 读，两道门并存）。**「voice/instrument 的 part/note/phoneme 参数改写」也已落地**=`part.getProperty/setProperty`、`note.pronunciation`、`note.getProperty/setProperty`、音素 `note.phonemes()/addPhoneme/removePhoneme/lockPhonemes/clearPhonemes` + `phoneme` 句柄（`symbol/duration/stretchWeight` + `getProperty/setProperty`）——schema 从 `list_sound_sources` 读；音素合成态只读、首次写自动固定（数据层 LockPhonemes）。**effect 的时间轴自动化曲线读写也已落地**=`effect.automationIds()/sampleAutomation/setAutomation/clearAutomation`（对齐 C# `IEffect.Automations`，与 part 级 automation 逐一平行、同 part 相对 tick 空间与绝对值语义，采样复用 `ScriptPart.SampleTicks`；只是目标从 voice 换成链中某 effect）。注：apply_edits 层的只读 `get_parameter`/`IsEffectiveAutomation` 仍只认 voice 级、未走 effect 路由（脚本面已覆盖，工具面要扩再补）。**「把合成产物固定成用户可编辑数据」也已落地**=`part.lockPitch()`（合成音高 → 音高曲线）/ `part.lockAutomation(id)`（回显 → 同 id 可编辑轨）/ `part.hasSynthesizedParameter(id)` + effect 同族三件（作用域为链中某 effect 的轨），与工具栏那支固定笔刷共用同一份 `SynthesisLock`（扣 vibrato 偏移、简化、裁剪、秒→tick 换算全同）。两点是给 agent 的显式性：区间两参**成对**（都省 = 整条 part），返回 `bool` = **有没有真的固定到东西**——没有产物（多半是还没合成，而脚本面没有触发合成的原语）是 no-op 返 `false`，用法错误（未知 id / 无配对回显）才报错。**「导入」也已落地**=`project.importTracks(path)`（从文件导入全部轨、加法式并进当前工程、保留当前时基/原始 tick 落位、返回新轨句柄；格式 tlp/tlpx/mid/midi+插件；只读入文件+加法式写工程、一个可撤销单位、失败原子回退）；**「导出」也已落地**，但在**工具面**=`export_project`（见下节，写外部文件、不改工程状态）。这些只读工具都不落 undo、不受分级授权闸门约束。

### 导出（`export_project`）

`importTracks` 的对偶：那个读入文件，这个写出文件。

- **为什么在工具面而不是 `tl` 脚本原语**（与"工程编辑恒走 `run_script`"的护栏不冲突）：护栏约束的是**工程状态的修改**，而导出**不改工程状态一分一毫**——它与 `save_script`/`delete_script`（同样写外部文件、同样不碰工程数据）同类，那两件本来就在工具面，故这是**循例而非例外**。另有一条硬理由：授权闸门是 async（要等用户点确认卡片），而脚本经 Jint **同步**跑在 UI 线程，中途阻塞等卡片会**自死锁**。
  将来若用户真需要在脚本里导出（如"每轨各存一个 midi"），再加 `tl` 原语并配**延迟写**机制（脚本内只登记意图 → 脚本成功结束后统一过闸门执行 → 脚本出错则一并丢弃、文件从未写），与本工具加性并存。
- **恒过闸门**（`AgentWriteKind.ProjectExport` / `ProjectExportOverwrite`）：与 `save_script`「新建不拦、只拦覆盖」不同——导出的 **path 是任意的**，agent 能往用户磁盘任何地方写，故每次都问。落到已存路径单列 `Overwrite` 一档，让卡片**另起一句**说"会替换现有文件"（别混在同一句里说轻了）；卡片必须摆出**完整落地路径**，那是用户判断"这一下写到哪"的唯一依据。
- **序列化先于授权**：`FormatsManager.SerializeNative` 本就缓冲进 `MemoryStream`（原子写语义——失败时目标文件尚未开写），故先序列化、失败直接报错，不为一个注定失败的请求白弹一次卡片（同 `save_script` 把 `getScriptInfo` 预校验放在授权之前）。
- **native / foreign 统一走 `SerializeNative`**：`.tlp/.tlpx` 带上 `EditorInfo`(播放头) + `ExportConfigInfo` 保真；`.mid` 等 foreign 由它内部自动降级到纯 musical `Serialize`。与「另存为」同一条路径。`ExportConfigInfo` 的互转已从 `Project` 实例方法提为 `IProjectExtension.GetExportConfig/SetExportConfig` 扩展方法（只读写 `IProject` 已有的 8 个 `Export*` 属性），故持 `IProject` 的本工具与 Editor 复用**同一份映射**，不各自重拼——多一份重拼就多一处漏字段的机会。
- **「导出 ≠ 保存」**（真实的误导风险，工具描述里明文钉住）：不改变工程保存到哪个文件、不清除未保存改动、不进最近文件列表（那是用户主动"另存"的痕迹，agent 导一份副本不该污染）。回报里也复述一遍，免得 agent 跟用户说"已帮你保存"。
- **不能导出音频**（wav/mp3/flac/ogg）——这不是能力缺口，是**人在环决定**。音频渲染期间界面必须锁住，根因是**渲染要求数据全程不变**（不是 UI 偷懒），这与"agent 边导出边继续干活"根本矛盾（它下一步很可能就改工程）；且"要不要现在把机器占住几分钟"该由用户定，同[播放/试听不给 agent]的裁定，音频导出比发声更侵入。正解是 agent 备好参数、最后一下由用户按。

### 设置助手（`list_settings` / `set_setting`）

「想调某个设置 → 告诉我在哪调怎么调，或者直接帮我调」是同一个能力的两档，故两件工具共用一个数据源与一个闸门：

- **数据源 = `SettingsRegistry`（唯一）**：设置的键/标签/所在页/控件 config（范围·选项·默认）/重启标记/描述/agent 可否写全声明在那里，设置窗与这两件工具都从它派生，**不存在第二份表**。运行时选项（语言 / 系统字体 / 音频驱动·设备）也挂在声明上（`SettingItem.DynamicOptions`），设置窗与 agent 取到的候选一致。
- **`list_settings`**（只读）：逐条列键、英文标签 + 本地化标签、**所在页**（→ agent 能用用户的语言说"在设置 > 外观 里的「界面字体」"，这就是"告诉在哪调"的出口）、允许值、当前值、默认值、重启标记、是否 agent 可写、描述。选项过多（系统字体数百项）时截断并标注总数；`SettingItem<int>` 的下拉项（采样率/缓冲区）在注册表里存的是数字的**字符串**形，呈现给模型时还原成数字。
- **`set_setting(key, value)`**（写）：按键改一项 + `Settings.Save`。**校验一律按条目声明**（滑条范围 / 下拉成员 / 布尔 / 路径必须存在），不设第二套判据；模型把数字写成字符串（或反之）都能吃（按条目值类型归一化，下拉成员比对用无引号字面量）。值与当前相同 → 直接回报"已经是该值"、**不弹卡片**。改完若 `RestartRequired` 则提示要重启。
- **agent 不可写的项（`AgentWritable = false`）**：① **授权档位 `AgentAuthorization` 本身**——防自我提权，只能用户在 agent 面板头部改；② **活值由别处 UI 拥有、只单向落盘的项**（`AgentModelProvider` 由 agent 面板设置拥有、`AutoScrollTarget` 由视图菜单拥有）——agent 写文件既不即时生效又会被那处 UI 覆盖，改了只会误导。这些项的 `Description` 写明"归谁管"，好让 agent 转告用户去哪改。
- **边界**：这里只有**宿主应用设置**（`Settings.json` 那 20 项）。工程/轨/part 的属性走 `run_script`，插件参数走 `list_sound_sources` / `list_effects` + 脚本写口，扩展自己的设置走扩展设置系统（设置窗「扩展」页，agent 未通）。**快捷键另有专门一对工具**（见下）。

### 快捷键（`list_keybindings` / `set_keybinding`）

同一范式（一个数据源 `Keymap` + 一个闸门），并补上诉求 1 的最后一环——「帮我做个功能**并绑个快捷键**」现在能一路做完：`save_script` 存下的工具脚本由脚本目录监视器同步成命令 `script:<稳定 id>`（`ScriptToolMenu.SyncKeyCommands`），agent 随即可给它绑键。

- **`list_keybindings(query?)`**（只读）：逐条给 id、本地化命令名、**作用域**、生效手势（`ctrl+z` 存储令牌 + `Ctrl+Z` 用户字形，前者供模型再喂回来、后者供 agent 对用户复述）、是默认还是用户改过（并给出默认值）、**同域冲突**标注。顺序 = `Keymap.OrderOf`（首次注册序，与设置页一致）。`query` 过滤同设置页搜索框（匹配 id 或名）。头部固定说明**手势语法**与**作用域语义**。
- **`set_keybinding(id, gesture?/reset?/replaceConflict?)`**（写）：`gesture` 绑定（`""` 解绑、`reset:true` 恢复默认）。解析走 `KeyCodec.TryParseDeclaration`（额外收 `mod+`/`primary+` 别名 → 本平台主命令键，落盘仍是物理修饰）；无效手势/不可绑键回灌语法说明让模型自纠；与当前生效手势相同则"什么都没做"、不弹卡。落地用 `Keymap.Rebind`/`ResetToDefault`（**自带落盘 + `Changed` 广播**，菜单与设置页即时刷新，无需重启）。
- **冲突口径（与 `Keymap` 一致，不另立判据）**：**同作用域**同手势才是冲突（只有一个生效：注册序最小者胜、内建恒胜）；**跨作用域**同手势不是冲突（按焦点解析、内层遮蔽外层），但绑定后如实告知，免得用户以为某个"失灵"。同域冲突时 `set_keybinding` **默认拒绝**并点名占用者，要 `replaceConflict: true` 才夺键——**这才对应设置页录制时那句「已被 X 占用，是否改绑」的用户确认**，且夺键会解除原命令绑定，故授权卡片用 `SecondaryTarget` 额外点名它（知情同意）。
- **边界**：一命令至多一手势（v1 模型，见 `docs/keybinding-system.md`）；「录制式」输入是 UI 的事，agent 只走令牌串。

### 扩展路由 = 排障能力（`list_extension_routing` / `set_extension_routing` + 两处如实标注）

扩展身份 id **跨包可重名**，冲突包全部加载、由路由选出活实现（见 `ExtensionRouting`）。对 agent 来说这**首先不是配置项而是排障线索**：用户问"我装的某插件怎么不生效"，真相常常是它被另一个包顶替了。**关键在于：不标注就会主动误导**——`status=Loaded` 是真的（确实加载成功），"被路由掉"是另一根轴。故本切片一半是给既有工具补如实标注：

- **`EngineCatalog.AppendEngineList`**（voice/instrument/effect 三类引擎列表共用）：多包提供同一 type 时，从 `multiple: A, B` 改为 **`A (ACTIVE) — shadowed: B`**（活实现直接用 `ExtensionRouting.ResolveActivePackageId` 解析，不另立判据）。
- **`list_extensions`** 每条补一行：本包提供的争用身份是 `ACTIVE` 还是 `SHADOWED: "X" provides it instead, so THIS package's implementation is loaded but never used`。
- **`list_extension_routing`**（只读）：无冲突时**明说"没有任何身份被争用、没有东西被顶替"**并把排查引向别处（加载错误 / 能力枚举）；有冲突则逐行给 `kind:identity`、各候选包（含 packageId）、当前生效者、以及那是**用户选定**还是**默认规则**（内建优先、否则包 id 序最小）。
- **`set_extension_routing(kind, identity, packageId?)`**（写）：只接受**确实争用**的身份（非争用直接报错，免得写进无意义选择）；packageId 必须是该身份的候选之一；空 = 清除选择并如实告知会回落到谁；与当前选择相同则"什么都没做"。过闸门（`AgentWriteKind.RoutingChange`），**回报必须说"重启后生效"**（`SetSelected` 即时落盘，但解析发生在加载期），卡片文案也带这句。
- **系统提示钉住排障链**：`list_extensions`（状态/错误/是否被顶替）→ `list_extension_routing`（有争用才需要）→ `list_sound_sources`/`list_effects`（能力真的在不在），**明确要求不许停在第一步**。

### 能力位摘要（`list_extensions` 内联补齐）

作者只写 introduction 全文，**刻意没有 summary 字段**（作者不知道模型要什么，写出来多半是产品文案）。代价是模型每要"扫一眼这台机器上都有什么能力"，就得逐个把全文拉进上下文（每份上限 2 万字符）——装十几个插件时这一步比它真正要做的事还贵。故 `list_extensions` **返回前把缺的摘要补齐**，逐能力位带一行；要作者原文仍走 `get_extension_introduction`。对 agent 而言 summary 就是能力位自带的属性，它感知不到生成过程、也没有对应的工具。

- **短文档直接用作者原话，不调模型**：introduction 归一化（去 markdown 标题/列表标记、折叠空白）后 ≤600 字符时，它本身就是一句话说明——再转述一遍既费钱又只会更差（转述必然丢信息，还引入编造的可能）。这类条目标 `Verbatim`，呈现时说 **(author's own words)** 而非 "TuneLab's paraphrase"：**出处比转述强，不该一律标成转述**。实测这一条能消掉大多数请求，真正要调模型的只剩长文档。
- **内容寻址**：键 = introduction **文件内容**的哈希（前 16 字节 hex）。三个好处白得：① 插件更新换了文案 → 哈希变 → **自动失效**（一份过期摘要比没有摘要更糟，模型会照着它向用户断言）；② 语言变体天然分开（`localizations` 让不同语言指向不同文件）；③ 多后缀 format 条目共用一份说明 → 共用一条摘要，卸载重装也照样命中。代价是文件不可读，故另存 `Label` 字段纯供人排查、**永不参与查找**。
- **存 `Configs/ExtensionSummaries.json`**：与用户环境绑定 **且** 是派生缓存，两条都指向不进 Settings.json。懒加载，照 `ScriptInputMemory` 范式。写入时顺带清掉指向"当前已装扩展里不存在的 introduction"的条目；**一条 live 都没有时不清**——那通常意味着扩展还没加载完，照做会把整份缓存抹掉。
- **`SUMMARY:` 标记协议**（防"我来帮你总结："）：要求模型的回复**最后一行**恰为 `SUMMARY: <一句话>`，宿主只取最后一个标记之后的内容，**没有标记就整条丢弃**。只在 prompt 里写 `no preamble` 挡不住——`Sure, here is a one-line summary.` 这种客套话长度合规、也不以 `{` 开头，会一路混进缓存被后来的每个会话读到。取**最后一个**标记是因为模型偶尔会先复述一遍格式要求。
- **宁可没有也不要错的，且绝不截断**：超 600 字符（离谱输出的兜底，不参与塑形）、以 `{`/`[` 开头、或答 `NONE`（模型自判这份文档没什么可总结的）→ 丢弃不缓存。截出来的半句话，agent 之后每次读到都会困惑，而用户与开发者都不知情。
- **串行 + 60 秒预算，补不完如实回报**：一次可能要补十几条，并发很容易撞上 provider 的频率限制，一旦 429 整批白跑。超预算就停手，末尾附一句"N 条没做完，让用户稍后再问一次"，对应条目写 `(not summarized yet …)`——**不静默给半份**，模型得知道自己拿到的是不是全的。单份失败不拖累其余、也不做负缓存。
- **不合批**（曾考虑拼几份进一次请求以减少往返，否掉了）：① **串味**——同时喂多个同类插件（措辞高度相似），模型很容易把 A 的特性写进 B 的摘要，而这种错"看起来完全合理"，是最难被发现的一类；② 失败从丢一条变成**丢一批**（合批要结构化输出，稍有偏差整批解析失败）；③ 长输入本身降质，靠后的几份明显更笼统；④ 省的只是往返、不是 token，而限流已由串行解决。何况有了"短文档直采"之后，真需要调模型的恰恰是长文档——最不该合批的那种。
- **喂入口径**与 `get_extension_introduction` 一致（复用它的 `MaxIntroductionChars`）——绝不用比 agent 自己能看到的更少的信息去总结；两处各设一个数，早晚漂移成"摘要是从半份文档提炼的"而没人察觉。
- **两版被否掉的形态**：① 给 agent 一个 `set_extension_summary` 让它读完写回——依赖它记得调，且为产出一句话得先把全文拉进它的上下文，而这一步的全部目的恰恰是让它不必读全文；② 拆成独立的 `get_extension_summary` 工具——"扫一眼全部"要么退化成 N 次工具往返（每次都要重发完整上下文 + 全部工具声明，比内联贵得多），要么就是把 `list_extensions` 抄一遍。
- **不进 UI**：详情窗渲染的是作者写的全文。把自动生成的句子摆到用户面前，等于让宿主替作者背书一段他没写过的话。

### 扩展启停（`set_extension_enabled` + `list_extensions` 的结局注记）

"关掉但不卸载"——插件老报错、或加载慢又不常用时的正解。存取在 `ExtensionActivation`（独立 JSON `Configs/ExtensionActivation.json`，形状 `packageId → 被禁 entryKey 列表`，`"*"` = 整包），UI 全在扩展详情窗（header 的包级开关 + 各 tab 的条目级开关）；侧栏卡片**只展示状态**（「已禁用」+「需重启」徽标），刻意不放开关——卡片就那么点高，右栏已有版本徽标与卸载键，再塞开关不是挨着卸载"邀请误点"、就是把右栏挤成三层堆叠。**不进 Settings.json**（`ExtensionRouting.json` 同理）：Settings 只承「宿主固定的设置集合」——同一份发给任何用户都成立；而这两份的键与值都是"这台机器上装了哪些包"的函数，换台机器整份都无意义，属用户使用留下的痕迹，与 `ParameterPinning` / `RecentSoundSourceManager` / `ExtensionSettings.json` 同类。判据是**数据是否与用户环境绑定**，不是它有没有设置窗 UI（路由有一整页 UI，照样得搬出去）。

- **与路由是两根轴，别混**：路由回答"同一身份有多个实现时用谁"，启停回答"这份实现要不要参与加载"——后者对**没有任何竞争者的独苗**同样成立，而路由对独苗无话可说（它只列冲突行）。故 agent 排障时两件都要看：被顶替（routing）与被关掉（activation）的表象都是"装了却没用上"，处方却完全不同。
- **两级粒度**：省略 `capability` = 整包（**legacy 包与 manifest 坏包只有这一档**，它们没有条目可禁；且整包关掉时连程序集都不加载，是真能省启动时间的那一档）；给 `capability`（`kind:identity`）= 只关包里那一个能力，其余照常工作。多后缀 format 条目的几个身份共用同一份实现，**一起开关**（写入时逐身份各存一条，判定时任一命中即算禁用——作者日后增删后缀不会让用户的禁用悄悄失效）。
- **`list_extensions` 的两处如实标注**（缺了就会主动误导，同 routing 的道理）：整包被关时包级 `status=Disabled` 外另起一行说明"装了但被关，本次运行它的能力一个都不存在"；逐能力位行补 `[DISABLED by the user]` / `[FAILED to load: …]` / `[SKIPPED: …]`——**包级状态是汇总，答不了"这个包里到底哪一个能力没起来"**，而那正是用户要问的粒度。这也是 `ExtensionEntryInfo.Status` 这个条目级状态存在的理由。
- **`set_extension_enabled(packageId, enabled, capability?)`**（写）：过闸门（`AgentWriteKind.ExtensionActivationChange`，卡片按"整包/单能力 × 开/关"四句分别措辞）。整包已关时，单个能力**既不能单独开**（如实要求先开整包）**也无需再关**（已经是关的）——不写一个看不出效果的选择。与当前状态相同则"什么都没做"、不弹卡。回报必须说**重启后生效**，且关的那次要补一句"在那之前它这一轮仍然可用"。
- **禁用发生在注册之前**：被禁的条目根本不进各 manager，于是 routing 矩阵、扩展设置页、`list_sound_sources`/`list_effects` 自然都看不到它——**无需在每个下游各写一遍过滤**。副作用之一：被禁条目在详情窗里没有设置齿轮（它没有实例可配置），重新启用并重启后自会回来。
- **工具描述里钉住后果**：关掉 = 下次启动它彻底不存在，工程里引用它的部分（用那个音源的 part、那个格式的文件）会解析不到——`set_extension_enabled` 的描述要求 agent 先把这句告诉用户再动手。
- **键会自己清理**（`ExtensionActivation.PruneUnknown`，加载全部包之后按"已装全集"跑一次）：卸载不会回来收拾自己的禁用键，留着就会埋雷——日后重装同一个包会**静默地装完就是关的**。语义 = 包没了，对它的选择也就没了；再装回来算新装、默认启用。

### 扩展自己的设置（`list_extension_settings` / `set_extension_setting`）

设置窗「扩展」页那些——由插件经 `IExtensionSettings` 声明、存 `ExtensionSettings.json`（两级分桶 `root[packageId][kind:extensionId]`）。与宿主应用设置那对工具同范式，差别在**判据只能来自插件的声明**（扩展字段没有静态类型，`GetSettingsConfig` 就是唯一真源，且它是**当前值的函数**、字段可动态显隐）：

- **`list_extension_settings(extension?, packageId?)`**（只读）：不给 `extension` → 列哪些扩展声明了设置（`kind:extensionId`、显示名、来源包、字段数）；给了 → 逐字段列键(+标签)/类型·范围·选项/当前值/默认值，未设过的如实标 `(unset, so the default applies)`。同一 id 跨包并存时**不猜**，要求传 `packageId` 消歧。
- **`set_extension_setting(extension, key, value, packageId?)`**（写）：按字段 config 校验（`ConfigText` 出措辞，`JsonScalar` 统一数字/字符串宽容口径），过闸门（`AgentWriteKind.ExtensionSettingChange`），落地路径**与设置窗关页时逐字一致**：读全量已存值 → 改一格 → **按改后值重算的 schema** 取密钥集（动态面板下密钥字段可能显隐，避免漏标/误标）→ `ExtensionSettingsStore.Save` → `ExtensionSettingsManager.ApplyOne` 立即回喂。回报提醒"引擎可能只在下次启动时读取"。
- **密钥政策（用户 2026-07-26 定）：只读不回灌 + 禁写**。声明为 `TextBoxConfig.IsPassword` 的字段（API key / 许可证，走 DPAPI/钥匙串）：`list` 只报 `currently SET` / `NOT set`、**绝不把明文放进模型上下文**；`set` 一律拒绝并引导用户自己去设置窗填。理由=把用户密钥经模型上下文送去第三方服务，风险与收益完全不成比例。注意**保存时密钥仍被完整保留**（Load 解密 → Save 重新加密，与设置窗同一往返），拒写只拦 agent 这一路。
- **边界**：`agent-model` 的 provider 设置也存在同一文件里，但它的 UI 在 agent 侧栏、不进设置窗扩展页，故 `ExtensionSettingsManager.GetEntries()` 不含它 → 这对工具也看不到它（刻意：agent 不配置自己的模型连接，见 `AgentModelProvider` 的 `AgentWritable=false`）。
- **format 也在列**（桶键 `<kind>:<两方向后缀并集按声明序用 `|` 拼接>`，如双向 `format:mid|midi`、只导出 `format-export:midi`；kind 由作者填了哪几个后缀字段**推出**，manifest 里的 `type` 恒为 `format`）：它与三种引擎的结构差异是注册的是**工厂**而非长驻实例，故 `GetEntries()` 拿到的是 `FormatsManager` 的**探测实例**（只用来问 schema / 存取值），真正干活的实例在导入导出时现 new、由 `FormatsManager` 就地回喂。对 agent 完全无感：`set` 后照旧立即生效（下次导入/导出的实例就会拿到），回报里那句"可能要重启"对 format 反而不适用但无害。**身份是条目不是后缀**——多后缀格式只出现一行，`extension` 传拼接串或显示名均可；同一后缀的导入与导出若由两个类实现（那是两个条目），则是**两行、两个桶**，得分别设。
- **顺手修的宿主漏洞**：`GetEntries()` 此前只收 effect + voice，**漏了 instrument**（其管理器同样有不触发 Init 的 `GetExtensionSettings`）——声明了设置的 instrument 插件既不在设置窗渲染、也拿不到 `ApplyPersisted` 回喂。已补一行 `Collect(…, "instrument", …)`，故本对工具与设置窗扩展页同时受益（新增 `instrument:` 桶键，此前从未写过任何值，无迁移问题）。

## 探测沙箱（`run_in_sandbox`）

静态读 schema 有天花板：空 context 只能拿默认分支，**条件化 schema**（参数 X 仅当 Y=某值才现）与**数据驱动 schema**（phoneme slot 来自真实音素）静态永远够不着。解法 = 给一段脚本一个**可丢弃的无头 `IProject`**，用同一 `tl` 面随便改，再**真触发合成、读回显**拿到只有合成后才存在的真相。与 A1–A4 静态读**互补非替代**（静态读=便宜前几级秒回；沙箱=够不着时的重武器）。

- **入口**：`run_in_sandbox(code)`。工程是全新、隔离、跑完即弃的——与用户工程无关，故写入**不碰用户数据、不过授权闸门**，可放开试。
- **实现**（`TuneLab/Scripting/SandboxHost.cs`）：整个生命周期跑在一条**专用后台线程**上，装一个**可泵的 `SynchronizationContext`**（`PumpableSynchronizationContext`：`Post`/`DrainAll`/`WaitForWork`）。合成是异步的（`VoiceSynthesisPipeline` 的 session 事件与 `Dispatch` 的 await 续体都经建管线时的 `SynchronizationContext.Current` marshal 回来）——编辑器那条数据线程=UI 线程（Dispatcher 自动泵），无头沙箱没有窗口故自带上下文**手动泵**，仿 `Editor.SynthesisNext` 的 peek/dispatch 驱动循环但零 UI 依赖。不能在 UI 线程上跑（同步阻塞的 `synthesize()` 会与其自身的 marshal 续体自死锁），故专用线程。
- **脚本面**：注入正常 `tl`（造场景：`addTrack`/`addPart`/`setSoundSource`/`addNote`…）+ 一个 `sandbox` 全局补「合成相关」的那半（**只沙箱有意义**，故不进正常 `tl` 句柄面）：
  - `sandbox.voices()` → `[{type,id,name}]`（如实镜像 `VoicesManager`，含内建空引擎；会惰性 Init 引擎、跑插件代码）。
  - `sandbox.synthesize(part, {timeoutMs?, maxDispatches?})` → 触发离线合成并**同步等待**（手动泵驱动循环），返回 `{done, dispatches, ms, timedOut}`。自带超时 + 派发次数预算。
  - `sandbox.syllable(note)` → 合成回显音素 `{leading:[{symbol,duration,stretchWeight}], body:[...], bodyOffset, symbols:[...]}` 或 null。读的是 `note.SynthesizedSyllable`（与钢琴窗音素显示同源）。
- **收口**：`ScriptContext.FlushForSynthesis()` 关 merge 括号让管线看到刚加的音符并 prep（否则 `IsSynthesisBatching` 抑制、通知未扇出）。跑完拆场景（换空工程触发旧工程 Detach+Dispose）。
- **成本护栏**：合成很重（引擎加载模型耗时数秒）→ 工具描述强制**一段脚本一次探完**（迭代/中间数据留脚本内、不进上下文）；`synthesize` 超时 + `maxDispatches` 预算；结果精炼（symbols 而非原始 dump）。
- **挂音源用正常 tl 写** `part.setSoundSource(...)`（真实编辑器/沙箱通用、含存在校验），沙箱不另开专用 setter。

## 问用户（`ask_user_question`）

让 agent 在**本轮之内**问一句并等到答案再继续——不必把任务切成两轮、也不丢掉已有进展（此前它只能"结束这轮 + 在正文里问"，用户回答后模型才继续，中途的工具往返成果得靠上下文重述）。

- **为什么是工具、不是 `tl` 原语**：等卡片必须 async，而脚本经 Jint **同步**跑在 UI 线程，中途阻塞等卡片会自死锁（同 `export_project` 那条理由）。它也不改工程状态、纯为 agent 自身决策服务，按分面原则归工具面。
- **不过授权闸门**：它不是写操作。闸门管的是"要不要允许改动"，这里是"请你告诉我"。
- **参数**：`question`（必填）、`options?`（预设答案，省略即纯开放提问）、`multiple?`（默认 false=至多选一）。宿主对 `options` 去空、去重（空行会渲成点不动的空按钮；重复项让用户无从分辨选了哪个）。
- **卡片形态**：问题 + 选项（**单选 `RadioButton` / 多选 `CheckBox`**，让"能选几个"一眼可辨）+ 自由文本框 + [提交回答]。**整行可点**（16px 的框在窄侧栏里靶子太小）。
- **卡片必须排队渲染**（`Dispatcher.UIThread.Post`，不走 `CheckAccess` 直接建）：工具块由 `AgentToolStarted` 经 `Progress<AgentEvent>` **异步 Post**，而回调本身在 UI 线程**同步**执行——直接建会抢在工具块之前落地，卡片就跑到自己那次调用的**上方**。授权卡片原先也犯这个错，已一并改。
- **选项与文本各自独立**：可以只选、只写、或两者都有；**至少有一样**才能提交——空答没有信息量，等于让 agent 白等，故 [提交回答] 在空答时**置灰**（不是"能点但没反应"，那让人以为界面卡了）。回报因此分开陈述，模型能区分三种情形：

  | 用户操作 | 回报 |
  |---|---|
  | 选了一项 | `Selected:` ＋ 次行 `- 保留原音源` |
  | 多选两项 | `Selected:` ＋ 两行 `- 轨道1` / `- 轨道3` |
  | 选项 + 补充 | 上述 ＋ 末行 `Additional input: …` |
  | 只写文本 | `No option was selected.` ＋ `Input: …` |
  | **多选但一个都不选** | `Selected: none — the user deliberately chose none of the options.` |

  **选中项逐行列出、不用逗号拼接**：选项文本自身可能含逗号（"轨道1, 副歌"），拼成一行后**模型与宿主一样**无从判断那是几项——而模型不会报错、只会静默误解，比解析失败更糟。文本字段固定放最后一段，因为它可能自带换行（"该标记之后全是文本"这条规则才立得住）。
  多选的**空集是一个答案**（"这几条都要吗" → 一条都不要），故措辞与单选的"没回答"明确分开，且多选时 [提交回答] **恒可用**。

- **互斥按仓库范式做**：点击只改卡片自己持有的"当前选中集合"，再统一刷新全部按钮显示——控件不知道同伴是谁（同 `FunctionBar` 的钢琴工具、`ParameterTabBar` 的参数 tab）。单选**允许点掉**（不接 `AllowSwitch`），因为要支持"一个都不选、只写文本"。
- **不设超时**：卡片一直挂着。**用户点停** → `tcs.TrySetCanceled()` → 这次调用成为悬空调用、被如实记作"结果未知"（同其它工具，见 `AgentRunner.CloseDanglingToolCalls`）。刻意不返回"空答案"——那会让模型以为用户答了个空，而事实是这次调用没有结果。
- **回答后 → 只读问答块，重载重建同一个块**：提交那一刻卡片内容整块换成只读留痕（问题 + 全部选项、选中的打勾且白字、未选的压暗 + 补充文本），重开会话时按记录重建**调的是同一个渲染函数**，故两边一字不差。换掉整块而非"逐个禁用控件"——旧控件离开视觉树就再也收不到事件，不必担心漏封一处又能改（那会让卡片显示的选中态与已发出的答案不符 = 虚假留痕）。
  - **重载 = 工具块 + 问答块**（与实时呈现对齐）：工具块照常重放（保留参数/结果原文，排查时有用），其后**再补**一个只读问答块负责把内容读得懂。`BuildReplayedTurn` 预扫一遍备好 `id→结果`（遍历到 assistant 的 tool_call 时结果还没扫到），遇 `ask_user_question` 即在工具块之后追加问答块。
  - **外壳共用 `QuestionCardShell`**：实时卡片与重载重建同一层圆角/底色/边框——分开写迟早漂移（重载那次就漏了整层框）。
  - **没有配对结果**（问了但那轮被打断）→ 块内标「未回答」，与"多选明确一个都不选"区分开。点停时实时也立刻变成这个样子（当时勾了什么并没提交、不是答案，显示出来会误导）。
  - 反解靠"逐行 `- 选项`"与"文本固定在最后一段"这两条格式约定，因此无歧义、**零新增持久化**。
- **用途约束写在工具描述里**（不是防滥用，是让它用对）：只在真有歧义、且猜错代价大时问；能自己查证的（读工程 / `list_settings` / 各 list 工具）不许问；尽量给具体选项（点一下比打字省事）；一次只问一个问题。

## 工具输出上限（中央兜底 + 分层）

防"某工具单次输出淹没上下文"，分两层：

- **中央硬上限（兜底，覆盖全部现有+未来工具）**：`AgentRunner` 在工具结果进上下文的**唯一入口**（`ClampToolResult`，紧接 `ExecuteAsync` 之后）统一截断——超 `Settings.AgentMaxToolResultChars`（**宽默认 40000 字符 ≈ 1 万 token；可在设置窗「常规」页调**；`<=0`=不限）即保留头部 + 明确标记 + 收窄指引。**在此一处 clamp，故展示(progress)与回灌(mMessages/trajectory)一致**。设宽默认的用意：普通机器十几个音源/结果远小于此、**体验不受影响**，只拦成百上千的畸形案例。
- **各工具自带的更贴心上限（在中央之下，作友好提示）**：`get_extension_introduction` 20000 字符截断；`list_sound_sources` 音源列表 300 条 + "refine" 提示；`run_script`/`run_saved_script` 脚本 `print/log` 输出 16KB（`ScriptRunner.MaxOutput`）。
- **设计取舍（业界通用套路的选型）**：可收窄的 list（有 `kind/engine/source` 参数）→ 收窄提示；原子读（一个脚本/一篇介绍）→ 截断（无可收窄，未来可补 offset/limit 分页）；脚本返回值 → 让脚本**在脚本内**先蒸馏（CodeAct：迭代与压缩都不进上下文）。纯"拒绝+让模型收窄"不作默认——原子读无从收窄、且开头够用时截断更省往返。

## 维护

- **新增/改脚本 API（≈ 给 agent 加能力）**：在对应句柄类或根（`ScriptApp`/`ScriptProject`/`ScriptHandles`）加 public 成员——标量字段用可读写属性（getter 实时读；setter 内 `ctx.EnsureBracket(midi)` + 改 + `ctx.Bump()`），查询/动作用方法（返回句柄经 `ctx.WrapXxx` 缓存保身份）。增删挂父、不自行 `Commit`、绝对 tick。新成员 PascalCase（脚本里写 camelCase）。收口服务（`EnsureBracket`/`Bump`/`WrapXxx`/`Project`）是 `ScriptContext` 的 internal 成员，只暴露 public 给脚本。
- **脚本模块三层**：`ScriptContext`（收口内核，脚本不可见、全 internal）/ `ScriptRoot.cs`（`ScriptApp`=注入的 `tl`、`ScriptProject`=`tl.currentProject()`）/ `ScriptHandles.cs`（句柄 + 只读快照）。`ScriptTools.cs` = 工具脚本枚举/预校验，`TuneLab/UI/.../ScriptToolMenu.cs` = 菜单注入。
- **新增一个 agent 工具**：写 `IAgentTool`（`Name`/`Description`/`ParametersJsonSchema` + `ExecuteAsync`，用 `ToolJson` 解析参数、catch 转错误文本），在 `AgentSideBarContentProvider.SetProject` 注册。但优先反思：能不能用 `run_script` 表达？能就别加工具。
- **文档权威源 = `Resources/ScriptDoc/en-US.md`**：先改它，再同步 `zh-CN.md`、`ScriptApiReference.cs`（喂 LLM 英文精简）、本文件、`ScriptSideBarContentProvider.FallbackDoc`。
