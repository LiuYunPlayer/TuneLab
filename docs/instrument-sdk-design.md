# TuneLab Instrument 插件 SDK 设计

> 配套文档：[voice-sdk-design.md](voice-sdk-design.md)。Instrument 与 Voice 在调度、隔离/快照、
> automation、Config 家族上**机制同构**——本文只详述两者的**差异**与 instrument 专属面，同构部分
> 指向 voice 文档对应章节，不复述。

## 0. 定位与为何要独立类型

Instrument 是**多声部音源**插件类型（合成器 / 采样器 / 和弦音源），与 voice / effect / format 并列。
核心特征：

- **多声部 / 和弦**：宿主**裸传重叠 note、不去重叠**——这与 voice 相反（voice 喂给插件的是去重叠后的
  单声部「有效末」边界）。instrument 引擎原味消费重叠几何，自己决定每个 note 怎么发声、怎么叠。
- **无音素系统、无歌词**：没有 `Lyric` / `Phonemes`（那是 voice 专属）。
- **产物仅音频**：无音素回填、无音高回显；v1 **纯按 note 的整数 pitch 发声**，不读 pitch 曲线
  （弯音 / 滑音是 v2 加性扩展，见 §4）。effect 链对 instrument 输出与对 voice 输出**同样生效**
  （音频段是中性承载单元）。
- **挂载**：像 voice 一样挂在 `MidiPart` 上；一个 part 的音源是 voice **XOR** instrument（二选一）。

### 为何不能复用 voice SDK，也不走能力位

voice SDK 喂给插件的 note 边界是**去重叠后的有效末**（钳到下一 note 起点，单声部音频口径），
且不再暴露 note 满末——voice 插件**彻底拿不到任何重叠几何**。多声部引擎必须要原始重叠 note，
这是与 voice 的实质分水岭。

曾设想用一个能力位（如 `VoiceSourceInfo.IsPolyphonic`）让单一 SDK 同时服务单 / 多声部——**该方案已废、
回退过**。原因：单 / 多声部的 note 末端语义根本不同（钳位 vs 满末），用一个开关切换会让快照口径、
去重叠责任、音素布局全部条件分叉，复杂度反而高于分两个类型。**故独立成 instrument 类型。**

> **唯一不会收敛的分水岭 = note 末端语义。** 其余能力（pitch 曲线、参数回显、乃至将来若需要的更多
> 输入）都能在两个类型间**各自加性补齐**（§4），但 voice 的「钳位单声部末」与 instrument 的「满末
> 重叠」是定义性差异，不随功能增补而趋同——这正是分家成立的根基。

---

## 1. 与 Voice 的关系：平行族 + 共享中性叶子 + 宿主单核

冻结 ABI 上的原则：**解耦 > DRY**；宿主（非 ABI 机器）尽量复用。落实为三层：

### 1.1 平行族（ABI 面，互不继承）

Voice 与 Instrument 各持一套 `engine / session / context / note / snapshot / 属性上下文`，
**彼此无继承关系**。代价是几个接口成员形似重复；收益是任一类型加成员都不污染另一类型的冻结面——
instrument 加 pitch 不碰 voice，voice 加成员也不碰 instrument，各自独立演化。这正是「解耦 > DRY」的兑现。

### 1.2 共享中性叶子类型（无领域语义，两族共用）

下列类型零 voice / instrument 语义，作为公共叶子被两族（及 effect）直接复用，无平行副本：

- `IAudioSegment`（音频承载 + effect 失效单元）
- `ISynthesisAutomation`（+ `IAutomationEvaluator`）、`SynthesisAutomationSnapshot`
- `SynthesizedParameter`（参数回显富类型）
- `SynthesisRange`（调度提示）、`SynthesisStatusSegment`（状态时间线）
- `AutomationConfig`（轨声明，在 `ControllerConfigs/`）

> 物理位置：除 `AutomationConfig`（属 `ControllerConfigs/`）外，上列共享叶子统一落 `TuneLab.SDK/Synthesis/` 文件夹——`Synthesis*` 前缀与该文件夹同义=共享中性；域专属类型则分落 `Voice/` 与 `Instrument/`（命名空间一律平铺 `TuneLab.SDK`，文件夹仅作分桶）。

> 注：`IVoiceSynthesisPartPropertyContext` / `IVoiceSynthesisNotePropertyContext` **不在**中性集——它们携带音源身份 id
> （voice 侧是 `VoiceId`），故 instrument 另有平行的 `IInstrumentSynthesisPartPropertyContext`
> （携带 `InstrumentId`），见 §2。

### 1.3 宿主单核（非 ABI，最大复用）

宿主侧的合成机器——会话级活视图代理（note / automation / property 的 `DerivedProperty` 借壳）、
快照物化器、逐步调度管线、`EffectGraph`、音频段登记——**一套实现两族共用**，只经两个策略钩子分叉：

1. **note 投影策略**：voice = 钳到有效末 + 暴露 Lyric/Phonemes；instrument = note 满末 + 无 Lyric/Phonemes。
2. **产物回填策略**：voice = 音素回填到 note；instrument = 无回填（仅音频 + 可选参数回显）。

`EffectGraph` / 音频段 / 调度循环对两族**字节级相同**——instrument 输出照常进 effect 链。

---

## 2. 顶层接口（instrument 专属面）

> 调度（`GetNextSegment` / `SynthesizeNext`）、音频交付（`CreateAudioSegment` / `IAudioSegment`）、
> 隔离与快照模型、automation 双语义与 Config 家族——与 voice **完全同构**，见
> [voice-sdk-design.md §3.5 / §4 / §7 / §8](voice-sdk-design.md)。下面只列与 voice 的差异。

### 2.1 `IInstrumentSynthesisNote`（满末、无语音）

```
IReadOnlyNotifiableProperty<double> StartTime   // 全局秒
IReadOnlyNotifiableProperty<double> EndTime      // note 满末（Pos+Dur 换算秒），不钳位
IReadOnlyNotifiableProperty<int>    Pitch
IReadOnlyNotifiablePropertyObject   Properties
IInstrumentSynthesisNote? Next / Last                     // 邻居链（数据线程分片导航）
```

- 与 `IVoiceSynthesisNote` 的差异：**`EndTime` 是满末、不钳位**（这是分水岭）；**无 `Lyric`、无 `Phonemes`**。
- `Notes` 链表**直传原始可重叠 note**（和弦 / 多声部裸喂）；排序契约同 voice（StartTime 升序 →
  同起点 EndTime 降序 → 宿主插入序），但**不做任何去重叠**。

### 2.2 `IInstrumentSynthesisContext`（去掉双音高通道）

```
IReadOnlyNotifiableLinkedList<IInstrumentSynthesisNote> Notes
IReadOnlyNotifiablePropertyObject PartProperties
IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }
InstrumentSynthesisSnapshot GetSnapshot(IReadOnlyList<IInstrumentSynthesisNote> notes, double startTime, double endTime)
IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate)
event Action? Committed
```

- 与 `IVoiceSynthesisContext` 的差异：**删 `Pitch` / `PitchDeviation`**（v1 纯 note pitch 发声）。
- **通用 automation 轨经只读 map `Automations`**（可点取 `TryGetValue` / 可枚举）：引擎仍可声明力度 / 表情 / 动态等轨
  （与 pitch 曲线无关）；不声明即零轨。

### 2.3 `IInstrumentSynthesisSession : IDisposable`（砍语音产物）

```
SynthesisRange? GetNextSegment(double startTime, double endTime)
Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default)
IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters   // 参数回显（引擎声明才有）
IReadOnlyList<SynthesisStatusSegment> GetStatus()
event Action? SynthesizedParametersChanged
event Action? StatusChanged
```

- 与 `IVoiceSynthesisSession` 的差异：**删 `DefaultLyric`、`SynthesizedPhonemes`、`SynthesizedPitch`**
  及其事件。音频仍走 `context.CreateAudioSegment`（不在 session 面）。

### 2.4 `IInstrumentSynthesisEngine`

```
IReadOnlyOrderedMap<string, InstrumentSourceInfo> InstrumentSourceInfos
void Init() / void Destroy()
IInstrumentSynthesisSession CreateSession(string instrumentId, IInstrumentSynthesisContext context)
IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IInstrumentSynthesisPartPropertyContext context)
IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IInstrumentSynthesisPartPropertyContext context)
ObjectConfig GetPartPropertyConfig(IInstrumentSynthesisPartPropertyContext context)
ObjectConfig GetNotePropertyConfig(IInstrumentSynthesisNotePropertyContext context)
```

- 与 `IVoiceSynthesisEngine` 同构，差异仅命名（`VoiceSourceInfo` → `InstrumentSourceInfo`，声明上下文携带
  `InstrumentId`）。note 级属性面板保留——per-note 力度 / 演奏法仍有用。

### 2.5 值 / 快照类型

- `InstrumentSourceInfo`：`Name` / `Description` / 可选 `Portrait`（同 `VoiceSourceInfo`）。
- `InstrumentSynthesisNoteSnapshot`：`StartTime` / `EndTime`(满末) / `Pitch` / `Properties`，无 Lyric/Phonemes。
- `InstrumentSynthesisSnapshot`：`Notes` / `Automations`(只读 map：`IReadOnlyMap<string, SynthesisAutomationSnapshot>`) / `PartProperties`，**无 Pitch/PitchDeviation**。
- `IInstrumentSynthesisPartPropertyContext`：`InstrumentId` + `PartProperties`；`IInstrumentSynthesisNotePropertyContext`
  加 `NoteProperties`。语义同 voice 的属性上下文。

---

## 3. 与 Voice 同构、不复述的机制

以下机制 instrument 与 voice **逐字相同**，直接套用 voice 文档，不另设计：

- **引擎生命周期与错误**：`Init`（懒）/ `Destroy`，失败抛异常、宿主在调用边界 catch。
- **调度**：宿主驱动逐步合成，`peek`（`GetNextSegment`）+ `commit`（`SynthesizeNext`）同窗口确定性
  重导出，并发槽位账本管控。见 voice §4。
- **隔离与快照**：活视图仅数据线程；合成只读 `InstrumentSynthesisSnapshot`（不可变值树，构造 happens-before
  offload）。见 voice §3.5。
- **automation**：连续 / 分段两形态由 `AutomationConfig.DefaultValue` 是否 NaN 区分；区间失效订阅
  `ISynthesisAutomation.RangeModified`；快照开窗冻结求值器。见 voice §7。
- **Config 家族 / 声明求值**：声明类 config 是当前 part 参数值的纯函数、求值在引擎层（不依赖会话实例），
  宿主在「建会话之前」填好声明。见 voice §8。

---

## 4. 加性演化与能力边界

未来若要给 instrument 补 voice 已有的能力（典型如 pitch 曲线 / 弯音），**是否纯加性取决于该面是
宿主实现还是插件实现**：

| 后续要加的能力 | 落在哪个面 | 谁实现 | 是否纯加性 |
|---|---|---|---|
| **Pitch / PitchDeviation 曲线（输入）** | context + snapshot | 宿主 | ✅ **纯加性**（含二进制兼容） |
| Lyric / Phonemes（输入） | note + snapshot | 宿主 | ✅ 纯加性 |
| 更多 automation 轨 | context（已支持） | 宿主 | ✅ 引擎声明即用 |
| SynthesizedPitch（音高回显，输出） | session | 插件 | ⚠️ 需 DIM 默认 / 子接口 |
| SynthesizedParameters 之外的输出产物 | session | 插件 | ⚠️ 同上 |

**规则**：
- **宿主实现、插件只读的面**（`IInstrumentSynthesisContext` / `IInstrumentSynthesisNote` / `InstrumentSynthesisSnapshot`）：
  加成员 = 宿主多给一个属性，旧插件不读照常跑、新插件按需读——**纯加性，连旧插件二进制都不受影响**
  （旧 DLL 根本未引用新成员）。**v1 砍掉 pitch 曲线因此零风险、完全可逆**：将来支持弯音时，只是宿主把
  `Pitch`/`PitchDeviation` 加回 context + 快照多采一道曲线即可。
- **插件实现的面**（`IInstrumentSynthesisSession` / `IInstrumentSynthesisEngine`）：裸加成员要求插件实现它、是破坏性的。
  **约定**：任何后续在插件实现面新增的输出成员，一律用**默认接口方法（DIM）给 `Empty` 兜底**
  （net8 / C# 12 支持），使输出侧增补也保持加性、不破已装插件。

平行族的红利：上述任何增补都**只动 instrument 一侧、完全不碰 voice**。

---

## 5. 宿主集成

- **注册**：扩展 manifest 新增 `kind="instrument"`；`ExtensionManager.RegisterEntry` 加 `case "instrument"`，
  `IsCodeKind` 纳入；新增 `InstrumentsManager`（镜像 `VoicesManager`：内建空引擎回退、多包并存、惰性 Init）。
- **冲突路由**：`ExtensionRouting` 新增 `"instrument"` 行（`RouteKey("instrument", type)`），
  多包同 type 并存、用户在「Extension Routing」矩阵选活实现，选择存 `Settings.json`。
- **数据层挂载**：`MidiPart` 音源为 voice **XOR** instrument——持有「音源种类 + 对应 Info」的判别联合，
  序列化按种类分支（不引入统一 `IMidiPartSource` 抽象：两者声明面不同，强抽象反而漏抽象）。
- **合成管线**：`InstrumentSynthesisPipeline` 复用 `EffectGraph` 与音频段机器，仅去掉音素回填那条产物分支。
- **UI**：「Set Voice」泛化为「设置音源」入口，voice / instrument 分组二选一。

---

## 6. 待办与缓后

- v1：纯 note pitch 发声、音频 + 参数回显；不读 pitch 曲线、无弯音 / 滑音。
- 缓后（均为加性，见 §4）：pitch 曲线输入（弯音）、合成音高回显输出、per-note 演奏法的更丰富面板。
- 参考实现：`tests/plugins` 下补一个最小 instrument 样例（多声部正弦合成器）作为 AI 参考与回归夹具。
