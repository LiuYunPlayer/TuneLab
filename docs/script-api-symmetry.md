# 脚本 API 对称性审计

脚本面（`tl` 对象式 API）与数据层 C# 面的逐成员对照。**目标形状：尽量原子、尽量与 C# 侧一一对应**，
把组合的自由留给使用者（用户 / agent），而不是由宿主预先假想一组参数替他们决定能表达什么。

本文是**对称性契约**：新增脚本 API 前先在这里找到它对应的 C# 成员；找不到对应就要说明为什么它该存在。

> **状态：本文列出的整改已全部落地。** 实现分布在
> `TuneLab/Scripting/ScriptInfo.cs`（新增，info ⇄ JS 双向映射层）、`ScriptHandles.cs`、`ScriptRoot.cs`、
> `ScriptArgs.cs`、`ScriptContext.cs`，外加数据层补一个对称入口 `IProject.CreateTrack(TrackInfo)`。
> 落地时对本文原稿有三处**修正**，各自记在对应小节：
> ① 导出配置**不做脚本面**（那 10 个属性不是 `IDataProperty`，写它们不入撤销栈，开成句柄字段会破坏
> 「整段脚本 = 一个可撤销单位」与 preview 的原子回退）；
> ② part 几何取**纯三元组** `{pos, startOffset, endOffset}`（原稿"调用方无感"的说法对 part 不成立）；
> ③ 「info 入口校验」一节的**前提不成立，未实施**（有序集合本就按键插入，无序 info 不会破坏不变式）。

## 判据（五类）

| 类 | 定义 | 处置 |
|---|---|---|
| **对称** | 与某个 C# 成员一一对应（含"吃完整 info"的创建入口） | 保留 |
| **A 假想参数袋** | 对应 C# 的 `CreateX(info)`，但只覆盖 info 的一小部分字段 | 升级成吃完整 info；旧写法是新写法的子集，调用方无感 |
| **B 真机制封装** | 对应真实 C# 机制，只是形状不同（回调 → 参数袋 / 多步 → 一步） | 保留，但**纯化**（不与无关字段耦合） |
| **纯糖** | 无 C# 对应，且脚本自己就能表达 | 删除——糖只增加冻结面与手册负担 |
| **缺失** | C# 侧有可写成员，脚本侧完全没有出口 | 补上 |

**为什么 B 类必须保留**：移动（维序摘除-重插）与区间曲线操作背后有复杂的数据处理与命令记录逻辑，
不能让脚本自己拼——拼错会破坏集合有序性或漏记命令。封装的边界就是"内部不变式不能交给外部维护"。

## 缺失（最需要补的一类）

### 导出配置（10 个属性）—— **进脚本面，但作为「不入撤销栈的设置项」**

> 这一节经过两次反转，结论与中间过程都记在这里，免得后人重走。
> 1. 起初判为"不做脚本面"（理由：写它不入撤销栈，会破掉脚本的原子回退承诺）。
> 2. 随后按 SDK 侧的理由（"这属性几乎只有本应用会用到，对别家工程格式是无效信息"）把它从 SDK 的
>    `TrackInfo` 内部化到宿主的 `NativeTrackInfo` 子类——**那是另一个问题**，与脚本面无关。
> 3. 最终反转：**"跑一段脚本把导出各项设成我的预设"是用户会要的可复用命令**（还会想绑快捷键），
>    按 `docs/agent-tools.md` 开头的归属判据（"用户会要的能力 → 走 `tl` 脚本动作面"）它就该在脚本面。
>    第 1 步的错在于把"不可撤销"当成了否决理由，而真正的解法是给非撤销写补一个回退保险。

**落地形状**：
- 工程级 8 项 = `project.exportPath` / `exportFileName` / `exportFormat` / `exportSampleRate` /
  `exportBitDepth` / `exportBitrate` / `masterExportEnabled` / `masterExportChannels`（与 `IProject` 一一对应）；
  逐轨 2 项 = `track.exportEnabled` / `track.exportChannels`。
- **不入撤销栈**：与在导出侧栏里改它们一致（改完 `Ctrl+Z` 不退回）。整个容器一致地不可撤销，不违反
  「撤销对称原则」（deriver 的派生记录同为刻意非撤销）。
- **但脚本的原子性照旧成立**：`ScriptContext` 对这批非撤销写做**写前留底 + 回退还原**
  （`CaptureExportConfig` / `CaptureTrackExport` → `RestoreNonUndoableSettings`），故脚本**出错**或跑
  **preview** 时它们如实还原；只有成功提交才坐实。
- **不进 `track.getInfo()`**：设置项不属于"轨的内容"，故复制一条轨不带导出开关（要跟随就显式赋值）。
- **格式 id 只有一张表**：`AudioExportFormatExtensions.AllIds` / `TryParseId`（严格，脚本用）/ `ParseId`
  （宽容，读文件与 UI 下拉用）。原先这张表在导出侧栏里是私有副本，已改为共用。

### SDK 侧：`TrackInfo` 的两个导出字段已内部化（与上面是两个问题）

`IProject` 的 8 个（`ExportPath` / `ExportFileName` / `ExportFormat` / `ExportSampleRate` / `ExportBitDepth` /
`ExportBitrate` / `MasterExportEnabled` / `MasterExportChannels`）与 `ITrack` 的 2 个（`ExportEnabled` /
`ExportChannels`）在数据层是**普通 C# 属性、不是 `IDataProperty`**（`Project.cs:20-27`、`Track.cs:26-27`），
写它们**不入撤销栈**；工程级那 8 个甚至不在 `ProjectInfo` 里，走宿主内部的 `ExportConfigInfo` 单独持久化，
导出侧栏也是直接赋值、不 `Commit`。它们是刻意的第三类状态：**工程内的设置项，非可撤销的工程数据**。

`3bf6d68`（"ProjectInfo 剥离 editor/export"）把工程级那 8 项内部化了、`PublicAPI.Shipped.txt` 减 26 行，
**但没碰 `TrackInfo`**（`git show 3bf6d68 --stat -- .../TrackInfo.cs` 为空），于是逐轨那 2 项留在了 SDK 公共面。
按那次重构自己写下的原则（"通用 `IImport/ExportFormat` 保持 musical-only"）这是遗漏：**它们对别家工程格式
是无效信息，却让每个通用格式插件都能读写**。连 compat 层都自相矛盾——`FormatConverter` 的注释声明
"交换格式不携带 app 私有元数据、如实丢弃"，同一文件却仍在映射这两项。

**已按 deriver 的 `NativeAudioPartInfo` 同一手法修正**：新增宿主内部 `NativeTrackInfo : TrackInfo` 承载这两项
（依 `docs/sdk-api-evolution.md` 的判据：数据要穿过宿主不拥有元素类型的公共集合 → 子类；`ProjectInfo.Tracks`
正是 `List<TrackInfo>`）。`Track.GetInfo()` 恒产出子类、`SetInfo` 用 `as` 取回（拿到基类则落默认值）；native
`.tlp`/`.tlpx` 序列化器 downcast 读写，**磁盘键位不变**（仍写在各 track 对象内），故老工程照旧能开；通用格式
插件只见基类。compat 层的映射一并删掉、注释扩到覆盖逐轨。删 `PublicAPI.Shipped.txt` 那 4 行是 **breaking，
已获维护者批准**；`ExtensionManager.SdkVersion` 不动——V1 尚未随 2.0.0 发布，那道闸门保护的是已发布插件。

**注意这与"进不进脚本面"是两个问题**：SDK 不暴露的理由是"对别家工程格式无效"，脚本面的问题是"这条轨被
复制出来后它的导出设置能不能改"。后者的结论见上一节（进，作为不入撤销栈的设置项）。

### `IProject`
- **`InsertTrack(index, track)`**：脚本只能追加到末尾（`addTrack`），**无法指定位置插入轨**。
  → 已补 `project.addTrack(info, index?)` / `insertTrack(track, index?)`。为此在数据层补了对称入口
  **`IProject.CreateTrack(TrackInfo)`**（原先只有 `AddTrack(info)` = 建 + 追加的合体，没有"建游离实体"那一半；
  `ITrack.CreatePart` / `IMidiPart.CreateNote` 都有，唯 `IProject` 缺）。
- **删除速度 / 拍号标记**：脚本有 `setTempo` / `setTimeSignature`（改或加），但没有对应的删除入口。
  → 已补 `project.removeTempo(atTick)` / `removeTimeSignature(atBar)`。该处无标记则**报错**（不静默 no-op，
  避免假成功）；首个标记 = 基准速度/拍号、不可删，与时间轴右键菜单同一条规矩（`TimelineViewOperation.cs`
  的 `TempoIndex != 0` 守卫）。

### `ITrack`
- **`AsRefer`**（`IDataProperty<bool>`）→ 已补 `track.asRefer`
- **`Color`**（`IDataProperty<string>`）→ 已补 `track.color`

### 向上的父引用（原稿漏了，落地时补）

`IPart.Track`（**可写**）/ `INote.Part` / `Vibrato.Part` / `IEffect.Part` 都是 C# 成员，脚本侧**零出口**——
整张对象图只能向下走。这不只是对称性洁癖：`tl.selectedParts()` / `tl.currentPart()` 拿到的是 part，
**没有轨句柄就既不能在旁边加 part、也不能调 `insertPart` 迁移**，是个真死路。
→ 已补只读的 `part.track()` / `note.part()` / `vibrato.part()` / `effect.part()`。

只读而非可写：`Part.Track` 直接赋值会让对象声称属于新轨、却仍留在旧轨的链表里（两边都坏）。真正的换父
是「摘出 + 插入」，那条路已由 `removePart`/`insertPart` 提供。

**刻意不补** `phoneme.note()`：`IPhoneme` 在 C# 侧就没有父指针（列表成员即其唯一归属），补它等于凭空发明
一个数据层没有的关系 —— 正是本文判为"纯糖"的形态。

### `IPart`
- **`StartOffset`** —— `IPart` 注释明写「三个原始字段各对应一个原子操作：移动改 `Pos`、拖左边缘改 `StartOffset`、
  拖右边缘改 `EndOffset`」。脚本只暴露派生的 `startPos`/`endPos` 两个可写量，**拖左边缘（前向裁剪）整个缺失**：
  audio part 的左边缘裁剪、midi part 的内容前向裁剪都无法表达。
- 且**方向反了**：C# 侧 `StartPos`/`EndPos`/`Dur` 是**只读派生**（`Part.cs:27-29`），脚本侧却做成可写。

→ 已按**纯三元组**落地：可写 `pos` / `startOffset` / `endOffset`，只读派生 `startPos` / `endPos` / `dur`。
三者都参与 `PartList` 的排序键（`IsInOrder` 比 `StartPos()` 再比 `EndPos()`），故都经 `MovePart` 维序。

> **修正原稿**："A 类整改"一节说"`track.addPart({startPos, endPos})` 今天怎么写明天还怎么写"——**对 part 不成立**，
> 因为 `PartInfo` 里根本没有 `startPos`/`endPos` 这两个字段。要么按 C# 一一对应（三元组），要么额外收两个派生
> 便利字段（= 同一件事两条路，正是本文判为"糖"要删的形态）。取前者：`{pos, endOffset}`，
> 老写法 `{startPos, endPos}` 失效（release/2.0.0 未发布、不做兼容）。

### 跨父迁移（**移动**，非复制）
`removePart` 返回 void 且把句柄标成失效，故脚本**无法把一个 part 移到另一条轨**——只能"删掉 + 重建"，
于是又是一次静默丢保真（音源 / 曲线 / effect / 音素全丢）。而 `ITrack` 注释明写「跨轨迁移属换父，
仍走显式 `RemovePart`/`InsertPart`」。解法见下"游离实体"一节。

→ 已落地，但**只有 part 真能换父**：`IPart.Track` 是 `{ get; set; }`，而 `INote.Part` / `Vibrato.Part` /
`IEffect.Part` 都是构造期绑定的只读属性（`Note.cs:13,49`、`Vibrato.cs:20,39`、`Effect.cs:13,40`）——数据层
本就不支持 note / 颤音 / effect 换父，脚本面照实反映：它们的 `insertX` 只接受"插回原父"，跨父给出明确错误并
指路 `另一个part.addX(x.getInfo())`。音素则另一种情况：`IPhoneme` 没有父指针（列表成员即其唯一归属，
无"游离"可言），故跨 note 搬运同样走 info 路。**原稿把这三类与 part 并列为"同理缺失"，是把数据层不存在的
能力也算进了缺口。**

### `IMidiPart`
- **`Gain`**（part 级增益，`MidiPartInfo.Gain`）→ 已补 `part.gain`
- **`PiecewiseAutomations`**（分段自动化）→ 已补一族四个方法
  （`piecewiseAutomationIds` / `samplePiecewiseAutomation` / `setPiecewiseAutomationLine` / `clearPiecewiseAutomation`），
  与 pitch 那一族逐一同形（pitch 本就是分段轨里的一条专属常驻通道）。`IEffect` 侧也有同一对
  `PiecewiseAutomations` / `AddPiecewiseAutomation`，故 effect 句柄上平行补齐——只补 part 侧会造成新的不对称。
  **连带修的一处不对称**：`automationIds()` 原先返回 `AutomationConfigs` 的**全部**键（含分段轨），而
  `sampleAutomation`/`setAutomation` 只吃连续轨 → 取到的 id 用起来报错。现按 `IsPiecewise` 分成两张表，
  两族各自自洽。

### `Vibrato`
- **`AffectedEffectAutomations`**（颤音影响到哪些 effect 自动化轨；`EffectInfo.Id` 就是为这张表的横向引用而存在）
  —— 连平行的 `AffectedAutomations`（音源级轨）也一样零出口。
  → 已补：两张表的只读快照 `affectedAutomations()` / `affectedEffectAutomations()`，写侧
  `setAmplitude(id, amp, effect?)` / `removeAmplitude(id, effect?)`，对齐 C# 的
  `SetAmplitude(AutomationKey, …)` / `RemoveAssociation(AutomationKey)`——那个可选的 `effect` 参数就是
  `AutomationKey` 的路由维度（省略 = `AutomationKey.Voice(id)`，给了 = `AutomationKey.Effect(index, id)`）。

## A 类：假想参数袋 → 参数升级为完整 info（**方法名不变**）

| 脚本原状 | C# 侧 | 原参数袋覆盖度 | 现在 |
|---|---|---|---|
| `project.addTrack(name?)` | `AddTrack(TrackInfo)` | 只有 `Name` | `addTrack(info?, index?)` |
| `track.addPart({startPos, endPos, name?})` | `CreatePart(PartInfo)` + `InsertPart` | 3 个字段 | `addPart(info)`（含 audio 型：`{type:"audio", path}`） |
| `part.addNote({pos, dur, pitch, lyric?})` | `CreateNote(NoteInfo)` + `InsertNote` | 4/7（缺 `Pronunciation`/`Properties`/`Phonemes`） | `addNote(info)` |
| `part.addVibrato({pos, dur, frequency?, …})` | `CreateVibrato(VibratoInfo)` + `InsertVibrato` | 缺两张影响表 | `addVibrato(info)` |
| `part.addEffect(type)` | `CreateEffect(EffectInfo)` + `InsertEffect(index, e)` | 只有 `Type`；**且丢了 index 定位** | `addEffect(info, index?)` |
| `note.addPhoneme({symbol, duration?, …})` | `Phoneme.Create(PhonemeInfo)` + `List.Add` | 缺 `Properties` | `addLeadingPhoneme(info)` / `addBodyPhoneme(info)` |

**读出侧同时补齐**：每个句柄加 `getInfo()`，产出与 `addX` 收的**同一形状**的普通 JS 对象（`JsObject`/`JsArray`，
脚本能 `for-of`、下标、`JSON.stringify`）。没有读出侧就谈不上"info 路能替代 duplicate"。

**schema 是脚本面自己的契约**（camelCase + 绝对 tick），由 `ScriptInfo.cs` 显式桥接到 SDK 的 `*Info`：
SDK DTO 是 `PublicAPI.Shipped.txt` 守着的冻结 ABI、演进节奏与脚本面相反，且用锚点三元组 + PascalCase，
直接暴露等于让脚本面变成 ABI 的一部分。位置换算也在这一层收口：part 内成员（note / 颤音 / 曲线点）的 tick
在 info 里是**绝对**的，读入减 `basePos`、写出加回，与句柄面同一条坐标铁律（`note.pos` 在句柄上读到的
和在 info 里看到的是同一个数）。

C# 侧一律是**三段式**：`Info`（纯数据，改它不进撤销栈）→ `CreateX(info)`（建游离实体）→
`InsertX(entity)`（入树，**这一步进回退栈**）。自由度落在"改 info"那一段，且那一段对纯数据任意修改、零心智负担。

**方法名保持 `addX`。** 但要注意 C# 侧 `Add` / `Insert` 的现状**不是一条命名规律，而是 API 面不完整**：

```csharp
void AddTrack(TrackInfo info);              // 吃 info，无位置（新建默认加末尾）
void InsertTrack(int index, ITrack track);  // 吃实体 + 位置（调序 / 删除后撤销时已有实体）
```

真正正交的是**两个维度**：参数类型（info / 实体）× 是否带位置。四个组合都合法，现在只实现了两角，
因为「新建默认加末尾」和「调序时已有实体」这两个需求恰好各占一角。完整的 API 面里
`Add` 一样可以带 index（例如"在某条轨下方新建一条"），只是眼下没有这个产品需求。
**所以不要从现状反推命名规律**——脚本侧的命名只需按语义选：吃 info 叫 `add`，吃实体叫 `insert`。

于是整改只有两点，**调用方方法名不变**：

1. 参数从假想袋换成**完整 info**。info 字段都有默认值，能填的字段从几个扩展到全部。
   （唯一的例外是 part 几何：`PartInfo` 没有 `startPos`/`endPos` 字段，故那两个键的老写法失效——见上文修正。）
2. **补上位置参数**：`addTrack(info, index?)`、`addEffect(info, index?)`（后者对应 C# 已有的
   `InsertEffect(index, …)`，现在被脚本硬编码成"追加末尾"、白丢了定位能力）。
   `addPart` 则**不需要** index：`PartList` 是按起点自排的有序表，位置由 `pos` 决定。

**另**：`addPhoneme` 的 `leading?`（默认 `false` = 进 body）把**两个不同的容器**糅在一个方法里——C# 侧
`LeadingPhonemes` / `BodyPhonemes` 是两个独立列表。按「增删挂父」+ 原子原则拆成
`note.addLeadingPhoneme(info)` / `note.addBodyPhoneme(info)`，那个假想的默认值也随之消失。→ 已落地。
（与 `INote` 的注释同一条纪律：「写入方显式选列表（编译期强制 revisit）」——脚本面对应的就是"必须明说往哪个容器加"。）

## 游离实体：可以存在，只是不可写

**「游离期不可写」≠「禁止游离对象存在」。** 前者是数据层纪律（未 Attach 时 `Head` 为 default，写入
**不记录命令**——已踩过的坑：「批量 Move 须 mutate 先行，摘除会 Detach 致命令不记录」）；后者不成立，
因为 C# 侧的**跨轨迁移本来就依赖游离实体**：

```csharp
// 同轨内重排（改 pos/dur）：摘除→跑 mutate→按新键重插。跨轨迁移属换父，仍走显式 RemovePart/InsertPart。
```

调序 / 跨轨迁移**必须**走实体，因为要保持**同一个对象身份**：note / 曲线 / effect 都挂在它身上，
undo 栈记录的也是这个对象；走 info 路重建出来的是**另一个对象**（新身份，且 remove+add 两条命令而非一次移动）。

所以两条路语义不同，**都必需**：

| 路径 | 语义 | 中间物 | 能落地几次 |
|---|---|---|---|
| `addX(info, index?)` | **复制 / 新建**（新身份） | info（纯数据，随便改） | 任意多次 |
| `insertX(entity, index?)` | **移动**（保持身份） | 游离实体（只读） | 一次（一个对象一个父） |

**实现要点**：现有代码所有成员都经一个 accessor 取底层对象

```csharp
IPart P => Removed ? throw new ScriptApiException("…") : Part;
```

把它拆成**读 accessor**（在树上 / 游离都放行）与**写 accessor**（游离态 throw，错误文本指路"先插回某条轨"）
即可全覆盖。

→ 已落地为 `P`（读）/ `W`（写）两个 accessor，五类句柄（track / part / note / vibrato / effect）各一对，
字段 `Removed` 改名 `Detached`。

> **实际是两态，不是三态。** 原稿设想的第三态"已丢弃（脚本结束仍游离的 `Dispose``）"**不能做**：remove
> 已经作为命令入栈，`Undo` 要把同一个对象放回树上；若脚本结束时把它 Dispose 掉，撤销就会恢复出一个已销毁的
> 对象。而且也**不必**做——数据层已在集合的 `ItemRemoved`/`ItemAdded` 上挂了 `Deactivate()`/`Activate()`
> （`Track.cs:57-58`、`Project.OnTrackAdded/Removed`），摘出即停活、插回即复活，脚本这层不需要额外生命周期。
> 于是"游离到脚本结束"就等于"删掉了"，与整改前的行为一致。

**连带简化**：`removeX(child)` 改为**返回游离句柄**，于是「删除」就是「摘出后不插回」——一个机制覆盖两种用法，
无需另设 detach 入口。这也正是数据层语义（`RemovePart` 只是摘除，之后丢弃还是插回由调用方决定）。

> 严格说返回值是**表达便利**（调用方本来就持有那个句柄）；真正的行为改变是**句柄摘出后不再作废**。
> `insertX` 同样交回句柄，故两头都能链式：`b.insertPart(a.removePart(p))`。定这个形状的依据见下节。
>
> `removeX` / `insertX` 都补了**父归属校验**（`ReferenceEquals(child.Parent, this)`），
> 把数据层里只有 `Debug.Assert` 兜着的"删非本容器成员"提升成脚本面的明确错误。

### 判据：脚本面该对齐 C# 还是对齐 JS 惯例

两者对齐的**不是同一件事**，分开就不冲突：

| 层面 | 对齐谁 | 为什么 |
|---|---|---|
| **能力面 / 分解方式**——哪些操作存在、怎么切分（三段式、增删挂父、排序键走 Move、可写 vs 只读派生、`insertX` 保身份 vs `addX` 换身份） | **C#** | 数据层是唯一真源；每偏离一点，脚本面就得自己维护一套本该由数据层保证的不变式（有序性 / 命令记录 / 对象身份） |
| **表达形态 / 人体工学**——命名、集合形状、坐标口径、纯数据形态、错误表达、返回值 | **JS** | 脚本作者（含 LLM）的先验来自 JS。本来就一直这样：集合给普通数组（C# 是链表）、位置给绝对 tick（C# 是 part 相对 + 锚点三元组）、纯数据给 plain object（C# 是 DTO 类）、非法用法 `throw`（C# 是 `bool` / `Debug.Assert`）、camelCase |

**冲突时的判据：这个差异是否承载语义。** 承载 → 跟 C#（丢了就表达不出那件事，哪怕更啰嗦，例：`insertX` 保身份、part 三元组几何、音素双列表）；只是表达习惯 → 跟 JS。

按这条判：

- **增删的返回值**不承载语义（返回的是刚传进去的同一对象，删掉它一点能力都不少）→ 归表达形态 → 看 JS 惯例。
  而 JS 里"插入"的返回值并不统一，把它们摊开看才有结论：

  | API | 返回 |
  |---|---|
  | `Node.appendChild(child)` / `Node.insertBefore(node, ref)` | 被插入的节点 |
  | `Element.append` / `prepend` / `before` / `after`（DOM4 那批新的） | `undefined` |
  | `Array.push(x)` / `unshift(x)` | 新长度 |
  | `Array.splice(i, 0, x)` | 被移除项（插入时为空数组） |
  | `Map.set(k,v)` / `Set.add(v)` | 容器自身（为链式） |

  底下那条真规律是：**JS 的 mutator 返回"调用方本来拿不到 / 算不便宜"的东西**（新长度、被移除项、容器），
  **不回声参数**。"插入返回被插入对象"只是老一代 DOM 的历史遗留，新一代刻意改成了 `undefined`。

  按这条规律：
  - `addX(info) → 句柄`：调用方没有别的途径拿到新对象 → **必须返回**（无争议）。
  - `insertX(child)`：回声参数、零信息 → **不返回**。
  - `removeX(child) → child`：也是回声参数，但这里有个不对称——**新一代 DOM 没有"父删子"的对应**
    （现代写法是 `child.remove()`，无参数、自然无可返回），故 `parent.removeChild(child)` 这个形状
    **唯一的先验就是返回被移除节点**；插入那边则新老并存且新的选择了不返回。于是定为
    **remove 返回、insert 不返回**，并让"移动"成为一个表达式：`b.insertPart(a.removePart(p))`。
- **非成员删除**：`Set.delete` / `Map.delete` 返回 `bool` 是因为**值/键型**集合里"不存在"是正常查询结果；父子归属是**归属型**，"不是我的孩子"是编程错误 —— DOM 的 `removeChild` 正是**抛** `NotFoundError`。
  而这也是数据层**自己写下的**判断：`Track.RemovePart` 的注释原话是"非成员删除是编程错误，DEBUG 期就地暴露，Release 仍宽容 no-op"。C# 那个 `bool` 是照 `ICollection<T>.Remove`（值型集合惯例）套的壳、自己又用 `Debug.Assert` 表达了"这其实是错误"；脚本层一律 `throw`，是把同一个判断一贯地表达出来。
  推论：脚本面 `removeX` 返回 `bool` 不只是"实用上是死重（恒为 true）"，而是**原则上就不对**。

## B 类：真机制封装 → 保留但纯化

### B1 维序（对应 `MoveX(item, Action mutate)`）

排序键**只有位置相关字段**，其余字段走 Move 是白付一次摘除-重插：

| 集合 | 排序键 | 原先白付 Move 的字段 |
|---|---|---|
| `NoteList`（`MidiPart.cs:939`） | `StartPos()`、`EndPos()`（= `pos`、`dur`） | `pitch`、`lyric`、`pronunciation` |
| `VibratoList`（`MidiPart.cs:343`） | `Pos↑`、同 `Pos` 时 `Dur↓` | `frequency`、`amplitude`、`phase`、`attack`、`release` |
| `PartList`（`Track.cs:141`） | `StartPos()`、`EndPos()`（= `pos`、`startOffset`、`endOffset` 三者） | `name` |

**整改**：排序键字段的 setter 内部继续走 `MoveX`（封装在 setter 里，脚本看到的是原子赋值）；
非排序键字段直接 `IDataProperty.Set`，不再套 Move。四个 `set({...})` 批量入口全部删除
（`part`/`note`/`vibrato` 的耦合了改名与移动，`track` 的更是连 Move 都没有、纯糖）。
改两个排序键字段就走两次 Move —— 多一条命令而已，正确性无损；真需要一次重排的是 UI 拖拽，那走 C# 侧 `MoveX`。

→ 已落地。注意 `PartList` 的排序键是**三个**几何字段（`IsInOrder` 先比 `StartPos()` 再比 `EndPos()`），
不只原稿写的"起点"；`track` 的字段则一个排序键都没有（`Tracks` 是按下标的有序表，位置由 `insertTrack` 的
index 决定），故全部直接 `Set`。

### B2 区间曲线（对应 `Clear` + `AddLine` 组合）

`part.setPitchLine` / `clearPitch` / `setAutomation` / `clearAutomation`，以及 effect 级的
`setAutomation` / `clearAutomation`。**这一组是 info 路无法替代的**：info 只能给整条曲线，
"只改 5–8 小节这一段"用 info 表达就得读全曲线 → JS 拼接 → 整条写回，既啰嗦又把无关区段也重写了。保留。

### B3 其他真机制

- `part.moveEffect(effect, index)` —— 脚本层自拼的 `RemoveEffect` + `InsertEffect`，C# 侧无 `MoveEffect`。
  但它**移动同一个对象而不重建**（`EffectInfo.Id` 是实例稳定标识，remove + insert(info) 会牵动身份），
  属"移动"范畴，保留。
- `project.setTempo(bpm, atTick?)` / `setTimeSignature(...)` —— 「该处已有标记则改、否则新增」的封装，
  对应 `TempoManager.SetBpm` / `AddTempo` 两个方法。保留。
- `note.lockPhonemes()` → `LockPhonemes()`；`note.clearPhonemes()` → `ClearLockedPhonemes()`。一对一，保留。
  （曾叫 `pinPhonemes`——脚本面后来把曲线侧的固定也暴露成动作，同一范式两个动词会让模型猜出 `lockPhonemes`/
  `pinPitch` 这类不存在的名字，故统一到 `lock`；状态字段同步为 `hasLockedPhonemes`。数据层的 `HasPinnedPhonemes`
  与中文"钉死"未动，那是宿主内部用词、不在动作面上。）
- `part.lockPitch()` / `part.lockAutomation(id)` / `effect.lockAutomation(id)` → `SynthesisLock` 的
  `CaptureSynthesizedPitch` + `WriteSynthesizedPitchLock` / `CaptureSynthesizedParameter` + `WriteSynthesizedParameterLock`。
  两步是**同一个动作的必然分解**（先冻结产物引用再写：写入即触发合成失效，引擎可能随即清掉回显），
  笔刷式 UI 因为要逐帧重写才拆成两个方法，脚本一次调用两步都走。与音素侧的 `lockPhonemes` 同族（同一个动词、同一个范式）。
  区间可省（= 整轨，用 `SynthesisLock.WholeTrackStart/End`），配套只读判据 `hasSynthesizedParameter(id)`
  = `HasPairedSynthesizedParameter`。返回 `bool` 是**新增的显式性**，非数据层原有：产物为空时 UI 用户看得见白刷，
  脚本调用方看不见，故把 no-op 如实回报（用法错误仍走 throw，两者分开）。
- `part.setSoundSource({kind, type, id})` → `SoundSource.SetInfo(SoundSourceInfo)`。**参数就是该 info 的全部字段**，
  已经是"吃完整 info"的形状；额外的存在性校验（未知音源报错而非静默回退空源）属必要守卫。保留。

## 纯糖 → 删

- `track.set({...})` —— `ScriptTrack.Apply` 里无任何 Move，只是逐个 `IDataProperty.Set`，分开写完全等价。
- `part.notesInRange(start, end)` —— 脚本层自己的 LINQ filter，C# 侧无对应；
  可完全由 `part.notes().filter(n => n.pos >= a && n.pos < b)` 表达。
  （唯一在用它的 `tests/scripts/kb-piano-range.js` 已改成 filter 写法。）
- `part.duplicate` / `track.duplicate` —— 等价于 `addX(getInfo())`，属同一件事的第二条路
  （见 `agent-tools.md`：同一件事多条路只会降模型选择准确率、堆 prompt）。若将来大 part 的映射开销成为
  实测问题，再作为**纯优化**加回（那时语义等价，选哪条都对）。

→ 全部删除。`duplicate` 的实现曾短暂进过工作区（未提交），本轮随 `addX(info)` + `getInfo()` 就位一并撤下。

**连带修的一处"文档在教模型做错事"**：两份 ScriptDoc 里那个示例标题恰是「复制第一轨 / Duplicate the first
track」，而它示范的正是 `addPart` + `addNote` 逐个重建（丢音源 / 曲线 / effect / 音素）。已改成
`getInfo()` → `addTrack(info)`，并附反面警示（逐字段重建会**静默**丢保真——看着像成功了，实际只剩音符骨架）。
`ScriptApiReference.cs` 里同步加了 info / 移动两节与三个示例。这条很可能就是 agent"挨个读挨个造"的直接来源。

## 本来就对称（无需改动）

- 各句柄的标量字段（`note.pitch`、`track.isMute`…）= `IDataProperty.Set`，本就原子。
- `getProperty` / `setProperty`（part / note / phoneme / effect 四处）= `Properties.SetValue(key, value)`。
- 集合读取 `tracks()` / `parts()` / `notes()` / `vibratos()` / `effects()` / `phonemes()`。
- 采样读 `samplePitch` / `sampleAutomation` = `GetFinalPitch` / `GetAutomationValues`；
  `automationIds()` = 读 `AutomationConfigs.Keys`。
- 删除 `removeTrack` / `removePart` / `removeNote` / `removeVibrato` / `removeEffect` / `removePhoneme`
  （落地后前五个改为返回游离句柄，见上"游离实体"一节；`removePhoneme` 无游离态，仍返回 void）。
- 编辑器态只读（`tl.currentPart()` / `selectedParts()` / `selectedNotes()` / `playhead()` / `snap()` /
  `trackSelection()` / `pianoSelection()` / `ppq` / `language`）—— 无数据层对应是**应然**：它们是编辑器状态，
  不是工程数据。
- `project.importTracks(path)` —— 宿主编排（`FormatsManager.Deserialize` + `AddTrack` 循环），非数据层单成员。

## info 入口校验 —— ❌ **前提不成立，未实施**

原稿的判断是：`MidiPart.SetInfo`（`MidiPart.cs:814`）把 info 列表直接塞进 `SortedDataObjectLinkedList<INote>`，
无序 info 会当场破坏"声称有序"的链表不变式，症状出现在很远处。

**核实后不成立。** 那条路根本不是"整块塞进去"，而是清空后**逐个按键插入**：

```csharp
// SortedDataLinkedList.SetInfo —— 注释原话：「有序表只按键插入：逐个 Insert 即可定位（输入已有序时游标令其接近 O(n)）」
public void SetInfo(IEnumerable<T> info) { using var _ = MergeNotify(); Clear(); foreach (var item in info) Insert(item); }
```

而 `SortedLinkedList.Insert` 是**按 `isInOrder` 定位**后再拼接（带 `mCursor` 就近扫描优化），类注释也明写
「对外只暴露按序定位的 Insert + Remove……有序不变量无法被外部旁路」。所以无序 `info.Notes` 进来**只是插得慢一点**
（游标优化失效、退化成扫描），落地后集合仍然有序。Note / Vibrato / Part 三个有序集合都走这条同一路径。

反过来说，**加校验反而有害**：它会把仅仅"顺序不对"的合法数据（第三方 format 插件、手捏的 info）拒之门外，
而这类数据本来能被正确接纳。故这一项**取消**，脚本 info 路只保留内部不变式级校验（`dur>0`、pitch 值域、
part 类型判别），不做有序性校验。

## 顺带修的数据层不一致（本轮 review 逼出来的）

对着 C# 面逐成员核对时，暴露出三处数据层自身的不一致，都已修（与脚本面无关，但同一条"别留半个惯例"的纪律）：

1. **`IProject.RemoveTrack` 曾是 `void`**，而 `ITrack.RemovePart` / `IMidiPart.RemoveNote`·`RemoveEffect`·
   `RemoveVibrato` 都是 `bool`。.NET 惯例是 `ICollection<T>.Remove(item) → bool`（"它原本在不在"），
   底层 `DataObjectList.Remove` 本来就返回 `bool`，只是被这层吞掉了。已改为 `bool`。
2. **按下标的 `*At` 家族越界时静默 `return`**（`Project.RemoveTrackAt` / `TempoManager.RemoveTempoAt` /
   `TimeSignatureManager.RemoveTimeSignatureAt`，外加 `Project.InsertTrack` 的越界 guard）。
   `RemoveAt` 之所以能没有返回值，前提正是**越界会抛**；既不抛又不返回等于"半个惯例"——调用方彻底无从
   得知什么都没发生，宽容只把 bug 藏起来。已一律改为 `throw new ArgumentOutOfRangeException`。
3. **`insertX` 的返回值**：脚本面曾让五个 `insertX` 返回句柄，无 C# 依据（C# 侧五个 `Insert*` 全为 `void`）
   且买不到能力，已删——见上"游离实体"一节。

**第 4 条（后一刀补齐）**：`TempoManager.SetBpm(index, bpm)` 与 `TimeSignatureManager.SetMeter(index, …)`
同为按下标寻址 + 越界静默 `return`，按第 2 条判据已一并改抛（.NET 里 `list[i] = x` 越界即抛）。

这一刀顺带纠正了第 2 条原先的一句错话——「现有调用点全在 UI 且下标取自集合自身，故行为无碍」**不准确**：
四个**对象版扩展方法**（`ITempoManagerExtension.RemoveTempo`/`SetBpm`、`ITimeSignatureManagerExtension`
的对应两个）是 `XxxAt(list.IndexOf(obj))` 的形状，**`IndexOf` 找不到时返回 -1**，会把 -1 透传到已改抛的
下标层——且异常原因会被误报成"下标越界"，而真实原因是"这东西不属于本 manager"。故：

- 四个对象版一律先取 `index`、`Debug.Assert(index >= 0, …)` 后守卫 `return`，**-1 绝不透传**。
  这是照 `ITrack.RemovePart` / `IMidiPart.RemoveNote` 的既有范式（非成员是编程错误 → DEBUG 期就地暴露、
  Release 宽容 no-op），不为此另立一套抛异常的规矩。
- 唯一的对象版调用点 `TimelineView.OnBpmInputComplete` / `OnMeterInputComplete` 补成员检查：bpm/拍号
  输入框开着期间那个标记可能已被**撤销**撤掉（`mInputBpmTempo` 持的是编辑开始时抓住的对象引用，而原先
  只判 `!= null`），此时放弃本次编辑并收起输入框。成员资格由发起方确认，不靠数据层宽容兜着。

## 落地后的校验位置（实施记录）

「info 阶段零心智负担、落地那刻才校验」按下面这条线划分，写在 `ScriptInfo.cs` 类头：

- **落地校验**（`Read*Info` 系列，被 `addX`/`insertX` 调用时才跑）：`dur > 0`、pitch 钳到
  `[MIN_PITCH, MAX_PITCH]`、`endOffset > startOffset`、part 起点 `pos + startOffset >= 0`、
  `type` 必须是 `"midi"`/`"audio"`、`kind` 必须是 `"voice"`/`"instrument"`。
- **存在性校验只在"按名字指定一个引擎"的显式入口**：`part.setSoundSource` 的 type/id、`part.addEffect` 的顶层
  `type`。嵌在 info 树里的 `soundSource` / `effects` **刻意不校验**——那条路的职责是忠实搬运，必须能带着
  孤儿数据往返（引擎卸载后工程文件照样能开、`getInfo()` → `addX()` 照样保真）。这与"孤儿自动化数据保留隐藏"
  是同一条判例。
- **`EffectInfo.Id` 的链内唯一性**是真不变式（颤音影响表按它做外键），故 `addEffect(info)` 发现 id 与目标链里
  已有 effect 撞号时清空它、让宿主发新号——正是 `EffectInfo.Id` 注释要求的"克隆进同一条链须显式清空"。
