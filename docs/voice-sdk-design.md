# TuneLab Voice 插件 SDK 设计

本文档定稿 voice 类插件的 SDK 接口与宿主交互模型，供后续实现参照。所有接口为草案签名，落地时以本文语义为准、细节可微调。

---

## 0. 设计哲学

贯穿全篇的几条原则：

1. **厚插件 / 会话托管**：合成是一段长生命周期的有状态过程。分片、调度状态、音频缓冲、合成进度、失效（dirty）判定，**全部由插件自己托管**；宿主只负责把工程数据的变更流推给插件、驱动调度、读取产物并展示。理由：失效依赖图（如 `音素时长 → 音高 → 音频` 的分级管线，改自动化只需重渲音频）只有引擎知道，宿主无从复制。
2. **声明 vs 执行分层**：插件对外有两件事——*声明*（这个声源暴露哪些参数/自动化/属性、默认歌词等）和*执行*（合成）。
3. **小冻结面、富行为**：跨 SDK 边界冻结的类型尽量少、尽量稳；富领域类型（分片、音素细节、曲线几何）留在插件侧或宿主侧，自由演进。每纳入一个 SDK 类型 = 一次永久 ABI 承诺，克制增长。
4. **领域知识归引擎**：凡是需要音韵学/声学知识才能做对的事（音素如何随音符伸缩、分片边界、失效粒度），逻辑归引擎；宿主不替它做近似覆盖。
5. **数据=具体类型、服务=接口注入**：数据对象直接 new；跨边界的服务用接口暴露、演进靠默认接口方法 + 版本治理。

---

## 1. 顶层结构

两层（取消了原 `IVoiceSource` 中间层，其职责并入会话）：

```
IVoiceSynthesisEngine        每"引擎类型"一个：加载模型、列声库目录、创建合成会话、声明（轨/面板/回显）
  └ IVoiceSynthesisSession   每"part 合成"一个：默认歌词 + 调度 + 产物 + 状态
```

```csharp
public interface IVoiceSynthesisEngine
{
    // 声库目录（菜单/选择器用，无需创建会话即可读）
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; }

    void Init();      // 见 §2
    void Destroy();

    // context 为该 part 的输入活视图（含 VoiceId，见 §3）；voiceId 已并入 context、不再单列。
    IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context);

    // 声明面（当前值的纯函数、不依赖会话；context 是承载活视图的薄壳，见 §8）。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context);
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context);
    ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context);
    // per-phoneme 自定义属性（required；复用 note 声明上下文，返回与 context.Notes 各 note 音素扁平展开索引对齐的 config 列表，见 §6 音素属性）
    IReadOnlyList<ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context);
}
```

**为何取消 `IVoiceSource`**：它原本承载"某个声源的声明 + 合成任务工厂"。现声明直接挂在会话上、合成由会话本身承担；且声源对象本就是每 part 一份（不共享），与会话一一对应。曾担心的"构造顺序循环"（part 的自动化轨需要 schema、schema 在会话上、会话又依赖 part 数据）不成立：声库元数据由 `VoiceSourceInfos` 目录在创建会话前就提供；自动化等 schema 的*取值默认*由插件自己用其声明填充（宿主不需要 schema 即可构造 context），schema 仅供 UI 在会话创建后消费；且任何"有声源的 part"必有会话（会话是轻量句柄，重模型加载是懒的）。

---

## 2. 引擎生命周期与错误

- **`Init()` 无参、失败抛异常**。不传 `enginePath`：插件 DLL 经 `Assembly.Location` 即可自定位包目录（宿主全程 `LoadFromAssemblyPath` 加载，`.Location` 必有值）。失败抛异常、宿主在调用边界 catch——归属靠*捕获点*判定（从插件调用边界出来的就是插件侧责任），不靠异常类型；故**暂不定义基类异常**（无消费者区分"受控失败 vs 崩溃"，YAGNI；将来要做友好提示再引入）。
- **仅有状态插件才有 Init/Destroy**。判据是*是否跨调用持有昂贵常驻状态*（voice/effect 加载模型 = 有状态），不是"重/轻"直觉。纯过程插件（如 format 的 `Deserialize`）无 Init。Init 本就是懒调用（首次用到才 Init），并不消除首次延迟，只是给宿主一个可主动预热的钩子。
- **采样率**：工程采样率是唯一真值，放全局宿主上下文（`TuneLabContext.Global`）。插件优先按此率产音频，并暴露自己**实际的 native 输出率**（产物上的 `SampleRate`）。宿主比对：相等直读、不等时套一层流式重采样（`WdlResamplerStream`，仓库现成；MediaFoundation 版为 Windows 高质量备选）。脏区在插件 native 域精确上报，宿主映射到工程率时按滤波器支撑略外扩即可。此法让重采样集中宿主一处、不逼每个插件自带，且让会话与工程率变化解耦。

---

## 3. 合成会话模型（核心）

### 3.1 创建与生命周期

`session = engine.CreateSession(context)`。会话绑定一个 part，活到 part 被删除（`Dispose`）。换声源（换引擎）时：宿主丢弃旧会话、重建新会话，**context 也随会话重建**（稳定的是其背后的数据层）。

**`voiceId` 已并入 `context`（修订）**：选定声库是会话 context 在其生命内的**不可变身份**（换声库 = 重建 context + 会话），故作为 `IVoiceSynthesisContext.VoiceId` 暴露，不再与 context 并列传——`CreateSession` 只收 context。（声明面是另一回事：走独立的调用级只读值视图 `IVoiceSynthesisPartView`/`IInstrumentSynthesisPartView`，音源 id 由 `IVoiceSynthesisPartView.VoiceId`/`IInstrumentSynthesisPartView.InstrumentId` 承载，见 §8。）

### 3.2 输入：`IVoiceSynthesisContext`（会话级、订阅式活视图）

context 由**宿主实现、会话级**（每次 `CreateSession` 新建、随会话死），向插件暴露**可订阅属性**。插件用与宿主侧一致的手感订阅：

```csharp
public interface IVoiceSynthesisContext
{
    string VoiceId { get; }   // 选定声库（IVoiceSynthesisEngine.VoiceSourceInfos 的 key）；context 生命内不可变，换库重建
    // 链表形态（无索引承诺——宿主数据层即双向链表，可索引是插件不需要的承诺）：
    // 顺序消费用枚举、头尾 O(1) 走 First/Last、邻居导航走 note.Next/Last；支持 WhenAny。
    IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> Notes { get; }
    PropertyObject PartProperties { get; }                    // 可订阅
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }   // 全部已声明轨（可点取 TryGetValue / 可枚举）
    ISynthesisAutomation Pitch { get; }            // 绝对约束：有值=用户钉死，NaN=插件自由
    ISynthesisAutomation PitchDeviation { get; }   // 加性偏差：处处有值、默认 0、永不 NaN

    // 物化合成快照（插件主动拉取，见 §3.5/§4）：notes = 本次合成所需 note（含协同发音邻居，
    // 插件自由圈定，返回 snapshot.Notes 与之索引对齐）；[startTime, endTime] = 曲线开窗区间（秒）。
    // 仅数据线程、仅 SynthesizeNext 同步前缀调用；一次合成可按需拉多份。
    VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes, double startTime, double endTime);

    // 音频产物的宿主分配工厂（见 §5）：插件合成产出音频时申请一个段握柄，写入、Commit() 标完成，
    // 重分片（或改长度/位置）时 Dispose() 释放重建。宿主据此持有段登记表、驱动下游 effect 链按段重渲染。
    // 仅数据线程调用；sampleOffset = 全局起始采样位置（native 率），sampleCount = 段长（采样数）。
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount);

    event Action? BatchBegin;       // 批量变更括号，见 §3.4
    event Action? BatchEnd;
}

// automation 的会话级活视图：求值 + 区间变更订阅（镜像宿主数据层 RangeModified 语义）。
// 插件由此做最细粒度失效："某轨 [start,end) 变了 → 只标脏覆盖该区间的段"。
// 注：IAutomationEvaluator（纯求值，TuneLab.SDK、voice/effect 共用；查询轴统一全局秒——
// 插件侧只面对秒轴）与本接口的分离是"活视图可订阅 / 冻结面无事件"双视图的类型化体现；
// 继承关系维持（is-a 成立：同一份采样例程可同型吃活视图与冻结求值器）。
public interface ISynthesisAutomation : IAutomationEvaluator
{
    IActionEvent<double, double> RangeModified { get; }   // (startTime, endTime)，全局秒
}
```

**音高双通道（绝对约束 + 相对偏差）**：`Pitch` 是用户钉死的绝对音高曲线（分段型：有值=钉死、NaN=插件自由发挥）；`PitchDeviation` 是加性偏差（连续型：处处有值、默认 0、永不 NaN），宿主侧偏差源（vibrato，将来 humanize 等）全部汇于此。合成契约：**`finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)`**——插件先解析绝对面（钉死区用用户值、自由区自己生成），再叠加偏差。由此偏差也作用于未绘制 pitch 的自由区域（旧管线把 vibrato 叠在绘制曲线上，自由区无载体、偏差丢失——结构性修复）。失效通道随之分流：pitch 曲线变更 → `Pitch.RangeModified`；vibrato 几何/包络变更 → `PitchDeviation.RangeModified`。

**变更定位的三种最小事实**：字段变了（note 可订阅属性，配合 `WillModify`/`Modified` 拿新旧值）、区间变了（曲线 `RangeModified` 带秒范围）、集合变了（`Notes` 增删）。失效依赖图（这些事实映射到哪些段、重合成到管线哪一级）归插件——机制粒度足够支撑最精细策略，也允许懒插件"任何通知 → 全部标脏"。**不设独立的"时基变了"信号，也不做增量分解**：时基变更（tempo 表 / part 平移）→ 宿主整体重建会话（旧会话 `Dispose`、新 context 新会话即新秒值），三种事实只在 note / 曲线 / 集合自身被编辑时触发（详见 §3.3）。

**命名约定（前缀编码桶 + 线程纪律）**：一条分层规则——① **域专属**类型带与所属文件夹一致的域前缀（`Voice*`/`Instrument*`，对称如 `IVoiceSynthesisSession`↔`IInstrumentSynthesisSession`、`IVoiceSynthesisNote`↔`IInstrumentSynthesisNote`、`VoiceSynthesisSnapshot`↔`InstrumentSynthesisSnapshot`）；② **共享中性**类型用 `Synthesis*` 前缀（或本就中性的 `IAudioSegment`/`IAutomationEvaluator`），统一落 `TuneLab.SDK/Synthesis/`，零 voice/instrument 语义、两族（及 effect）共用零改名（effect 收敛直接复用见 §10）；③ **产物例外**——engine→host 的输出值用 `Synthesized*` 角色前缀、不受桶规则约束（`SynthesizedPitch` 属 voice 落 `Voice/`、`SynthesizedParameter` 共享落 `Synthesis/`）。会话**容器**用域名（`IVoiceSynthesisContext`=voice、`IEffectContext`=effect，因其暴露面本质不同、不可共用一名）；活视图与冻结快照成对、靠 `*Snapshot` 后缀区分（活=裸名、冻=`+Snapshot`）：`IVoiceSynthesisNote`↔`VoiceSynthesisNoteSnapshot`、共享活视图自动化 `ISynthesisAutomation`↔`SynthesisAutomationSnapshot`——活视图不再靠 `Live` 前缀标记（已弃，活视图本就不靠前缀消歧）；`*Snapshot` 后缀 = 不可变冻结物家族（纯值无事件，可跨线程）；`IAutomationEvaluator`/`ITiming` = 横跨两域的求值/换算能力接口（实现可活可冻，接口面不带事件）。活视图上的事件恒在数据线程触发与处理；快照上**没有**事件（类型上拿不到，"把回调留到合成线程"写不出来）。出方向（插件→宿主）的 `StatusChanged` 允许任意线程触发、宿主负责 marshal（v2 跨进程时它本就是 IPC 消息）；进度经状态带（`SynthesisStatusSegment.Progress`）+ `StatusChanged` 上报（不单设 `IProgress` 推送参数；将来如需独立推送通道再加性补）。

**纪律的强制层级**：插件持有 context 引用、技术上可以在 worker 线程访问活视图——进程内无法类型强制（C# 无线程所有权类型系统），"仅数据线程"是纪律性约束。三道防线：① 命名纪律（本节）；② 宿主 context 实现的各取值/枚举入口带**数据线程断言**（DEBUG 编译），违例插件在开发期第一次跨线程访问即抛异常，而非静默数据竞争；③ v2 进程隔离后物理强制（context 留在宿主进程，worker 进程摸不到引用）。

**为何用订阅式而非把 `SynthesisChange` 结构体推给插件**：结构体 + switch 太难用，远不如回调。订阅式既给了插件熟悉的手感，又能精确到字段、天然有序。

**为何不直接暴露宿主数据层的 NotifiableProperty（避免泄漏的关键）**：直接订阅长寿数据层有两个坑——① 跨 ALC 订阅 = 生命周期/泄漏陷阱（插件忘退订则宿主对象 GC 不掉）；② 插件 handler 跑在宿主线程、宿主 mutation 关键区里，烂实现（抛异常/阻塞/重入）会拖垮宿主。

解法：**context 是会话级的中间层**，由宿主驱动 emit。插件订阅的是 context（短命，随会话一起死 → 泄漏*结构性*不可能，无需弱事件、无需契约）；context 内部订阅长寿数据层、由宿主转发，宿主因此始终握着*线程 / 时机 / 故障隔离 / 批量*四个旋钮（可在 command 提交后、选定线程、try-catch 包裹下 emit）。这正是"中间层"方案的落地——而 `OnChanged` 式推送本质就是它，订阅只是更好用的外壳。

代价：需在 `TuneLab.Foundation`（契约层）冻结一个**最小订阅侧接口**：

```csharp
public interface IReadOnlyNotifiableProperty<out T>
{
    T Value { get; }
    event Action? WillModify;   // 改前触发：handler 内读 Value 得旧值（用于作废"被腾空的旧区域"）
    event Action? Modified;       // 改后触发
}
```

`WhenAny` 作为该接口（及其集合）的**扩展方法定义在 TuneLab.Foundation（契约层），逻辑一份**；宿主 Hosting.Foundation 的富 NotifiableProperty 实现此接口，host 与插件共用同一份 `WhenAny`，不存在两份实现漂移。

### 3.3 `IVoiceSynthesisNote` / 时间真值域 / `ITiming`

宿主业务层的 `INote` **不暴露**；SDK 另立 `IVoiceSynthesisNote`，字段皆为可订阅属性。**固定字段保持最小**（通用乐理属性），voice 专属的 per-note 参数一律走 `Properties`（keyed）——加新参数 = 加 `NoteProperties` 的 key，不动 `IVoiceSynthesisNote` 固定面。

```csharp
public interface IVoiceSynthesisNote
{
    IReadOnlyNotifiableProperty<double> StartTime { get; }   // 全局秒（tempo 派生，变化经 Modified 通知）
    IReadOnlyNotifiableProperty<double> EndTime   { get; }
    IReadOnlyNotifiableProperty<int>      Pitch  { get; }
    IReadOnlyNotifiableProperty<string>   Lyric  { get; }
    IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> Phonemes { get; }  // 活音素几何列表，见 §6（整列表换引用即通知；活视图侧不带属性，属性合成时经快照读）
    PropertyObject Properties { get; }   // 可订阅；voice 专属 per-note 参数都在这

    // 邻居链保留（协同发音方便）。注意：合成须在快照上沿链导航，见 §3.5。
    IVoiceSynthesisNote? Next { get; }
    IVoiceSynthesisNote? Last { get; }
}
```

**插件侧全秒轴原则：插件面对的所有时间量统一为全局秒，tick 只是宿主乐谱内部表示、不外露。**合成是声学域作业（音频按秒/采样点），插件需要的永远是"第 X 秒"；note 边界、曲线查询点、开窗区间、`RangeModified` 区间一律秒。秒由 tempo 表换算而来——精度上 double 秒在工程规模下远超采样点（比 48kHz 采样间隔精确约 7 个数量级），对合成无损；tick 的整数精确性价值在编辑域（网格对齐、定点比较），合成域用不到。插件因此**不碰任何 tick↔秒换算**：宿主在 note 边界派生、求值器边界、快照物化处完成换算，`IVoiceSynthesisContext` 与 `VoiceSynthesisSnapshot` 都**不暴露** `ITiming`。

tick↔秒换算仍由宿主内部的 `ITiming`（`LiveTiming` 活实现 / `TempoSnapshot` 冻结实现）承担——`ITiming` 接口与其实现家族同居宿主 `TuneLab.Data.Timing`，**不在插件 SDK 面**（既不进 context/snapshot，类型本身也已从 `TuneLab.SDK` 移除）。

**tempo 变化无独立信号，也无增量分解通知**（删去了曾经的 `TimingModified`；曾设想的"分解为边界 `Modified` + 轨全区间 `RangeModified`"方案已否决——tempo 改动影响面是整个 part，逐 note/逐轨扇出通知即全量风暴，不如直接换新）：时基变更（tempo 表 / part 平移）→ 宿主**整体重建会话**（旧会话 `Dispose`、新 context 新会话，接线在 `MidiPart.OnTimebaseModified`、调度轮内合并触发），新会话读到的即新秒值。插件无需处理"时基变了"：正确实现 `Dispose`（退订 + 释放音频段）即天然正确。

**命名约定**：插件面只有 `Time`/`Second`（单位秒）。**单复数表元数**：单个时间点用单数（`StartTime`/`EndTime`），批量用复数。新 SDK 杜绝歧义的 `Pos`。宿主内部 tick 量（`Pos`、`TempoMark.Tick` 等）的 `Pos → Tick` 重命名是好清理但量大、可能触及 Format 序列化 ABI，作为**独立 refactor**、本设计不捆绑。

### 3.4 变更通知：过滤 + 批量括号 + 改前事件

- **`canIgnore` 过滤**：宿主转发数据层通知时，只转发 `canIgnore == false`（已提交的真实变更），丢弃中间态（如拖拽过程）。中间态合并由业务层 merge 负责。中间层因此只是**薄过滤器**，不持缓冲。
- **批量括号 `BatchBegin` / `BatchEnd`**：用于让插件**延迟昂贵的状态修正**。每个逻辑编辑（一个 command，含单条编辑）都包在括号里：插件在每条变更通知里**廉价记录**，在 `BatchEnd` 一次性做重活（如重分片）。括号不是宿主缓冲，是让*插件*决定延迟的作用域信号。批量编辑（如移调 500 个 note）因此只重分片一次。
- **`WillModify` 改前事件**：作废"被腾空的旧区域"必需。note 从 A 移到 B，`Modified` 只给 B（当前值），`WillModify` 给 A（旧值），插件据此把 A、B 两段都作废。**merge 语义与 `Modified` 对偶**：作用域内首次 canIgnore=false 必达（订阅者在此抓旧值），其余 canIgnore=true 可忽略——`Modified` 折叠掉的中间态，其"改前旧值"同样无需作废（订阅者眼中状态从作用域前直达收口后）；收口时重置。最小订阅面（`event Action?`）只收必达的首次，与只收结果态的 `Modified` 恰好成对。
- **集合级增/删**：不是独立机制——`WhenAny(Notes, …)` 本身覆盖集合成员变化（新 note 自动纳入订阅），纯增删触发 WhenAny；插件在 `BatchEnd` 重读 `Notes` 重分片即可。

### 3.5 隔离模型与合成快照（线程，及未来进程）

合成跑在 worker 线程、宿主改数据在 UI 线程；且**引擎未来可能整体移入独立进程**（插件崩溃不连累宿主）。线程隔离（现在）与进程隔离（将来）用**同一个原语**解决——**dump / 不可变快照**——故本设计一套到底，v2 只换传输（线程交接 → 序列化 + IPC），不重设计。

**定调：数据层维持 UI 线程单写、不加锁、不做 COW；隔离靠快照、不靠同步。** worker（将来 worker 进程）**永不碰活对象**，只读一份在 UI 线程捕获的不可变快照。

#### 两个视图

- **活视图**（context，UI 线程，可订阅）：仅用于"有变化 → 重排"。插件订阅回调、`GetNextSegment`、重分片都在这条数据线程上跑，可 live 全量访问宿主数据。
- **合成快照**（worker 线程 / worker 进程，不可变）：派发时捕获、对快照合成（含沿邻居链导航）。

#### 捕获时机与线程（快照由插件主动拉取）

- **`GetNextSegment`**：数据线程上的**廉价 peek**，live 全量访问——插件基于完整 part 做分片决策，只报出纯值秒边界（`SynthesisRange` struct，纯调度提示）；peek 常被多会话 speculative 地叫、多数不中选，不做任何捕获。
- **`SynthesizeNext` 同步前缀**：仍在数据线程，入参是选中它的那次 peek 的**同一窗口** `(startTime, endTime)`（而非把 `SynthesisRange` 回灌），插件按同一窗口**重算分块**（确定性分片 + peek→commit 同调度 tick 无编辑 ⇒ 与 peek 同结果），随后经 **`context.GetSnapshot(notes, startTick, endTick)` 主动拉取**所需快照——notes 与开窗区间由插件按本次合成需要自由圈定，一次合成可按需拉多份（如音素级小窗 + 音频级大窗）；**之后**才 offload 到 worker（进程内）/ 序列化送进程（v2）。
- 拉取式替代早期"segment 携带捕获声明、宿主代为物化递入"的形态：声明本就是插件需求的间接表达，直接调用消除一层间接；物化/版本缓存/记账仍收在宿主的 GetSnapshot 实现内，`GetSnapshot` 入口带数据线程断言（§3.2 防线 ②）兜住"offload 后才拉"的违例。

#### 快照构成（非对称：小而必须的送、大而要算法的留宿主侧）

| 数据 | 形态 | 进程内 | 跨进程（v2） |
|---|---|---|---|
| **note** | eager 物化的不可变值快照（`StartTime`/`EndTime`=有效末·单声部音频口径·全局秒，宿主独占音素布局不暴露满末、`Pitch`、`Lyric`、`VoiceSynthesisPhonemeSnapshot[]`=几何描述符（`SynthesizedPhoneme`：时长 / 权重 / IsLead）+ per-phoneme 属性值拷、`Properties` 值拷；有序列表与递入 notes 索引对齐，邻居按索引导航） | worker 直读 | 序列化进消息体送过去 |
| **automation** | **host 侧不可变原始点快照**；插件经 `IAutomationEvaluator.Evaluate(points)` 拉采样值 | worker 直接调求值器、宿主插值算法就地对冻结点插值 | 快照序列化时物化为离散点（提前采样，跨进程牺牲项；细节缓后）；**插值算法恒在宿主侧** |
| **timing** | `ITiming` 接口接缝（接口与实现 `TempoSnapshot` 同居宿主 `TuneLab.Data.Timing`，与 live 同一套共享算法；不在插件 SDK 面） | worker 直接调冻结实现 | 快照序列化时物化为离散数据（细节缓后） |

**冻结形态 = 原始锚点 + 按需插值（而非"冻结时传点算好返回值"）**：查询点常是合成的中间产物（音素定时后才知道在哪采参数），快照时刻预知不了，预算值形态会逼插件超采或放弃精确采样；且锚点形态**严格包含**预算值用法——想"冻结时算好"的插件在同步前缀调 getter 把值采成 `double[]` 自存即可（**推荐模式**：前缀预采则 v2 下 worker 零 RPC，worker 内调用仅留给依赖中间产物的动态点）。锚点也比密集采样小 1–2 个量级。快照上 automation 以可枚举 `Automations` Map 平铺（纯数据体，非查询方法）。

automation **开窗**只取该段区间的原始锚点，不是整条曲线整 part 拷。**无变形开窗规则**：取闭区间 `[start, end]` 内的锚点 + 每侧**开区间之外**最近的至多两个锚点（边界恰为锚点时其自身计入）。两个是单调 Hermite 的斜率影响半径——查询所落段两端锚点的斜率各依赖再向外一个锚点，外扩两个恰好补齐，窗口内取值与全曲线**逐点全等**；查询恰落在锚点上时不依赖斜率，故压边界一侧只需外扩一个。注意不能用"边界处采样烘焙端点值"的取段方式（如编辑用的 `RangeInfo`），那会改变边缘段的插值形状。

#### `IAutomationEvaluator.Evaluate(points)` 是传输无关接缝

接口 `double[] Evaluate(IReadOnlyList<double> points)`：插件递一列时间点、拿回一列值。背后实现按 transport 换——进程内直读冻结点插值；跨进程（基于 v1 接口做，本就有牺牲项）在快照序列化时把求值器物化为离散点（细节缓后）。插值算法永远单一权威地留在宿主侧，插件不需要懂插值。

#### 安全发布与唯一纪律

- 快照**不可变、只写一次**（构造，单线程）；构造 happens-before worker 启动（task 派发 / 序列化送出提供内存屏障）。此后只读。**宿主从不修改一份已发布的快照**——数据变了走活视图 → 插件标脏 → 下次 `GetNextSegment` 出新段 → 捕获**一份全新快照**。**替换，而非同步**，故无共享可变状态、无需锁。
- **唯一纪律**：深拷必须**触底到值类型**，快照里不许漏进任何 live `NotifiableProperty`/`DataObject` 引用。用纯 record（物理上拿不到事件接线）从类型上杜绝；跨进程下更被双重强制——漏了根本序列化不过去。

#### 为何不加锁、不做 COW

- **不加锁**：合成的读 profile（长读、密集采样）与编辑的写 profile（高频、须保持 UI 响应）天生对立——长持读锁 → 整个合成期 UI 冻结；频繁取放 → 读到不一致 → 仍得锁内拷一份（= 快照 + 锁）。且变更事件沿父链**同步 inline 级联**触发（`NotifiableProperty.Value` set 即触发、`DataObject.Notify` 上爬祖先），加锁 = 把任意订阅者级联拖进持锁区 → 重入 / 锁序死锁雷区。并且锁是进程内的，对进程拆分**零前向价值**。
- **不做 COW**：数据层是 `DataObject`（带 undo/通知接线），非裸不可变 list，无法 O(1) 抓引用；要 COW 得先把存储改成裸不可变 list、丢掉编辑能力，且**编辑是高频**，COW 让热编辑路径变 O(n)（每次改重建数组）= 负优化。跨进程又**无论如何要 O(n) 序列化**，COW 的 O(1) 抓引用带不过进程边界。故 eager 物化。

#### 成本与宿主控制

- **峰值占用 ≈ 并发上限 × 单段快照**，**短命**（合成完即回收），不随工程大小累积。
- **UI 线程捕获开销小**：automation 是 bulk struct 拷（几万点 ~亚毫秒-1ms）、note 逐项值拷（几百 note ~几 ms）；仅"引擎要整 part + 超密曲线"的退化情形接近掉一两帧，且被版本缓存 gate、其后紧跟数秒合成、按段节奏发生（非逐帧）。
- **宿主握三个旋钮定聚合负担**（皆不依赖插件配合）：①**派发时机/限速**（dump 只发生在宿主排定的派发点，可挑空闲帧、可限速）；②**版本缓存**——automation 切片按"曲线版本 + 区间"缓存不可变副本，仅 `RangeModified` 命中窗口才重拷（甚至做 delta 只送改动区间），未改的重派零拷贝；③**并发上限**（账本式）封顶同时存在的快照数。per-segment 的"量"由插件按合成正确性需求定（给少 = 算错，不该由宿主压）。

#### 前向兼容（线程隔离 → 进程隔离）

| 设计件 | v1 进程内 | v2 跨进程 |
|---|---|---|
| note 快照 | worker 读的不可变值树 | 直接当序列化消息体 |
| automation | worker 直读冻结点 + 宿主插值 | 快照序列化时物化为离散点（细节缓后） |
| `SynthesisRange`（纯值 struct） | 两个 double | 两个 double 直接过线 |
| `GetSnapshot` | 数据线程同步物化 | 插件进程发起的一次批量 RPC（快照即返回体，一次过线） |
| context 订阅 | C# event，宿主在 UI 线程 emit | 宿主 emit 转 marshaled 消息（中间层本就宿主控 emit） |
| 音频产物 `ReadAudio` | pull 拷贝 | pull 自共享内存 |
| **崩溃** | （线程崩拖垮宿主） | IPC 失败 → 段标 `Failed` → 重排；宿主只持副本/答查询，**数据无损** |

**现在要 bake 进实现**（让 v2 是"换传输"而非"重设计"）：快照构造成自包含、可序列化的值树；曲线点用 blittable 的 `Point`（将来可直接 memcpy 进共享缓冲、近零序列化）。**现在不做**：真正的 IPC 传输层 / 共享内存 / 进程拆分（v2 的事，此处只是别画死路）。

---

## 4. 调度（宿主驱动逐步合成）

宿主掌握全局视图（播放线 + 所有 part 位置），故由宿主驱动"逐步合成"；插件只在被调用时干活、干完即停等下一次。模型仿 ACE 的 `findNextNeedSynthesisContext`。

- **一个会话同时只合成一块**；并行发生在**不同 part 的不同会话**之间。`GetNextSegment` 只在会话空闲时被问。
- **并发上限**由宿主设置（账本式，可运行时改：调大则填满空槽、调小则停派新的等 drain，必要时发 token 抢占）。

```csharp
public interface IVoiceSynthesisSession
{
    // —— 调度 ——
    // peek：窗内"下一块待合成"的纯值边界，无副作用
    SynthesisRange? GetNextSegment(double startTime, double endTime);

    // commit：入参为选中它的那次 peek 的【同一窗口】（而非把 GetNextSegment 自报的 SynthesisRange 回灌）——
    // 插件按同一窗口确定性重导出同一块、经 context.GetSnapshot 拉取所需快照后 offload；
    // await 返回 = 槽位释放、宿主重排。进度不在此传入——经状态带 + StatusChanged 上报。
    Task SynthesizeNext(double startTime, double endTime,
                        CancellationToken cancellation = default);

    // ... 声明 / 产物 / 状态见下 ...
    void Dispose();
}

// GetNextSegment 的返回：插件报给宿主的"下一块大致区间"纯值边界（readonly struct），宿主只用它排播放线
// 就近优先级。不精确、不承载 notelist——精确 notelist 由插件在 SynthesizeNext 里按同一窗口确定性重导出
// （或 peek 时自缓存于会话字段），故它不入 SynthesizeNext 入参。命名改自旧 SynthesisSegment（"段"已被
// IAudioSegment / SynthesisStatusSegment 占用，避免三义）。
public readonly struct SynthesisRange(double startTime, double endTime)
{
    public double StartTime { get; }   // 秒，与产物同一时间系
    public double EndTime { get; }
}

// 宿主物化的不可变快照（context.GetSnapshot 的返回体）：纯数据体故为具体类型（§0 原则 5），
// 无参构造 + required init（初始化后不可变，加字段纯加性）。形状与活视图镜像对称。
// 物化/版本缓存/限速/并发记账全留宿主一处；v2 跨进程时它就是 GetSnapshot 一次批量 RPC 的返回体。
public sealed class VoiceSynthesisSnapshot
{
    public required IReadOnlyList<VoiceSynthesisNoteSnapshot> Notes { get; init; }   // 与递入 notes 索引对齐（邻居按索引导航）
    public required ITiming Timing { get; init; }    // 接口接缝：实现在宿主侧（与 live 共享算法），SDK 不带实现
    public required SynthesisAutomationSnapshot Pitch { get; init; }          // 可扩展容器（裹全局秒轴求值器 Evaluator），开窗 = 拉取区间；双通道语义同活视图
    public required IAutomationEvaluator PitchDeviation { get; init; }
    public required IReadOnlyMap<string, IAutomationEvaluator> Automations { get; init; }   // 全部已声明轨（可枚举 Map）
    public required PropertyObject PartProperties { get; init; }                // 值拷
}
```

**快照 note 不带邻居链**（接口最小化）：`Notes` 有序列表与 `GetSnapshot` 递入的 notes 索引对齐已含全部邻接信息，协同发音按索引取邻居即可。活视图 `IVoiceSynthesisNote` 的 `Next/Last` 保留——事件 handler 内只有 note 自身引用、无列表索引上下文，O(1) 邻居导航是分片决策的真实便利。

**peek→commit 原子性**：两者在同一调度 tick 内、同在数据线程同步衔接，期间无编辑可插入——commit 时插件重算分块（确定性分片）必得 peek 报出的同一块；`GetSnapshot` 默认把全部已声明轨按区间开窗物化（bulk 拷亚毫秒级），将来有压力再加可选 keys 白名单，不动接口面。原"半透明 token + downcast 取私货 + 不跨 tick 缓存"一组约定随 segment 纯值化整体消失；原 `IVoiceSource.Segment<T>` 外露分片函数取消（分片内化进会话）。

**`SynthesizeNext` 语义**：
- 返回纯 `Task`、无 outcome 枚举——状态全托管插件，宿主不关心"为何完成"（真完成/被取消/失败都一样），完成即重排、靠 `GetStatus` 看错误、靠 `GetNextSegment` 看是否还有待合成。done/pending/errored 三种处置在插件内部。
- **槽位在 `await` 真正返回时才释放、不在"请求取消"时**：取消是尽力请求，不可中止的插件把这块跑完才返回——资源始终封顶在并发上限内，不会出现"取消了还在偷耗资源、宿主却以为空了又派新的"。
- 取消正常返回（不抛 `OperationCanceledException`）：取消是正常调度结局，抛异常会逼每个 await 套 try-catch。

---

## 5. 产物与状态

更新靠**单一信号** `StatusChanged`，宿主收到直接刷新；状态段（`GetStatus`）充当 UI 状态带，曲线类产物 pull 读取。**音频产物走另一条通道**——插件向宿主申请的**音频段握柄** `IAudioSegment`。

为何音频单独走段握柄而非 `ReadAudio` 扁平 pull：下游 effect 链（离线整段模型，如 SVC 换声，整段重过很贵）要能**按段增量重渲染**——voice 改了哪段，只有那段重新过 effect 链，而不是 voice 产物一变就整 part 重跑。把 voice 本就内部持有的分片，提升成宿主持有的一等握柄，段即 effect 的失效/重渲染单元。

```csharp
public interface IVoiceSynthesisSession   // 续
{
    // —— 音频采样率（插件 native 率；音频本体经 IAudioSegment 握柄交付，不再 ReadAudio pull）——
    // 工程率是唯一真值，宿主比对：相等直读、不等套一层流式重采样（集中宿主一处，会话与工程率变化解耦）。
    int SampleRate { get; }

    // —— 曲线类产物 ——
    SynthesizedPitch SynthesizedPitch { get; }                                          // 具名富类型 { Segments }，见 §6
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }           // 富类型，与 effect 同形
    IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>> SynthesizedPhonemes { get; } // 按归属 note 键，每 note 一组 VoicePhoneme（只报几何），见 §6

    // —— 状态 / 按段报错（UI 状态带，与音频段解耦）——
    IReadOnlyList<SynthesisStatusSegment> GetStatus();
    IActionEvent StatusChanged { get; }   // 单一刷新信号
}

// 音频段握柄：宿主实现、经 context.CreateAudioSegment(offset, count) 分配，插件持有并写入。
// 它是音频产物的承载单元，也是下游 effect 链的失效/重渲染单元——一个段 Commit 即作为整体送 effect
// 重过（effect 缓存按握柄身份键：段重 Commit → 该段链重跑；段销毁 → 丢该段缓存）。
// 起始与长度创建时固定（宿主一次性分配缓冲，插件就地写故渐进合成不累积重拷）；位置/长度需变 → 删旧建新。
// 时间对齐协议：全局 0 时刻 = 采样点 0；缓冲按 native 采样率从段起始铺。
// 线程：写入 / 提交 / 释放全在数据线程（worker 渲染完，在 marshal 回数据线程的续延里写）。
public interface IAudioSegment : IDisposable   // Dispose() = 删除该段（重分片 / 改长度或位置时重建）
{
    // 段内 [offset, offset+samples.Length) 就地写入（offset = 段内相对采样位置）；宿主拷进自有缓冲——
    // span 借用语义，返回后插件可随意复用 / 池化。越界非法；可多次写 / 覆盖重写 / Commit 后再写。
    // 写入区间即"该子区间已更新"信号（将来段内局部 effect 重渲据此；当前整段失效暂不消费区间）。
    void Write(int offset, ReadOnlySpan<float> samples);

    // 标该段音频已固定——送 effect 的唯一闸门。Commit 前的写入只供进度/波形，冻结数据才进 effect，
    // 故合成爆发期不会拖着昂贵 effect 频繁重合成（闸门在协议层，非宿主防抖）。
    void Commit();
}

public enum SynthesisSegmentStatus { Pending, Synthesizing, Synthesized, Failed }

public struct SynthesisStatusSegment
{
    public double StartTime;    // 秒，与音频产物同一时间系
    public double EndTime;
    public SynthesisSegmentStatus Status;
    public string? Message;     // 状态文案：Failed=错误信息；Synthesizing=可选管线阶段（如"正在合成音高"），宿主原样展示
    public double Progress;     // 合成中该段进度 [0,1]；不报进度的插件保持 0（将来需区分"无进度"加 bool HasProgress，纯加性）
}
```

**音频段与状态段解耦**：`IAudioSegment` 是音频承载 + effect 失效单元，`SynthesisStatusSegment` 是 UI 状态带（着色 / 进度 / 失败提示）。两套分区可以不同——插件内部是否用一个对象同时背两套，是插件的自由，宿主**不假设对齐、不干涉**。状态带把"已完成 / 合成中 / 失败 / 待合成"统一成一条时间线，宿主据 `范围 + Status` 着色、显示进度、在失败段显示错误。范围**平铺为两个 double、不引入冻结的区间类型**（裸名 `Range` 与 `System.Range` 歧义，且区间运算是宿主侧能力）：宿主将来需要区间合并 / 相交等运算时在宿主内部封装（如对照 ACE `Base/Utils/Math/RangeF` 的泛化区间），不进 SDK 冻结面、随时可改名。

**effect 按段重渲染（宿主侧）**：宿主二维失效 `cache[segment][stage]`——voice 段 Commit → 丢该段全部级缓存、从 stage 0 重过；effect[i] 变化 → 各段从 i 级重过；链尾 = 各段末级输出按时间拼接。段边界由 voice 自己挑（理想落在停顿处），跨段连续性归 voice 分片，宿主直接拼。波形按段逐段绘制。legacy 插件经 compat adapter 建单段（覆盖整 part），= 整段行为、零变化。

---

## 6. 音素

音素**几何核** `SynthesizedPhoneme`（`Symbol + 标称时长 Duration + 弹性权重 StretchWeight + 前置标记 IsLead`，**不报绝对位置**）是输入 / 输出 / 布局三方共享的稳定形状。定位 / 跨 note 去重叠压缩 / melisma 铺设 / 留白门控全由**宿主**按同一时长模型派生、独占布局。

```csharp
// 音素描述符（方向无关，输入 / 输出共用一个类型）：只报几何（标称时长 + 弹性权重 + 前置标记），**不报绝对位置**。
// 进（用户钉死约束，挂 IVoiceSynthesisNote.Phonemes）与出（引擎产物 IVoiceSynthesisSession.SynthesizedPhonemes）同形，
// 故合并为一个方向无关类型。同时是布局纯函数 PhonemeLayout.Resolve 的载体（布局只读几何、方向无关）。
public struct SynthesizedPhoneme
{
    public string Symbol;
    public double Duration;        // 标称时长（秒）：辅音(w=0)固定长；核(w>0)此值被布局忽略（恒按填充派生）
    public double StretchWeight;   // 弹性权重：0=刚性辅音 / >0=可伸核·元音（吸收 note 伸缩、按权重先让）
    public bool   IsLead;          // 前置音素（音节核之前的引导辅音）：决定摆放（前置往左累积、核填充）
}
```

**`SynthesizedPhoneme` 维持单一方向无关类型（不拆三角色）**：音素的"几何"进出对称，故输入约束与输出产物共用一个 `SynthesizedPhoneme`，布局纯函数也直接吃它。引入 per-phoneme 引擎属性（§6 末「音素属性」）后**不破这条对称**——属性不混进 `SynthesizedPhoneme`，而是只在**输入侧 / 钉死音素**上、随合成快照单独以 `VoiceSynthesisPhonemeSnapshot`（= 几何字段平铺 + `PropertyObject Properties`，见 §6 末）承载。这样几何契约保持瘦、布局只碰几何，而属性是 niche 的 pay-as-you-go 附加（绝大多数音素无属性）。

**为何报时长而非绝对位置（时长模型核心）**：引擎报已压缩的绝对位置会让宿主布局误判——「内容末 vs 后 note 核起点」的相接判据把"已压缩到核前"误判成"有空隙"而早返回、跳过压缩。故引擎只报自然时长，宿主独占布局；引擎自己的音频内部如何摆放与此显示契约解耦。

### 输入（host→engine，per note）

活视图挂在 `IVoiceSynthesisNote.Phonemes`（`IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>>`，整列表换引用即通知；活视图侧**只暴露几何**、不带属性——引擎在合成时经快照读属性，见 §6 末）；进合成快照时物化为 `VoiceSynthesisPhonemeSnapshot[]`（几何描述符 + 属性值拷）。

- **位置由布局派生、不存**：前置分界线（核起点）= 音符头；`IsLead` 音素从分界线往左累积固定时长（可任意加长、向 note 前越界）；核 + 后辅音往右——辅音用固定时长、核填充到组末（含 melisma 铺过乘客）、多核按权重分摊。便于「推挤式」编辑（改一个音素长度，相邻整体平移而非互相挤占）。
- **钉死粒度为整 note**：列表非空 = 全部音素用户钉死（约束，引擎遵守）；空列表 = 引擎从 `Lyric` 做 G2P + 全自由定时。不支持单音素级"部分钉死"。

### 布局算法（SDK 共享纯函数 `PhonemeLayout.Resolve`）

把「音素描述符（时长 / 权重 / IsLead）+ note 几何锚点（核起点 / 核填充终点）」解析为各音素跨 note 去重叠后的真实 `[Start, End]`，由 SDK 的确定性纯函数 `PhonemeLayout.Resolve` 统一完成。`Resolve` 只接管**定位 / 去重叠**这一半——标称时长生成（G2P / 分词分组 / dur 模型 / padding）仍是引擎专属、不被消掉。宿主显示侧与引擎调**同一份代码**（不是两份对齐）——故音频 == 显示（WYSIWYG）。**冻结的只是 I/O 形状（数据契约），压缩内部逻辑仍可宿主侧自由演进**；插件运行时绑定宿主进程里的这一份 SDK，故随宿主算法演进永不漂移。

**两种用途分清**：① **音频布局**（用 `Resolve` 输出驱动帧时序）——`FillEnd` 直接塑造音频，要 WYSIWYG 须与宿主同口径（自己末 + 仅延续乘客 melisma、空隙停在自己末），偏离则音频与显示分叉、**听得见**。② **纯显示对齐**（不驱动音频、只对齐音素线）——可调可不调，不调自由放置、错位非致命。**「错位非致命」这条 escape hatch 只对纯显示成立，对音频不成立。**

- **几何锚点由调用方算**：`PhonemeLayoutNote.FillStart` = 音符头；`FillEnd` = 调用方按自己的数据模型算的前向铺末（宿主走 continuation、引擎走延音符跳过，含 melisma 铺过相接乘客）——布局数学不掺和。元音自然铺到 `FillEnd`，布局再据真实邻居跨 note 去重叠。
- **去重叠语义（两阶分级，逐 note 边界相互独立）**：重叠只发生在 note 边界（同 note 内连续无隙）。每个边界吸收跨度 = `[前 note 核起点（固定左锚）… 后 note 核起点（固定右锚、核不压）)`——① 元音（w>0）**先让**（从尾收缩，最多到 0）；② 元音耗尽仍超 → 辅音簇（w=0，前 note 尾辅音 ∪ 后 note 前辅音）**按标称长度等比压**（V1 无最小地板、可压到 0）；③ 单调钳制兜底。**仅相接 / 重叠才跨 note 协同**；有空隙（前 note 内容末 < 后 note 核起点）时两 note 音素各自保持自然几何、互不推挤。
- **留白门控**：某侧有「相接、非乘客、却尚无音素数据」的邻居（正在合成 / 待合成）时，本 note 边界未决，宿主一并留白待邻居就绪，避免数据到达后跳变。
- **乘客（延续）= 引擎判定为延续的 note**：被前一音节元音铺过（melisma）、透明。**延续与否由引擎判定、宿主照单全收且判定优先级最高**——会话方法 `IVoiceSynthesisSession.IsContinuation(note)`——**必须实现、刻意无默认体**：判定与合成行为是一对绑定承诺，任何默认体都替实现的合成许诺它未必做到的语义（`"-"` 铺末默认对不做 melisma 的引擎撒谎、恒 false 默认掩盖做了 melisma 却忘实现的引擎），沉默继承即静默分叉；不做延音语义的引擎如实 `=> false`。参考语义（编辑器 `"-"` 约定；宿主自营零引擎 `EmptyVoiceSynthesisEngine` 以同一语义实现自己的判定，故无声源 part 也按此显示——判定无真空）：`"-"` 记号 ∧ 经不断裂相接链回溯到内容 note（严格比较）∧ 本 note 无钉死音素，孤儿 = false。音素布局的第一步就是延音判定：判定为延续的 note 其音素数据（钉死 / 回显）**根本不被读取**——宿主不叠加任何合取（原宿主链回溯已退役），编辑期同步求值并按 part 级数据变更缓存失效——显示骨架合成前即终态且恒稳定（硬 WYSIWYG），引擎的自定义记号自动获得布局与手势支持。判定语义**完全归引擎自有**（链 / 相接 / 记号，含"小间距视为相接"这类策略），SDK 刻意不提供判定助手——判定绑定合成行为，实现须完全吃透语义才敢用，黑盒积木结构性无用（完整参考实现见样例插件 V1.Voice，十行链回溯）。**绑定性**：判定为延续的 note 不得回传音素（区段发音全挂链头 note）；违约回传落账但被忽略不显示（兜底），音频与显示的分叉属引擎自身矛盾。**无回喂标志**：live/快照面都不再有 `IsContinuation` 字段（回喂只会是引擎自己的输出，冗余）；快照窗口可能裁掉链头，引擎要把身份带进 worker 就在 `SynthesizeNext` 同步前缀对 live note 自判、随自有快照冻结。**钉死音素是判定输入而非宿主强制条件**：默认语义选择钉死排除，自定义判定可自由决定钉死地位——宿主照单尊重（判定为延续的 note 显示透明，即使带钉死）。legacy compat 适配器判定恒 false（老模型无乘客机制，忠实降级）。设计推导全程见 `continuation-contract-draft.md`。

### 输出（engine→host，合成时返回）：按归属 note 键的 map

```csharp
IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>> SynthesizedPhonemes { get; }
```

- **按归属 note 键**（而非扁平时间线 + 出身字段）：描述符不报绝对位置，**无主音素无锚不可定位、也落不进 note 失效链，故砍掉「无主音素（Note=null）」契约**——`SynthesizedPhoneme` 不带 `Note` 字段，归属全由 map 键表达。辅音入侵上一 note 尾巴这类越界，由宿主派生位置时自然产生。（breath 等将来用「归属 note 的前置 / 后置音素」或专属事件通道承载。）
- **键怎么填**：用递给 `GetSnapshot` 的活 note 列表（`origins`）按快照索引对齐回取（`snapshot.Notes[i]` ↔ `origins[i]`）。键仅作身份 token，合成中不得读其属性。脏 / 合成中的块不在 map 里报告其 note 的音素（宿主据此留白）。
- **回填直拷**：宿主 `WriteBackPhonemes` 按键直接拷到对应 note（免归组）。
- **null vs 空（语义区分，宿主回填面）**：宿主 note 上的 `SynthesizedPhonemes` 字段——`null` = **该 note 未参与合成**（未决、留白）；`[]`（空非 null）= **已合成、该 note 确无音素**（终态）。两者本质不同（前者待定、后者已定），回填时按此分流（`WriteBackPhonemes`：参与合成的 note 即使无音素也写 `[]`，仅未参与者写 `null`），下游显示 / 触发逻辑可据此区分"还没出"与"出了就是空"。

### 伸缩、锁定与 preview

音素如何随音符长度伸缩 / 去重叠是引擎的音韵学知识（元音吸收伸缩、各引擎自有比例），宿主没有元音/辅音概念。解法是把知识编码进每音素一个 `StretchWeight` 数字 + `IsLead` 前后标记，宿主据它跑确定性的乘法 / 等比布局（缩放比 `len/d = r^w`）：

- **核时长是基准比例**：核（w>0）的 `Duration` 是原长——单核时被抵消（恒填满核空间）、多核时定彼此基准比例（各乘 `r^w`）；辅音（w=0）的 `Duration` 即固定长。引擎无需自己摆位，只诚实报时长 + 权重 + 前后标记。
- **权重随锁定持久化进工程**：用户锁定音素时固定的是"几何 + 伸缩性质"整体——`StretchWeight` 随锁定存进 `PhonemeInfo.StretchWeight` / 数据层 `IPhoneme.StretchWeight`。根除时序错位（工程加载后首轮合成前拖伸 note，伸缩有正确分布而非退化均匀）。`StretchWeight` 默认全填同一正值（如 `1`，等比缩放）；全 `w=0` 退化为按原长整体等比，无除零。
- **锁定零跳变**：锁定 = 宿主取 `{Duration, StretchWeight, IsLead}` 按「核起点 = 音符头」重新派生位置；显示侧 `PhonemeLayout` 按当前邻居重新分配（可伸音素按 `r^w` 重新缩放），与合成同源、常态不双重压缩，无需「反压缩」。
- **preview 纯显示、绝不反馈给引擎当约束**：权威时长由全量合成重新定时返回（带新权重），覆盖 preview。

### 音素属性（per-phoneme 自定义属性，引擎声明 + 宿主通用持有）

让 voice 引擎能给**音素**声明用户可编辑的自定义属性（与 note 的 `GetNotePropertyConfig` 平行），用于按音素设引擎特定值（如每音素 tension / 对齐微调 / 语言 tag / 音素级混合比例）。per-phoneme 的**引擎专属、宿主不解释**数据走 `PropertyObject` 属性袋——这正是 `PropertyObject` 在 note/part 上扮演的角色（宿主不懂语义、引擎声明 schema、宿主通用地存 / 撤销 / 渲染），故 phoneme 沿用同一机制，而非在几何描述符里塞 `Language` 这种宿主根本不关心的 typed 字段。

**当前进度**：**SDK 契约 + 数据 + 持久 + 快照管线 + 侧栏编辑面板**已落地——引擎可声明、可持久、合成可读到，用户可在侧栏逐音素编辑；仍待做的是音素的**选中模型**（详见末段）。

**SDK 声明面**：

```csharp
public interface IVoiceSynthesisEngine   // 续（声明面）
{
    // per-phoneme 自定义属性声明（required，与 GetNotePropertyConfig / GetPartPropertyConfig 一样必须实现）。
    // **复用 note 声明上下文 IVoiceSynthesisNotePropertyContext**（不再有独立 phoneme context）：每个
    // IVoiceSynthesisNoteView 现带 Phonemes（该 note 的有序音素）。返回与「选中各 note 的音素**扁平展开**」
    // **索引对齐**的 config 列表——扁平顺序 = context.Notes 顺序 × 各 note 的 Phonemes 顺序；
    // list[k] = 第 k 个扁平音素的 schema。schema 可依**音素在 note 内的位置（= 该 note Phonemes 索引）/
    // 邻居 / note 信息**条件化（如首辅音 vs 核 vs 尾辅音给不同控件）。
    // **返回空列表 = 所有音素均无属性**（不声明音素属性的引擎直接返回空列表）；否则长度须 = 扁平音素总数。
    // 一次调用即拿全部选中 note 的音素 schema，天然支持多选 note。
    IReadOnlyList<ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context);
}

// 音素只读值视图（声明面）：几何当前值 + per-phoneme 属性值快照（多选合并三态归插件）。
// 挂在 IVoiceSynthesisNoteView.Phonemes 上（见 §8 声明面值视图）。
public interface IVoiceSynthesisPhonemeView
{
    string Symbol { get; }
    double Duration { get; }          // 标称时长（秒）
    double StretchWeight { get; }
    bool   IsLead { get; }
    PropertyObject Properties { get; }
}
```

**合成快照载体**（`TuneLab.SDK/Voice/`，进合成快照供引擎读钉死值）：

```csharp
// 合成快照里一个钉死音素的冻结表项（VoiceSynthesisNoteSnapshot.Phonemes 的元素）。
// 几何字段平铺直读（喂 PhonemeLayout.Resolve 时按字段重建 SynthesizedPhoneme）；Properties 是该音素属性的冻结值。
// 数据线程物化、worker 只读的不可变值体（自包含、无 live 引用）。
public readonly struct VoiceSynthesisPhonemeSnapshot
{
    public string Symbol { get; }
    public double Duration { get; }
    public double StretchWeight { get; }
    public bool IsLead { get; }
    public PropertyObject Properties { get; }   // 未声明 / 未设 = PropertyObject.Empty
}
```

`VoiceSynthesisNoteSnapshot.Phonemes` 的元素类型即为 `VoiceSynthesisPhonemeSnapshot`（几何描述符 + 属性值拷），见 §3.5 / §4。

**语义要点**：

- **属性只存在于钉死音素上**（用户数据）；引擎 G2P 的自动音素、合成产物音素**无属性**。给音素设属性这一动作**隐含钉死该 note 的音素**（与既有"编辑音素即转用户数据、`Phonemes` 非空 = 整 note 钉死"一致）。
- **真相源在数据层实体**：宿主 `IPhoneme`（`: IDataObject<PhonemeInfo>`，原有 Duration/Symbol/StretchWeight/IsLead）**加 `DataPropertyObject Properties`**，与 `INote.Properties` 完全平行——身份 / 持久 / undo / merge 全由数据层既有机制承担。`VoiceSynthesisPhonemeSnapshot.Properties` / `IVoiceSynthesisPhonemeView.Properties` 是它的冻 / 读投影。
- **pay-as-you-go（轻量）**：数据层 `IPhoneme.Properties` 是 **lazy**——首次写才物化 `DataPropertyObject`，未编辑过的音素零开销；只读消费（快照 / 三态合并）走 `HasProperties` 闸门避免无谓物化；持久层 `PhonemeInfo.Properties` 空则为 `null`、不序列化。**空容器 ≡ 无属性**。
- **活视图侧不变**：`IVoiceSynthesisNote.Phonemes` 仍是几何列表（不带属性）——引擎在合成时经快照 `VoiceSynthesisPhonemeSnapshot.Properties` 读属性，活视图侧无须改面。
- **引擎声明 schema**：`GetPhonemePropertyConfigs` 复用 note 声明上下文 `IVoiceSynthesisNotePropertyContext`（每个 `IVoiceSynthesisNoteView` 带 `Phonemes`），返回与「选中各 note 的音素扁平展开」（顺序 = `context.Notes` × 各 note `Phonemes`）索引对齐的 schema 列表（条件式，可依音素在 note 内的位置 / 邻居 / note 信息 / part / voice 给不同控件，如对首辅音、核、尾辅音返回不同配置；空列表 = 所有音素无属性）；每个 phoneme 的 `Properties` 存值，宿主渲染时三态合并 + keyed-diff。phoneme 声明上下文与 note 声明上下文本就等价，故复用同一接口、不重复造类型。
- **曲线类不进属性**：per-phoneme 的音高 / 能量曲线本质是**时间轴参数**，走回显 / automation 通道（§7），不做成音素属性。
- **voice-only**：instrument 无音素系统，不涉及。
- **编辑 UI**：侧栏音素属性面板已落地——**逐音素一行**（符号标签 + 该音素自己的控制器），按选中 note 成批调 `GetPhonemePropertyConfigs` 求 config，三态合并 + keyed-diff 渲染。仍待做的是音素的**选中模型**（当前音素无 `ISelectable` 选中态）；在选中模型补齐前，宿主以选中 note 的全体音素为面板范围。

---

## 7. 自动化形态

两种形态，**一个扁平 config 类**——由 `DefaultValue` 是否 NaN 区分连续/分段（**已合并**；早期为两个独立类 `AutomationConfig` + `PiecewiseAutomationConfig`，见本节末"合并记"）：

```csharp
// 连续型：DefaultValue 为实数（处处有值 + 默认基线）；分段型：DefaultValue 为 NaN（段间空、无基线）。
public class AutomationConfig : IValueConfig<double>
{
    public string? DisplayText { get; init; }
    public required double DefaultValue { get; init; }   // NaN ⇒ 分段（无基线）
    public required double MinValue { get; init; }
    public required double MaxValue { get; init; }
    public required string Color { get; init; }
    public bool IsPiecewise => double.IsNaN(DefaultValue);
}
```

- **合并记**：连续/分段本是同一伞概念下的两形态，分段 = "无默认基线的 automation"。把"无基线"用 `DefaultValue = NaN` 表达（与本 SDK 既有 **NaN 表空**求值约定同源），二者收成一个类 + 一个 `GetAutomationConfigs` 方法（删去 `PiecewiseAutomationConfig` 与两处 `GetPiecewiseAutomationConfigs`）。收益：作者在**一张有序 map 里自由穿插**两种轨、声明序即呈现序；`GetAutomation*` 不分家。**宿主侧仍保留两种数据类型**（`IAutomation` / `IPiecewiseAutomation`）+ 两序列化槽，按 `IsPiecewise` 物化到对应 map、kind 在路由处现解析。`IValueConfig.DefaultValue` 的多态消费只在属性控件家族（与 automation 无关），故 automation 这边唯一需特判 NaN 的消费方是默认值侧栏 `AutomationDefaultsController`（分段轨不显默认基线行）。
- **不再 `AutomationConfig : SliderConfig`**：具体类继承在冻结 ABI 上是陷阱（SliderConfig 一迭代，AutomationConfig 被迫跟随；且"automation 是一种 slider"是 category error）。UI 复用 slider 控件是宿主侧渲染选择，读各自的 `MinValue/MaxValue(/DefaultValue)` 即可，不需类型继承。原则：**冻结面上解耦 > DRY**。
- **统一命名根 `Automation`**（不用业务词 `Expression`，也不用纯结构词 `Curve`）：求值器 `IAutomationEvaluator` 已把"Automation"锚定为"按时间求值"的伞概念，且 `IAutomation` 本就把曲线几何操作收在该名下。故 host 数据层 `IPiecewiseCurve / PiecewiseCurve` 改名为 `IPiecewiseAutomation / PiecewiseAutomation`，pitch 直接 typed 为 `IPiecewiseAutomation`，删 `PitchAutomation.cs` 空壳。（求值器原名 `IAutomationValueGetter.GetValue`，后正名 `IAutomationEvaluator.Evaluate`——"ValueGetter/Getter"非 .NET 惯用后缀，且 "sample" 一词在本 SDK 已被 PCM 音频占用。）
- **求值器统一、NaN 表空**：连续型与分段型共用 `IAutomationEvaluator.Evaluate(times) → double[]`（查询轴 = 全局秒）；连续型永不返回 NaN，分段型段间返回 NaN（IEEE 标准"非数"，DSP 惯用，且现有 pitch 求值与 ACE 都这么做）。

### 回显轨 ⇄ 可编辑轨：同 key 由宿主接管可编辑（零新 API）

回显轨（`GetSynthesizedParameterConfigs`，只读）与可编辑轨（`GetAutomationConfigs`）都由引擎声明、都按同一 `key` 命名空间（数据侧 `SynthesizedParameters` / `Automations` 同为 `string` 键）。**约定：引擎对同一 key 同时声明一条可编辑轨 + 一条回显轨 ⇒ 宿主识别为"模型建议 + 用户覆盖"同一参数，在视觉与编辑上叠加接管**：

- 回显作底（模型吐出的建议曲线），用户在可编辑轨上画即覆盖；引擎合成时本就读可编辑轨（§3.2），用户覆盖**自然回喂**，无需任何"提权"新接口。
- **覆盖语义（V1：整段二选一）**：用户画过的段用覆盖值、没画的段回退模型建议——具体怎么读归引擎（不进冻结面）。相对偏移 / 混合是将来的加性演进。
- **"编辑态 vs 自动"的可视化由此自然落地**：哪段实 / 哪段虚，宿主按"该 key 可编辑轨用户是否动过"自己决定渲染，**插件不报透明度**（曾有把逐段透明度 stop 塞进产物的提案，被否：透明度是纯渲染量、且用相对位置 0~1 违反全秒轴；插件只供语义数据，渲染策略归宿主）。

---

## 8. 声明数据与 Config 家族

- **目录元数据**在 `IVoiceSynthesisEngine.VoiceSourceInfos`（菜单/选择器用，无需会话）：

  ```csharp
  public struct VoiceSourceInfo
  {
      public string Name;
      public string Description;
      public ImageResource? Portrait;   // 可选立绘，显示在钢琴窗
  }
  ```
  `Portrait` 是格式无关的资源引用：封闭层次（构造器 private protected，变体仅 SDK 内新增），变体按数据形态分型（v1 仅 `FileImageResource` 路径变体——可指向图像文件或序列帧目录）、保持可序列化数据形态；动图（GIF/APNG）是宿主解码能力不进类型，Live2D/Spine 等富动态为独立特性。运行时会变的图像走目录变更信号（将来 `IVoiceSynthesisEngine` 加性事件），资源对象本身恒为不可变值。
  宿主渲染：钢琴窗按当前 part 音源声库解析 `Portrait`，把图（静态 / 动图当前帧）画在网格之后、音符之前（音符盖其上仍清晰）。**立绘优先于全局背景图**——当前声库有立绘则画立绘（背景图让位、且其动图定时器停表省 CPU），无立绘才画全局背景图。两者**同样靠右贴住、按高度填满钢琴窗等比缩放，并同套 `BackgroundImageOpacity` 不透明度**（仅来源与优先级不同，几何 / 透明度一致）。两者还**共用同一帧播放器**（`ImagePlayer`）：解码走 Skia（`SKCodec`），**静态图**（png/jpg/静态 webp…）= 单帧无定时器；**动图**（animated webp / gif / apng）= 多帧 + 逐帧时长，`DispatcherTimer` 按帧时长推进、控件挂上视觉树才播（不可见不空转）——故全局背景图也支持动图。仅解码指向**单个图像文件**的路径（立绘走 `FileImageResource` 路径变体）；序列帧目录 / 其余变体 / WebM 等视频容器走兜底（不显示）。换 part / 换引擎 / 换声库即重解析（按路径去重，不重复加载）。`instrument` 同理走 `InstrumentSourceInfo.Portrait`。

- **声明在引擎、不在会话**：声明（轨集合 / 属性面板 / 回显轨）是当前 part/note/phoneme 值的纯函数、不碰任何合成
  运行时状态，故全留 `IVoiceSynthesisEngine`（一处、规整）。**会话只保留 `DefaultLyric`**（创建后才取用的运行时值）。
  **声明全部 context 驱动、纯函数**：宿主在值 commit 时按当前值重算并 diff 到 UI——轨集合可随参数显隐（条件轨），
  属性面板可随值换控件/显隐。静态声明的插件忽略 context 返回固定 map/config 即可。**孤儿数据**：轨从声明消失后
  宿主保留其已画曲线（隐藏不删、不参与合成），参数回退使轨复现即原样恢复（数据层不裁剪）。

  **为何留引擎、不挪会话（根因）**：会话要在构造期订阅自己声明的自动化轨（`context.Automations`），
  该轨集是否"已填"取决于 `AutomationConfigs`。声明留在引擎（无状态、纯函数）后，宿主**建会话之前**——`CreateSession`
  的入参 `context` 已存在——就能据引擎求出声明并填好轨集合，构造期 `context.Automations` 已含你声明的轨（可点取 `TryGetValue` / 可枚举）。
  把声明传**活视图 context**（而非快照）也不破这条：context 先于会话存在（见 §3.1），无鸡生蛋。

  **取值面用只读 map `Automations`（而非 `TryGetAutomation(key)` 方法）**：①`AutomationConfig` 是动态的（part 当前值的纯函数），
  插件难预知有哪些 key，可枚举省得重跑声明逻辑去反推；②宿主侧 automation 本就可枚举，直接暴露零额外物化负担；
  ③跨进程枚举更优——宿主一次枚举物化整图送达，胜过反复回调；④快照里本就是一份 map，活视图与之对齐。

  **声明面收调用级只读值视图（已落地；voice/instrument 持平行副本、不抽公共类）**：原 `IVoiceSynthesisPartPropertyContext` 畸形——
  `PartProperties` 是列表（为多选 part）却 `VoiceId` 单数，多选不同声库时说不清是谁的。根因厘清后重设计：

  - **声明 context 是调用级（ephemeral），不是 part 级、更不是会话级**。GetConfig 是数据线程上一次性同步只读求值
    （读完即返、不留存不订阅），故宿主**每次求值前就地从数据层包一层当前值视图、调完即弃**——构造期 / 管线拆除后 /
    无会话时照样能包（数据层永远在）。生命周期顾虑与 part 级 context 重构因此都不存在。
  - **是值快照、不是会话活对象（关键，决定不复用 IVoiceContext）**：声明多选要做三态合并
    （`PropertyObjectExtensions.Merge`——逐 key 比对各成员**值快照**、不等给 `Multiple`），这是 `PropertyObject` 值操作；
    会话面 `IVoiceSynthesisContext.PartProperties` 是活外观 `IReadOnlyNotifiablePropertyObject`（导航式、无 `Merge`、无三态、无值快照形）。
    故声明面 `PartProperties` 取 **`PropertyObject` 值**、与会话活视图**不复用**。曾尝试让声明面复用 `IVoiceSynthesisContext`/`IVoiceSynthesisNote`
    （走继承基面），即因此被否——活属性喂不进 `Merge`。
  - **但比纯属性袋富**：值视图带 **note 非属性字段的当前值**（`IVoiceSynthesisNoteView` 的 StartTime/EndTime/Pitch/Lyric + note 集合 + 已声明自动化曲线），
    引擎可据此（而非仅属性）条件化 schema——这正是声明面自成一套类型、而非只传 `PropertyObject` 列表的理由。
  - **voice/instrument 平行副本、不抽公共类**（遵命名约定的"域专属 Voice*/Instrument* 前缀"、与会话面 `IVoiceSynthesisContext`/`IInstrumentSynthesisContext`
    一致）：两域声明 context 各成一套，便于独立演进——例如 instrument note **无 Lyric**（其无歌词系统），voice note 有。
    底层 part 数据相同，故**宿主一套实现同时满足两域**（covariance + 显式接口实现），是宿主内部复用、非 SDK 公共契约。
  - **音源 id 由 `IVoiceSynthesisPartView.VoiceId` / `IInstrumentSynthesisPartView.InstrumentId` 承载**（与"id 不进**合成** context"不冲突——那条约束会话级 context 生命周期，本视图调用级、无此顾虑）。
  - **命名（`*View` 后缀）**：声明只读值视图用 `*View`，与会话裸名活视图（`IVoiceSynthesisContext`/`IVoiceSynthesisNote`）、跨线程冻结 `*Snapshot` 三分清楚——裸名=活、`*View`=声明读值、`*Snapshot`=冻结跨线程。

  ```csharp
  // voice 声明面（TuneLab.SDK/Voice/）；instrument 持平行副本（IInstrumentSynthesisPartView{ InstrumentId; … }、IInstrumentPartNoteView 无 Lyric、两壳同形）
  public interface IVoiceSynthesisPartView
  {
      string VoiceId { get; }                                    // 该 part 选定声库
      IReadOnlyList<IVoiceSynthesisNoteView> Notes { get; }           // 当前 note 集合（原始几何，未去重叠/钳位）
      PropertyObject PartProperties { get; }                     // part 属性值快照
      IReadOnlyMap<string, IAutomationEvaluator> Automations { get; }  // 读已声明轨当前曲线（秒轴）；可点取 TryGetValue / 可枚举
  }
  public interface IVoiceSynthesisNoteView { double StartTime { get; } double EndTime { get; } int Pitch { get; } string Lyric { get; } PropertyObject Properties { get; } IReadOnlyList<IVoiceSynthesisPhonemeView> Phonemes { get; } }  // Phonemes = 该 note 的有序音素（前置辅音→核→后辅音；位置=索引），供 GetPhonemePropertyConfigs 用（见 §6）

  // 两个声明壳（抗迭代）——取代旧 IVoice/IInstrument × Part/Note 的快照形态
  public interface IVoiceSynthesisPartPropertyContext { IReadOnlyList<IVoiceSynthesisPartView> Parts { get; } }                                   // 多选 part
  public interface IVoiceSynthesisNotePropertyContext { IVoiceSynthesisPartView Part { get; }  IReadOnlyList<IVoiceSynthesisNoteView> Notes { get; } }  // 单 part 多 note

  public interface IVoiceSynthesisEngine   // 续（声明面）
  {
      // part 级（多选；属性面板有「默认值」列，多选 part 批量调轨默认值是真需求 ⇒ 轨声明也多 part）。
      IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context);
      IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context);
      ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context);
      // note / phoneme 级（单 part、多选其下成员）；VoiceId + part 当前值从 context.Part 取。
      ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context);
      // phoneme 级（required）：**复用 note 声明上下文**（每个 NoteView 带 Phonemes），返回与「选中各 note
      // 的音素扁平展开」（context.Notes × 各 note Phonemes）索引对齐的 config 列表（空列表=无属性；见 §6 音素属性）。
      IReadOnlyList<ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context);
  }
  // IInstrumentSynthesisEngine 收对应的 IInstrumentPart* 平行副本。

  public interface IVoiceSynthesisSession   // 续（仅余运行时取值）
  {
      string DefaultLyric { get; }
  }
  ```

  - **三态合并归插件**：壳里给活视图列表，插件自己遍历 `Parts`/`Notes` 做三态合并（`context.Parts.Select(p => p.PartProperties).Merge()`）。
  - **线程契约写死**：所有 `GetXxx` 恒在数据线程(=UI 线程)同步调用，插件只做一次性只读取值，**绝不订阅 / 不留存视图引用**。
  - **跨引擎多选 part**：多选不同引擎类型的 part 时，宿主**只显示 part 公共属性、不调用任何引擎 GetConfig**——故 GetConfig 永远只在同引擎下被调。本轮宿主各调用点恒传单 part（多 part 编辑面为后续）。
  - **会话面 `IVoiceSynthesisContext`/`IInstrumentSynthesisContext` 不动**：仍会话级、引擎专属，各加不可变 `VoiceId`/`InstrumentId`、`CreateSession` 删 id 参（见 §3.1/§3.2）。
  - **已落地**（阶段 A，release/2.0.0；三 sln Release 绿 + host 87 + compat 5 用例过）：新增 `IVoiceSynthesisPartView`/`IVoiceSynthesisNoteView`/`IVoiceSynthesisPartPropertyContext`/`IVoiceSynthesisNotePropertyContext` + instrument 平行副本（`IInstrumentSynthesisPartView`/`IInstrumentSynthesisNoteView`/两壳），删旧四 context + `SoundSource` 两适配类；宿主 `PartContext`/`PartNote`(TuneLab.Data) 一套实现同时满足两域、现包只读值视图。

- **扁平 config 原则推广**：整个 `IControllerConfig` 家族（SliderConfig/TextBox/CheckBox/ComboBox/ObjectConfig）审一遍，去掉为复用而做的具体类继承，各自自包含、只共享最小接口。（实现阶段执行）

---

## 9. 待办与缓后

**隔离/快照实现清单**（§3.5 已定调：数据层不加锁、不做 COW，靠不可变快照隔离）
- 不可变**原始点快照容器** + 在其上的不可变 `IAutomationEvaluator` 实现（`Evaluate(times)` 对冻结点插值，查询轴秒）。
- 抽一份**共享纯采样函数**：live `IAutomation`/`IPiecewiseCurve` 与上面的冻结求值器共用同一套"锚点 → 取值"算法（逻辑一份，杜绝两套实现漂移）。
- **note 快照值**（StartTime/EndTime、Pitch、Lyric、`VoiceSynthesisPhonemeSnapshot[]`、Properties 值拷；有序列表索引对齐、不带邻居链）；automation 按无变形开窗规则（见 §3.5）取原始锚点。
- **tempo 快照**（`ITiming` 的冻结实现；`ITiming` 接口与实现家族同居宿主 `TuneLab.Data.Timing`，不在插件 SDK 面）。
- 可选：automation 切片**版本缓存**（按"曲线版本 + 区间"缓存不可变副本，`RangeModified` 命中才作废/重拷）。
- 契约钉死：`GetNextSegment` = 数据线程上廉价 peek（live 全量、定分片，报纯值边界）；`SynthesizeNext` 同步前缀在数据线程重算分块、经 `context.GetSnapshot` 拉取快照，再 offload。
- 前向兼容进程拆分：快照构造为自包含可序列化值树、曲线点用 blittable `Point`；不做真正的 IPC/共享内存（v2）。

**实现阶段的清理项**
- `IControllerConfig` 家族扁平化审计（§8）。
- `ISynthesisData.GetAutomation` → `TryGetAutomation`，后再改为只读 map `Automations` 等命名对齐。
- ~~`SynthesizedPhoneme.ToString` 格式 bug~~（已随类型合并到 `SynthesizedPhoneme` 重写、修正）。
- DataObject 补 `WillModify` 事件（NotifiableProperty 统一的一环）。

**本轮定稿落地清单**（声明面对称化 + 音素 properties + 回显可编辑 + null/empty）

*阶段 A — 声明面对称化（地基，先行）* —— **已落地**（release/2.0.0；三 sln Release 绿 + host 87 + compat 5 用例过）
- ✅ `IVoiceSynthesisContext` 加 `string VoiceId`、`IInstrumentSynthesisContext` 加 `InstrumentId`；`CreateSession(IVoiceSynthesisContext/IInstrumentSynthesisContext)` 删 id 参（宿主创建 context 时填入）。
- ✅ 声明面收调用级只读值视图（`*View` 后缀），voice/instrument **平行副本**（不抽公共类）：voice 新增 `IVoiceSynthesisPartView`/`IVoiceSynthesisNoteView`/`IVoiceSynthesisPartPropertyContext`/`IVoiceSynthesisNotePropertyContext`（TuneLab.SDK/Voice/）+ instrument 平行副本（`IInstrumentSynthesisPartView`/`IInstrumentSynthesisNoteView`/两壳，note 无 Lyric）；删旧四 context 的快照形态 + `SoundSource` 两适配类。宿主一套实现（`PartContext`/`PartNote`）经 covariance 同时满足两域。（音素属性复用 note 声明上下文 + 在 `IVoiceSynthesisNoteView` 上挂 `Phonemes`/`IVoiceSynthesisPhonemeView`，见 §6。）
- ✅ 宿主侧 GetConfig 调用点改造：数据层 `PartContext`/`PartNote`（TuneLab.Data）现包只读活视图，不再造快照 DTO；`SoundSource`/`VoicesManager`/`InstrumentsManager`/`MidiPart`/`PropertySideBarContentProvider` 全部收新壳。
- ✅ 跨引擎多选 part：本轮各调用点恒传单 part（多 part 编辑面为后续）；跨引擎多选只显公共属性的拦截在多 part 面落地时补。
- ✅ 两 SynthesisContext 实现 `VoiceId`/`InstrumentId`；两 pipeline `CreateSession(context)`。
- ⏳ 未做（非阻塞）：GetConfig 入口的数据线程断言（DEBUG）。

*阶段 B — 音素 per-phoneme properties*（详见 §6 音素属性。`SynthesizedPhoneme` 维持单一方向无关类型，**不拆三角色**；属性经独立载体 `VoiceSynthesisPhonemeSnapshot` = 几何字段平铺 + `PropertyObject Properties` 承载）

*— 阶段一 SDK 契约 + 数据 + 持久 + 快照* —— **已落地**
- ✅ SDK：新增 `VoiceSynthesisPhonemeSnapshot`（`TuneLab.SDK/Voice/`）；`VoiceSynthesisNoteSnapshot.Phonemes` 元素类型从 `SynthesizedPhoneme` 改为 `VoiceSynthesisPhonemeSnapshot`（契约变更）。活视图 `IVoiceSynthesisNote.Phonemes` 仍为几何列表（不带属性）。
- ✅ SDK 声明面：`IVoiceSynthesisEngine.GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext) : IReadOnlyList<ObjectConfig>` **required**（复用 note 声明上下文，返回与「选中各 note 的音素扁平展开」索引对齐的 config 列表，空列表=所有音素无属性）；新增 `IVoiceSynthesisPhonemeView` 并在 `IVoiceSynthesisNoteView` 上挂 `Phonemes`（该 note 的有序音素序列，位置 = `Phonemes` 索引）——不再有独立 phoneme context。
- ✅ 数据层：`IPhoneme` 加 `DataPropertyObject Properties` + `HasProperties` 闸门（**lazy 物化**，未编辑零开销）；`PhonemeInfo` 加属性序列化槽（空 = `null`、不序列化）。
- ✅ 产物回填：`SynthesizedPhonemes` 区分 `null`（未参与合成）/`[]`（已合成无音素）——`WriteBackPhonemes` 按此分流。

*— 阶段二 编辑 UI*
- ✅ 侧栏音素属性面板已落地：**逐音素一行**（符号标签 + 该音素自己的控制器），按选中 note 成批调 `GetPhonemePropertyConfigs` 求 config，三态合并 + keyed-diff 渲染。
- ⏳ 待做：音素的"选中"模型（当前音素无 `ISelectable` 选中态）+ 挂波形 / 卷帘的音素直接编辑入口。在选中模型补齐前，宿主以选中 note 的全体音素为面板范围。

*阶段 C — 回显可编辑（约定，可独立）*
- 宿主约定：可编辑轨与回显轨同 key ⇒ 视觉叠加（回显作底、编辑覆盖）+ 编辑接管；覆盖段实、未覆盖段虚（宿主自决渲染，不依赖插件透明度）。引擎合成读可编辑轨即自然回喂。

**缓后/独立**
- 宿主全局 `Pos → Tick` 重命名（独立 refactor，注意 Format 序列化 ABI）。
- **定点 tick**（`Tick` 结构体：int64 存 1/2ⁿ tick 定点数）：全局 tick 域 double→Tick 的横切 refactor（数据层 + Format 序列化，可与 `Pos → Tick` 重命名同做）。收益是加减/比较**零误差**与跨进程确定性（`==` 恢复语义、结果与量级无关），**非音质**——double 在现实工程规模下误差比可感知量小 9 个数量级以上。关键约束：**n ≤ 16**，否则与 double 互转重新引入舍入（double 仅能精确表示 ≤2⁵³ 的整数，须 `pos < 2^(53−n)` tick）；秒域保持 double（其精度参照物是采样点，由采样率换算而来，不归 tick 管）。**决策时限：对外发布冻结 SDK 前定案**——若 `Tick` 进 SDK 冻结面则影响接口签名；若仅做宿主内部存储、SDK 边界转 double（n 取小则无损），则与 SDK 解耦、随时可做。
- **RESOLUTION 维持常量 480**（= 2⁵·3·5，二/三/五连音至 128 分音符整数落格；与 MIDI PPQ 同概念——SMF 按文件头可变、DAW 内部固定常量，导入按 `480/filePPQ` 缩放）：不做可调（每个 tick 数值失去自释含义、跨工程粘贴要换算、进 Format ABI，全是横切成本而收益约等于零）。若需更细网格，将来定点化时顺路一次性升高常量（如 960），那次本就要动数据层与 Format。
- **简易合成框架**（双 SDK）：把宿主式分片/调度做成插件侧库，简单插件复用、自定义插件走原生托管。本设计先做核心协议，框架降优先级；它同时可收编 legacy 引擎的薄模型适配。
- **音频段内子区间增量**（`IAudioSegment` 段内增量）：`Write(offset, samples)` 本就带"段内哪段变了"的区间（中间态仅驱动进度/波形、不进 effect），宿主累积这些区间随 `Commit` 交 effect，effect 自行决定段内局部重合成 + 拼接（含上下文余量 / 淡化、跨级脏传播）。V1 按整段失效（段 Commit 即整段送 effect、不消费写区间），子区间增量是纯加性优化、缓后。
- **effect 收敛到本会话模型**（当前 effect 为「宿主厚 / 插件薄」的逐段处理器模型：状态/调度/`cache[segment][stage]` 失效图全在宿主）。**设计已铺细 → §10**；落地分阶段见 §10.8。已锁的收敛决策（5 条，§10 逐条展开）：① 平行接口 `IEffectSession`（不与 `IVoiceSynthesisSession` 合并基类）；② `IEffectContext` 对偶 `IVoiceSynthesisContext`（暴露上游音频段活视图 + 自身参数/自动化 + GetSnapshot + 产出段，宿主接线、effect 对链结构无感）；③ 调度统一、链为单位（一个段区间自上而下跑完一遍 = 一个调度单位，按播放线就近挑；effect-only 改动退化为从脏的那一级往下）——voice 非音频产物（pitch/phoneme）不依赖 effect、eager 暴露不被串行；此为宿主调度策略、不进冻结面；④ `IEffectChange` 退役，effect 订阅 context 自算 dirty（失效图搬进插件）；⑤ `SynthesizedParameters` 借此换富类型（见下）。重构时一并处理：
  - ~~`IAutomationEvaluator` 与 `ISynthesisAutomation` 的合并/归属再审~~（已决：维持继承——is-a 成立，同一份采样例程同型吃活/冻两面；接口轴无关、轴由暴露面规定）。
  - ~~`SynthesizedParameters` 的双重 `IReadOnlyList<Point>` 实为 piecewise 结构，届时考虑引入富类型~~（**已实现**，见 §10.6：换形为 `SynthesizedParameter`，voice/effect 两侧同形）。
  - ~~`IPropertyContext` 从 SDK.Voice 挪 SDK.Base（effect 条件面板复用）~~（已随 SDK 程序集合并消解：voice/effect 同居 `TuneLab.SDK` 顶层命名空间，effect 可直接复用）。
- ~~**动态声明面**：轨集合/属性声明运行中变化 + 既有轨用户数据的归宿~~（**已实现**：声明全部 context 驱动、纯函数，
  宿主在参数 commit 时按当前值重算并 diff——轨集合随参数显隐（`GetAutomationConfigs`/`GetPiecewiseAutomationConfigs`
  收 `IVoiceSynthesisPartPropertyContext`），属性面板同 `GetPartPropertyConfig`/`GetNotePropertyConfig`；effect 侧 `IEffectEngine.GetPropertyConfig`/`GetAutomationConfigs` 同构（各收 effect 专属 `IEffectPropertyContext`——voice/effect context 分开以备 effect 将来追加 part 级官方字段而发散；effect 单层故用不带 Part 的 `GetPropertyConfig`）。**（本轮再修订见 §8：voice context 由快照改活视图薄壳、VoiceId 并入 `IVoiceSynthesisContext`、加 `GetPhonemePropertyConfigs`；上述 commit-重算-diff 机制不变。）**
  voice 走材料化缓存（part 参数驱动重算），effect 走惰性 dirty 缓存（自身参数驱动），宿主聚合签名去抖、仅轨集合实变才刷新 UI。
  孤儿数据归宿定为**保留隐藏、轨复现即原样恢复**：数据层不因声明收缩而裁剪曲线，隐藏轨不参与合成。
  引擎自发的运行中变化（如异步模型加载后改轨集合，非参数驱动）若将来需要，再加声明级变更事件——当前 context 驱动已覆盖参数驱动的全部场景。）
- ~~动态立绘~~（**已实现**：静态图 + 动图 animated webp/gif/apng，帧播放器走 Skia `SKCodec`，详见 §8 `Portrait` 宿主渲染）；动态全局背景图、序列帧目录立绘、Live2D/Spine 富动态仍为独立特性。

- ~~**合成参数回显 + 可编辑分段轨**~~（**已实现**）：
  - 合成参数回显：`IVoiceSynthesisSession.SynthesizedParameters`（按轨 id 键、与音频/音高同一秒时间系、分段）端到端透传
    （pipeline→MidiPart），在参数栏按 id join 到同名 voice 轨上**只读叠加**（白色半透明、NaN 段断开），镜像合成音高回显。（effect 回显后于阶段三补齐，见 §10.7。）
  - 可编辑分段轨：除 Pitch 外，声源/效果器在 `GetAutomationConfigs` 里声明分段轨（`AutomationConfig.DefaultValue=NaN` ⇒ `IsPiecewise`；见 §7 合并记），
    宿主按轨 id 存 `DataObjectMap<string, IPiecewiseAutomation>`（MidiPart + Effect 各一份；Pitch 仍是专属常驻通道、不入此 map），
    MidiPartInfo/EffectInfo 各加 `PiecewiseAutomations` 序列化槽（同 Pitch 形、孤儿数据整存）；参数栏列出、按 kind 渲染（段间 NaN 断开）、
    编辑交互镜像 pitch（绘制/擦除/锚点选移删插）。AutomationKey 保持纯路由，kind 由查 config map 现解析。
    引擎对分段轨的 DSP 消费（effect 分段轨回写、voice 分段轨参与合成）为后续需求，当前仅"可编辑 + 存盘 + 显示"。

- ~~**统一 automation config（连续/分段合一）**~~（**已实现**，见 §7）：合并为一个 `AutomationConfig` + 一个 `GetAutomationConfigs`，
  `DefaultValue` 为 NaN ⇒ 分段轨（`IsPiecewise`）。删去 `PiecewiseAutomationConfig` 与两处 `GetPiecewiseAutomationConfigs`。
  宿主保留两数据类型 + 两序列化槽，路由处按 `IsPiecewise` 现解析；唯一特判 NaN 的 automation 消费方是 `AutomationDefaultsController`（分段轨不显默认基线行）。

- ~~**合成参数回显升级为只读回显轨**~~（**已实现**）：旧版 `SynthesizedParameters` 是"按 id 叠加到同名编辑轨、借其 config 画白线"，已废弃。
  改为**一等只读回显轨**：`IVoiceSynthesisSession` 加 `GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext) → IReadOnlyOrderedMap<string, AutomationConfig>`
  （回显是分段形、`DefaultValue=NaN`；context 驱动、可预声明 ⇒ 合成前 key 即存在、显隐不抖），`SynthesizedParameters` 退回只承载曲线数据（按同一批 key）。
  宿主把这些 key 作**可显隐的只读轨**（独立 key 如 `energy`、自带 Min/Max/Color/DisplayText）：在 `Voice.SynthesizedParameterConfigs` 并行材料化（镜像 `RebuildAutomationConfigs`，
  随 part 参数 commit 重算、纳入聚合签名）；UI 侧独立于可编辑轨的 `AutomationKey`/`VisibleAutomations`/tabbar 机制——
  `PianoWindow` 持 `mVisibleReadbacks` + `SetReadbackVisible/IsReadbackVisible/ReadbackVisibilityChanged`，显隐 chip 放参数区**标题栏**
  （`ParameterTitleBar` 由哑色条升级成细工具条：chip 命中切显隐、空白区仍拖拽改高），`AutomationRenderer` 在可编辑轨之上、活动轨之下把回显轨绘成
  **曲线与底部基线围成的半透明积分面积**（用各自 config 色，分段 NaN 断开），只读、不可激活、不可编辑。去掉了"叠加到同名编辑轨"逻辑。
  线程契约（数据线程发布、发布即不可变、StatusChanged 单一刷新）已补进三个曲线产物成员注释。

- ~~**phoneme 输出模型小复盘**~~（**已定**）：输出改回 **per-note map** `IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>>`、**砍掉无主音素**（Note=null）。时长模型下音素只报时长、无绝对位置，无主音素无锚不可定位、也落不进 note→piece 失效链，故无意义；越界由宿主派生位置自然产生，无需扁平结构表达。进 / 出描述符合并为单一 `SynthesizedPhoneme`（无 Note 字段）。breath 等将来用「归属 note 的前置 / 后置音素」或专属事件通道承载。**（本轮再修订见 §6：引入 per-phoneme 属性后 `SynthesizedPhoneme` 仍维持单一方向无关类型——属性不混进几何，而是经独立载体 `VoiceSynthesisPhonemeSnapshot`（= `SynthesizedPhoneme` 描述符 + `PropertyObject` 属性）随合成快照承载；per-note map 与砍无主音素不变。）**

---

## 10. Effect 收敛：每段一个厚 processor

当前 effect 是**宿主厚 / 插件薄**的逐段处理器：宿主（`VoiceSynthesisPipeline`）全包了 `cache[segment][stage]` 失效图、脏判定、变化事实构造（`IEffectChange`）、段间串行调度、`input[i]=output[i-1]` 链接线；插件只看到 `IEffectProcessor.Process(整段 input, output, change)`。本节把它收敛到**厚 processor / 每段一个**——保留 `IEffectProcessor` 概念（不另立会话），但让它变厚：持有自己那一段的上下文，自管这一个 segment 生命周期的失效与重处理。

> 与早期"对偶 voice 会话（`IEffectSession` + 多段 `IEffectContext` + `GetSnapshot` 快照）"的设想相比，本模型刻意更简：**段间彼此无共享上下文**（音频段间共享上下文意义不大，分别处理后再混音才是多数算法更正确的做法），故无需会话去统管多段、无需快照/开窗机制。早期 §9 列的决策 ①②（平行 session / 对偶 context）被本模型覆盖；③（链为单位调度）④（自算 dirty）⑤（`SynthesizedParameter`）不变。

### 10.1 模型与退役

```
IEffectEngine                每"effect 类型"一个：声明参数/自动化、创建处理器
  └ IEffectProcessor          每「effect 实例 × 一个上游音频段」一个：持有本段上下文，自管该段失效与重处理
```

- `IEffectEngine.CreateProcessor()` → **`CreateProcessor(IEffectContext context)`**（context 绑定该 effect × 一个上游段）。
- **退役**：`IEffectInput` / `IEffectOutput` / `IEffectChange`（其职责并入下面的 `IEffectContext` 活视图 + processor 自管失效）。
- **段间独立、无共享上下文**：每个 processor 只看自己那一段，对链结构无感；宿主把 effect[i] 的输出段接成 effect[i+1] 的输入段。

### 10.2 输入：`IEffectContext`（每 processor / 每段）

宿主实现、绑定「该 effect × 一个上游音频段」、随 processor 死。processor 订阅它、自管失效。

```csharp
public interface IEffectContext
{
    // 本段输入：整段不可分割。worker 直读其不可变 PCM（按 CommitVersion，重 Commit 换新缓冲）——
    // 不剪切、不开窗、无快照拷贝（对比旧模型零额外开销，旧模型本就按版本物化一份 buffer 按引用传）。
    IUpstreamAudioSegment Input { get; }

    // 该 effect 自身参数活视图（取代退役的 IEffectInput.Properties；订阅 Modified 标参数脏）。
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 该 effect 声明的连续自动化轨（按 key；查询轴 = 全局秒）：只读 map，可枚举可点取（分段轨不在此列）。
    // processor 订阅各轨 ISynthesisAutomation.RangeModified、按本段时间界自筛标脏。
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }

    // 产出（与 voice 同一握柄 IAudioSegment）：自由重分段——输出段起始/长度/采样率均可与输入不同，
    // 可一段进多段出。宿主把输出段接成下游 effect 的 Input。仅数据线程调用。
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);

    IActionEvent Committed { get; }   // 逻辑编辑收口（同 IVoiceSynthesisContext.Committed），processor 在此一次性做重活
}

// 上游音频段的只读视图（voice 输出，或链上前一个 effect 的输出）：整段、不可分割。
public interface IUpstreamAudioSegment
{
    long SampleOffset { get; } int SampleCount { get; } int SampleRate { get; }
    ReadOnlyMemory<float> Samples { get; }   // 已提交版本不可变整段 PCM；同步前缀抓引用、worker 直读
    int CommitVersion { get; }               // 重 Commit 递增，processor 据此判是否需重处理
    IActionEvent Committed { get; }                  // 内容变（未来可加性补 RangeCommitted 局部更新信号）
}
```

**为何整段直读、无快照**：上游 segment 是一个整体输入单位、不可分割（不像 voice 要从乐谱开窗物化 note/曲线）。已提交版本的 PCM 不可变（重 Commit = 换新缓冲、版本递增），故 worker 在同步前缀抓住引用后可直读，无需拷一份快照——与旧模型同效率（旧模型本就按 commit 版本物化一份 buffer 按引用传 `Process`）。自动化值由 processor 在同步前缀用 `ISynthesisAutomation.Evaluate`（数据线程）预采成数组再 offload；参数同理读值。都是 processor 自管的轻量捕获，不需要 SDK 快照类型。

### 10.3 `IEffectProcessor`（厚）

```csharp
public interface IEffectProcessor : IDisposable
{
    // (重)处理本段：同步前缀读 context（数据线程、抓 PCM 引用 + 预采自动化）后 offload；产出经 context.CreateAudioSegment。
    // 语义同 voice SynthesizeNext：返回纯 Task 无 outcome；取消正常返回（不抛 OperationCanceledException）；
    // await 真正返回 = 槽位释放；错误抛异常，宿主在调用边界 catch → 该段 passthrough 降级。
    Task Process(CancellationToken cancellation = default);

    // 订阅 context（Input.Committed / Properties.Modified / automation.RangeModified）自标脏后触发 → 宿主据此调度 Process。
    IActionEvent ProcessingRequested { get; }
}
```

- **进度不传入 `Process`**：与 voice `SynthesizeNext` 去 `IProgress` 一致；进度作为 processor 成员将来加性补。
- **第一版不带 `GetStatus`**：宿主从各 processor 生命周期推导 UI 状态带（建=Pending、Process 中=Synthesizing、归=Synthesized、抛=Failed）；将来如需 processor 自报状态再加性补。

### 10.4 失效自管（决策 ④：`IEffectChange` 退役）

原 `IEffectChange`（= `VoiceSynthesisPipeline.StageChange`）由宿主构造"自上次变了什么"递给插件；现搬进 processor：processor 订阅 `IEffectContext` 自算 dirty——`Input.Committed` → 输入变；`Properties.Modified` → 参数变；`automation.RangeModified` → 该轨该秒区间变（按本段时间界自筛）。脏 → 触发 `ProcessingRequested` → 宿主调度 `Process`。原宿主的 `cache[segment][stage]` 二维失效图就此**分解进各 processor**（每个 processor 管自己那一格的失效与重处理），宿主不再算变化事实。无内部增量的引擎仍可"任何信号 → 整段重处理"。

### 10.5 调度：链为单位（决策 ③：宿主策略，不进冻结面）

- 宿主持「effect × 段」的 processor 图：voice 段 Commit → 作为 effect[0] 某 processor 的 `Input`；effect[i] 的输出段集 → effect[i+1] 各建 processor 接为 `Input`。
- 按 `ProcessingRequested` + 播放线就近、**链为单位**调度（一个段的 processor 链自上而下连续跑完）；不同段/不同 part 并行；voice 非音频产物（pitch/phoneme）不依赖 effect、eager 暴露不被串行 gate。
- 链尾各 processor 输出**按时间混音**汇成最终音频（分别处理后再混音，正确处理 effect 尾巴重叠；非硬拼接）。
- 这是宿主调度策略、可自由演进、不进冻结 ABI。冻结面只有 `Process` / `ProcessingRequested` / `CreateAudioSegment` / `Committed` 这组原语。

### 10.6 `SynthesizedParameter` 富类型（决策 ⑤）（**已实现**）

`SynthesizedParameters` 原是 `IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<Point>>>`——双重裸嵌套。已给参数回显曲线一个**具名冻结值类型**（命名按 §3.2：产物用 `Synthesized*`，避开已被宿主数据层占用的 `PiecewiseCurve`），成员换形为 `IReadOnlyMap<string, SynthesizedParameter>`（voice/effect 两侧同形）：

```csharp
public sealed class SynthesizedParameter
{
    // 各连续段，按时间升序、互不重叠；段内 Points 为 (全局秒, 值) 折线。空集合=整条无值；段间间隙=NaN 区（绘制断开）。
    public required IReadOnlyList<IReadOnlyList<Point>> Segments { get; init; }
}
//   SynthesizedParameters : IReadOnlyMap<string, SynthesizedParameter>   // 成员名不变
```

形态用 nested-segments（产物本由合成器预分段，渲染端要段折线、不想逐点扫 NaN）。**`SynthesizedPitch` 另立具名类型 `SynthesizedPitch { Segments }`**（与 `SynthesizedParameter` 有意**不共类型**）——pitch 是固定专属通道（宿主全知其色/量程，将来加清浊 / 颤音分解等专属维度），parameter 是动态 keyed 集合（引擎声明、自带 Min/Max/Color 元数据）；二者各自演进、不同线，套同一类型是 category error（同 §7「automation 不是一种 slider」、§0.3「解耦 > DRY」）。

### 10.7 effect 回显（与 voice 同构）（**已实现**）

effect 引擎与 voice 一样能暴露**一等只读回显轨**，端到端打通：
- **SDK producer 面**（冻结面加性扩展）：`IEffectEngine.GetSynthesizedParameterConfigs(IEffectPropertyContext) → IReadOnlyOrderedMap<string, AutomationConfig>` 声明回显轨（live 纯函数 of 当前参数，镜像 `GetAutomationConfigs`）；`IEffectProcessor.SynthesizedParameters → IReadOnlyMap<string, SynthesizedParameter>` 承载**本段**曲线数据（线程契约同音频产物：数据线程发布、宿主只读，无新事件——宿主在 `Process` 收尾随 `SynthesizedSegments` 一并重读）。
- **宿主聚合**：`EffectGraph` 把同一 `IEffect` 的各段 processor 回显按 key 拼接（段按起始秒升序），经 `MidiPart.GetEffectSynthesizedParameters(effect)` 暴露。
- **UI 按源统一**：标识复用 `AutomationKey`（voice 与 effect+index 区分源）；`PianoWindow.mVisibleReadbacks` 升 `HashSet<AutomationKey>`、`ReadbackConfigs` 合并 voice + 各 effect（`AutomationKey` 键）；`ParameterTitleBar` chip 按源分组（每组前置 `Voice`/effect Type 源标签）；复用 `AutomationRenderer.DrawReadbackArea`。
- 夹具 `V1.Effect` 的 `TLTestGain` 真产一条 `loudness` 回显（输出按窗 RMS），`TLTestReverse` 无回显。

### 10.8 分阶段落地

- **阶段一（设计 + voice 落地，已完成）**：本节设计定稿；voice 侧 `SynthesizeNext` 去 `IProgress` 落地（进度改经状态带 + StatusChanged）。
- **阶段二（effect 实现，一体 chunk，已完成）**：新 `IEffectProcessor`（厚）/ `IEffectContext`（每段）/ `IUpstreamAudioSegment` + 退役 `IEffectInput`/`Output`/`Change` + `IEffectEngine.CreateProcessor` 改签名 + 宿主 effect 半部重写为「effect × 段」反应式 processor 图（`EffectGraph`：voice 段 / 各级输出段为图中输入，每节点一厚 processor 自管失效；reconcile + pump、跨段并行受 `Settings.MaxParallelSynthesisTasks` 全局闸门封顶；链尾各输出段经消费端按绝对时间混音）+ 夹具 `V1.Effect` 重写。compat 无 effect 适配（无需同步）。这些改 `IEffectProcessor`/`IEffectEngine` 签名、必须一起落才编译绿，故为一体 chunk（不可像 voice 侧那样纯 additive）。
- **阶段三（已完成）**：`SynthesizedParameter` 富类型换形（`SynthesizedParameters` → `IReadOnlyMap<string, SynthesizedParameter>`，连带 voice producer/consumer）；effect 回显端到端（SDK producer 面 + 宿主聚合 + UI 按源统一到 `AutomationKey`，见 §10.6/§10.7）+ `V1.Effect` 真产回显轨 + 文档/测试（`tests/EFFECT-READBACK-TEST-CASES.md`）。
