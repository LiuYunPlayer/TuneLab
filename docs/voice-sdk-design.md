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
IVoiceEngine        每"引擎类型"一个：加载模型、列声库目录、创建合成会话
  └ ISynthesisSession   每"part 合成"一个：声明 + 调度 + 产物 + 状态
```

```csharp
public interface IVoiceEngine
{
    // 声库目录（菜单/选择器用，无需创建会话即可读）
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; }

    void Init();      // 见 §2
    void Destroy();

    // voiceId 选定声库；context 为该 part 的输入活视图（见 §3）
    ISynthesisSession CreateSession(string voiceId, ISynthesisContext context);
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

`session = engine.CreateSession(voiceId, context)`。会话绑定一个 part，活到 part 被删除（`Dispose`）。换声源（换引擎）时：宿主丢弃旧会话、重建新会话，**context 也随会话重建**（稳定的是其背后的数据层）。

### 3.2 输入：`ISynthesisContext`（会话级、订阅式活视图）

context 由**宿主实现、会话级**（每次 `CreateSession` 新建、随会话死），向插件暴露**可订阅属性**。插件用与宿主侧一致的手感订阅：

```csharp
public interface ISynthesisContext
{
    // 链表形态（无索引承诺——宿主数据层即双向链表，可索引是插件不需要的承诺）：
    // 顺序消费用枚举、头尾 O(1) 走 First/Last、邻居导航走 note.Next/Last；支持 WhenAny。
    IReadOnlyNotifiableLinkedList<ISynthesisNote> Notes { get; }
    PropertyObject PartProperties { get; }                    // 可订阅
    bool TryGetAutomation(string key, out ISynthesisAutomation automation);
    ISynthesisAutomation Pitch { get; }            // 绝对约束：有值=用户钉死，NaN=插件自由
    ISynthesisAutomation PitchDeviation { get; }   // 加性偏差：处处有值、默认 0、永不 NaN

    // 物化合成快照（插件主动拉取，见 §3.5/§4）：notes = 本次合成所需 note（含协同发音邻居，
    // 插件自由圈定，返回 snapshot.Notes 与之索引对齐）；[startTime, endTime] = 曲线开窗区间（秒）。
    // 仅数据线程、仅 SynthesizeNext 同步前缀调用；一次合成可按需拉多份。
    SynthesisSnapshot GetSnapshot(IReadOnlyList<ISynthesisNote> notes, double startTime, double endTime);

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
    event Action<double, double>? RangeModified;   // (startTime, endTime)，全局秒
}
```

**音高双通道（绝对约束 + 相对偏差）**：`Pitch` 是用户钉死的绝对音高曲线（分段型：有值=钉死、NaN=插件自由发挥）；`PitchDeviation` 是加性偏差（连续型：处处有值、默认 0、永不 NaN），宿主侧偏差源（vibrato，将来 humanize 等）全部汇于此。合成契约：**`finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)`**——插件先解析绝对面（钉死区用用户值、自由区自己生成），再叠加偏差。由此偏差也作用于未绘制 pitch 的自由区域（旧管线把 vibrato 叠在绘制曲线上，自由区无载体、偏差丢失——结构性修复）。失效通道随之分流：pitch 曲线变更 → `Pitch.RangeModified`；vibrato 几何/包络变更 → `PitchDeviation.RangeModified`。

**变更定位的三种最小事实**：字段变了（note 可订阅属性，配合 `WillModify`/`Modified` 拿新旧值）、区间变了（曲线 `RangeModified` 带秒范围）、集合变了（`Notes` 增删）。失效依赖图（这些事实映射到哪些段、重合成到管线哪一级）归插件——机制粒度足够支撑最精细策略，也允许懒插件"任何通知 → 全部标脏"。**不设独立的"时基变了"信号**：tempo 变化被分解为上述具体事实——note 边界秒值变（`StartTime/EndTime.Modified`）+ automation 秒映射移位（宿主在批量括号内对受影响轨触发全区间 `RangeModified`），插件用既有订阅即收到（详见 §3.3）。

**命名约定（线程纪律编码进名字）**：`Synthesis*` 前缀 = 会话活视图家族（可订阅，仅数据线程）；`*Snapshot` 后缀 = 不可变冻结物家族（纯值无事件，可跨线程）；`IAutomationEvaluator`/`ITiming` = 横跨两域的求值/换算能力接口（实现可活可冻，接口面不带事件）。活视图上的事件恒在数据线程触发与处理；快照上**没有**事件（类型上拿不到，"把回调留到合成线程"写不出来）。出方向（插件→宿主）的 `StatusChanged` 允许任意线程触发、宿主负责 marshal（v2 跨进程时它本就是 IPC 消息）；进度用 `IProgress<double>`（`Progress<T>` 自带 SynchronizationContext marshal）。

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

### 3.3 `ISynthesisNote` / 时间真值域 / `ITiming`

宿主业务层的 `INote` **不暴露**；SDK 另立 `ISynthesisNote`，字段皆为可订阅属性。**固定字段保持最小**（通用乐理属性），voice 专属的 per-note 参数一律走 `Properties`（keyed）——加新参数 = 加 `NoteProperties` 的 key，不动 `ISynthesisNote` 固定面。

```csharp
public interface ISynthesisNote
{
    IReadOnlyNotifiableProperty<double> StartTime { get; }   // 全局秒（tempo 派生，变化经 Modified 通知）
    IReadOnlyNotifiableProperty<double> EndTime   { get; }
    IReadOnlyNotifiableProperty<int>      Pitch  { get; }
    IReadOnlyNotifiableProperty<string>   Lyric  { get; }
    IReadOnlyNotifiableProperty<IReadOnlyList<PinnedPhoneme>> Phonemes { get; }  // 见 §6
    PropertyObject Properties { get; }   // 可订阅；voice 专属 per-note 参数都在这

    // 邻居链保留（协同发音方便）。注意：合成须在快照上沿链导航，见 §3.5。
    ISynthesisNote? Next { get; }
    ISynthesisNote? Last { get; }
}
```

**插件侧全秒轴原则：插件面对的所有时间量统一为全局秒，tick 只是宿主乐谱内部表示、不外露。**合成是声学域作业（音频按秒/采样点），插件需要的永远是"第 X 秒"；note 边界、曲线查询点、开窗区间、`RangeModified` 区间一律秒。秒由 tempo 表换算而来——精度上 double 秒在工程规模下远超采样点（比 48kHz 采样间隔精确约 7 个数量级），对合成无损；tick 的整数精确性价值在编辑域（网格对齐、定点比较），合成域用不到。插件因此**不碰任何 tick↔秒换算**：宿主在 note 边界派生、求值器边界、快照物化处完成换算，`ISynthesisContext` 与 `SynthesisSnapshot` 都**不暴露** `ITiming`。

tick↔秒换算仍由宿主内部的 `ITiming`（`LiveTiming` 活实现 / `TempoSnapshot` 冻结实现）承担，但它退为宿主内部契约，不进插件可见的 context/snapshot 面。

**tempo 变化无独立信号**（删去了曾经的 `TimingModified`）：tempo 变 → 所有 note 的秒边界变 → `StartTime/EndTime.Modified` 自动触发（宿主把 `TempoManager` 接入 note 边界派生属性的源）；automation 的秒映射移位 → 宿主在批量括号内对受影响轨触发全区间 `RangeModified`。插件用既有订阅机制即收到具体变更，无需"时基变了"这种元信号——这是全秒轴的优雅副产品。

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

- **`GetNextSegment`**：数据线程上的**廉价 peek**，live 全量访问——插件基于完整 part 做分片决策，只报出纯值秒边界（`SynthesisSegment` struct）；peek 常被多会话 speculative 地叫、多数不中选，不做任何捕获。
- **`SynthesizeNext` 同步前缀**：仍在数据线程，插件**重算分块**（确定性分片 + peek→commit 同调度 tick 无编辑 ⇒ 与 peek 同结果），随后经 **`context.GetSnapshot(notes, startTick, endTick)` 主动拉取**所需快照——notes 与开窗区间由插件按本次合成需要自由圈定，一次合成可按需拉多份（如音素级小窗 + 音频级大窗）；**之后**才 offload 到 worker（进程内）/ 序列化送进程（v2）。
- 拉取式替代早期"segment 携带捕获声明、宿主代为物化递入"的形态：声明本就是插件需求的间接表达，直接调用消除一层间接；物化/版本缓存/记账仍收在宿主的 GetSnapshot 实现内，`GetSnapshot` 入口带数据线程断言（§3.2 防线 ②）兜住"offload 后才拉"的违例。

#### 快照构成（非对称：小而必须的送、大而要算法的留宿主侧）

| 数据 | 形态 | 进程内 | 跨进程（v2） |
|---|---|---|---|
| **note** | eager 物化的不可变值快照（`StartTime/EndTime` 全局秒、`Pitch`、`Lyric`、`PinnedPhoneme[]`、`Properties` 值拷；有序列表与递入 notes 索引对齐，邻居按索引导航） | worker 直读 | 序列化进消息体送过去 |
| **automation** | **host 侧不可变原始点快照**；插件经 `IAutomationEvaluator.Evaluate(points)` 拉采样值 | worker 直接调求值器、宿主插值算法就地对冻结点插值 | 快照序列化时物化为离散点（提前采样，跨进程牺牲项；细节缓后）；**插值算法恒在宿主侧** |
| **timing** | `ITiming` 接口接缝（实现在宿主侧 `TempoSnapshot`，与 live 同一套共享算法）；SDK 契约面只声明接口 | worker 直接调冻结实现 | 快照序列化时物化为离散数据（细节缓后） |

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
| `SynthesisSegment`（纯值 struct） | 两个 double | 两个 double 直接过线 |
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
public interface ISynthesisSession
{
    // —— 调度 ——
    // peek：窗内"下一块待合成"的纯值边界，无副作用
    SynthesisSegment? GetNextSegment(double startTime, double endTime);

    // commit：合成 peek 报出的这一块。插件在同步前缀重算分块、经 context.GetSnapshot
    // 拉取所需快照后 offload；await 返回 = 槽位释放、宿主重排
    Task SynthesizeNext(SynthesisSegment segment,
                        IProgress<double>? progress = null,
                        CancellationToken cancellation = default);

    // ... 声明 / 产物 / 状态见下 ...
    void Dispose();
}

// 调度块的纯值边界（readonly struct）：宿主只用它排播放线就近优先级。
// 不携带捕获声明、不是插件对象——快照获取归插件主动（context.GetSnapshot）；
// 插件 peek 时如需为 commit 留信息（分块缓存等）在会话自己的字段里存即可。
public readonly struct SynthesisSegment(double startTime, double endTime)
{
    public double StartTime { get; }   // 秒，与产物同一时间系
    public double EndTime { get; }
}

// 宿主物化的不可变快照（context.GetSnapshot 的返回体）：纯数据体故为具体类型（§0 原则 5），
// 无参构造 + required init（初始化后不可变，加字段纯加性）。形状与活视图镜像对称。
// 物化/版本缓存/限速/并发记账全留宿主一处；v2 跨进程时它就是 GetSnapshot 一次批量 RPC 的返回体。
public sealed class SynthesisSnapshot
{
    public required IReadOnlyList<SynthesisNoteSnapshot> Notes { get; init; }   // 与递入 notes 索引对齐（邻居按索引导航）
    public required ITiming Timing { get; init; }    // 接口接缝：实现在宿主侧（与 live 共享算法），SDK 不带实现
    public required SynthesisAutomationSnapshot Pitch { get; init; }          // 可扩展容器（裹全局秒轴求值器 Evaluator），开窗 = 拉取区间；双通道语义同活视图
    public required IAutomationEvaluator PitchDeviation { get; init; }
    public required IReadOnlyMap<string, IAutomationEvaluator> Automations { get; init; }   // 全部已声明轨（可枚举 Map）
    public required PropertyObject PartProperties { get; init; }                // 值拷
}
```

**快照 note 不带邻居链**（接口最小化）：`Notes` 有序列表与 `GetSnapshot` 递入的 notes 索引对齐已含全部邻接信息，协同发音按索引取邻居即可。活视图 `ISynthesisNote` 的 `Next/Last` 保留——事件 handler 内只有 note 自身引用、无列表索引上下文，O(1) 邻居导航是分片决策的真实便利。

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
public interface ISynthesisSession   // 续
{
    // —— 音频采样率（插件 native 率；音频本体经 IAudioSegment 握柄交付，不再 ReadAudio pull）——
    // 工程率是唯一真值，宿主比对：相等直读、不等套一层流式重采样（集中宿主一处，会话与工程率变化解耦）。
    int SampleRate { get; }

    // —— 曲线类产物 ——
    IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch { get; }                       // 分段
    IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedParameters { get; }  // 同 effect 形状
    IReadOnlyList<SynthesizedPhoneme> Phonemes { get; }                                 // 见 §6

    // —— 状态 / 按段报错（UI 状态带，与音频段解耦）——
    IReadOnlyList<SynthesisStatusSegment> GetStatus();
    event Action? StatusChanged;   // 单一刷新信号
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

音素时序会**外溢**（辅音常入侵上一个音符的尾巴），故输入/输出形态不同。

### 输入（host→engine，per note）

```csharp
// 命名与输出侧对仗：Pinned 进（用户钉死约束）、Synthesized 出（引擎产物）。
public class PinnedPhoneme
{
    public string Symbol = string.Empty;
    public double StartTime;   // note-相对秒，恒有值
    public double EndTime;
}
```

- 挂在 `ISynthesisNote.Phonemes`。**相对 note 起点的秒偏移**：可编辑、随 note 移动自动跟随（偏移不变）、负值表示越界到 note 之前的辅音引导。秒（而非 tick）是因为音素时长是声学量，应随 note 平移而保持、跨 tempo 不变形。
- **钉死粒度为整 note**：列表非空 = 全部音素用户钉死（约束，引擎遵守）；空列表 = 引擎从 `Lyric` 做 G2P + 全自由定时。不支持单音素级"部分钉死"（半约束的组合空间对插件是真实负担，且宿主侧本就只产全钉死列表，没有生产者）。
- **不引入最小音素时长声明**：宿主压缩（拖短 note）封顶在非负 clamp、可以压到 0——这只是编辑态约束值，合成正确性不受影响：引擎收到不合理约束按自己的音韵学知识兜底，输出的 `SynthesizedPhoneme` 才是权威（preview 本会被全量合成覆盖，见下节"宿主公式封顶"共识）。将来若要精细编辑 UX，在声明面纯加性补元数据即可。

### 输出（engine→host，合成时返回）

```csharp
public struct SynthesizedPhoneme
{
    public string Symbol;
    public double StartTime;     // 绝对秒（与音频产物同一时间系），可越界/重叠
    public double EndTime;
    public ISynthesisNote? Note; // 出身 note（歌词归属），换气等无主音素为 null
    public double StretchWeight; // 供宿主 preview 重算，见下
}
```

- **扁平时间线 + 出身 note 引用**（而非按 note 装进字典）。`Note` 是"出身"（歌词归属），不是"压着谁"——N+1 的辅音入侵 N 的尾巴时 `Note = N+1`、`StartTime` 落在 N 范围内。无主音素 `Note = null`。这样既解开了字典 key 的二义，又能表达越界与换气。
- 修掉旧 `SynthesizedPhoneme.ToString` 的格式 bug（`"{{0}"` 被转义成字面 `{`，Symbol 没替换）。

### 伸缩与 preview（不引入 dur API）

音素如何随音符长度伸缩是引擎的音韵学知识（元音优先拉、各引擎自有比例），宿主没有元音/辅音概念。解法**不是**在宿主养一套布局引擎，而是：

- 引擎输出每个音素带 `StretchWeight`（透明权重，非黑盒）。宿主拖动/拉伸 note 时用**一条共享公式**就地算 preview，零引擎调用：
  ```
  new_dᵢ = dᵢ + Δ × (wᵢ / Σwⱼ)        // 再做非负 clamp
  ```
  辅音 w=0、元音 w=1 → 长度变化全进元音、辅音不动；w 默认 = dᵢ 即退化为均匀缩放（兼容旧行为）。宿主套公式但不需懂音韵学——知识被编码进一个数字。
- **权重随锁定持久化进工程**：用户锁定音素的那一刻，固定下来的不只是时长，是"时长 + 伸缩性质"这个整体——权重随锁定动作完成所有权转移，本质上属于用户意图固定下来的数据（与 pinned 时长同一逻辑地位），故随 pinned 音素一并进工程（Format `PhonemeInfo.Weight`，数据层 `IPhoneme.Weight`）。这根除时序错位：若权重只存在于合成产物（缓存），"工程加载后、首轮合成前拖伸 note"的压缩只能退化均匀且**错误会固化进 pinned 数据**（引擎忠实遵守错误约束，无自愈通道）；入库后压缩任何时刻都有正确分布可用。旧工程缺省 Weight=0 → 与 **Σw ≤ 0**（插件未设权重）共用一条防御路径：退化均匀缩放。SDK 输入面（`TuneLab.SDK.PinnedPhoneme`）不带权重——引擎只消费钉死时长，权重是宿主编辑侧知识载体。
- 移动 note → 相对偏移不变、跟随平移，不重算。
- **preview 纯显示、绝不反馈给引擎当约束**；权威时长由**全量合成**重新定时并返回（带新权重），覆盖 preview（接受短暂"跳变"，因合成本就按播放线就近增量调度、纠正及时）。
- 宿主公式**封顶在"权重 + 非负 clamp"**——真实下限/协同发音等硬情况交给引擎全量合成，不靠养大宿主公式覆盖。

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

---

## 8. 声明数据与 Config 家族

- **目录元数据**在 `IVoiceEngine.VoiceSourceInfos`（菜单/选择器用，无需会话）：

  ```csharp
  public struct VoiceSourceInfo
  {
      public string Name;
      public string Description;
      public ImageResource? Portrait;   // 可选立绘，显示在钢琴窗
  }
  ```
  `Portrait` 是格式无关的资源引用：封闭层次（构造器 private protected，变体仅 SDK 内新增），变体按数据形态分型（v1 仅 `FileImageResource` 路径变体——可指向图像文件或序列帧目录）、保持可序列化数据形态；动图（GIF/APNG）是宿主解码能力不进类型，Live2D/Spine 等富动态为独立特性。运行时会变的图像走目录变更信号（将来 `IVoiceEngine` 加性事件），资源对象本身恒为不可变值。

- **会话直接暴露声明**（不再包一个 `VoiceSourceInfo` 字段，元数据由 `VoiceSourceInfos` 提供、不重复）。
  **接口面只保留函数式获取**：接口不为它单设属性面——否则"固定属性 + 动态方法回退"两套并存显得多余。
  **声明全部 context 驱动、纯函数**：轨集合（连续/分段）与属性面板均 = f(当前 part/note 参数值)，
  宿主在参数 commit 时按当前值重算并 diff 到 UI——轨集合可随参数显隐（条件轨），属性面板可随值换控件/显隐。
  静态声明的插件忽略 context 返回固定 map/config 即可。**孤儿数据**：轨从声明消失后宿主保留其已画曲线
  （隐藏不删、不参与合成），参数回退使轨复现即原样恢复（数据层不裁剪）。

  ```csharp
  public interface ISynthesisSession   // 续
  {
      string DefaultLyric { get; }

      // 自动化轨声明（part 级，context 驱动）；commit 时重算 + diff 轨集合。
      IReadOnlyOrderedMap<string, AutomationConfig>          GetAutomationConfigs(IPartPropertyContext context);
      IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> GetPiecewiseAutomationConfigs(IPartPropertyContext context);

      // 条件面板（吃宿主现造的 property context，纯函数，commit 时重算 + keyed-diff）；
      // 静态面板的插件忽略 context 返回固定 ObjectConfig 即可。
      ObjectConfig GetPartPropertyConfig(IPartPropertyContext context);
      ObjectConfig GetNotePropertyConfig(INotePropertyContext context);
  }
  ```

- **扁平 config 原则推广**：整个 `IControllerConfig` 家族（SliderConfig/TextBox/CheckBox/ComboBox/ObjectConfig）审一遍，去掉为复用而做的具体类继承，各自自包含、只共享最小接口。（实现阶段执行）

---

## 9. 待办与缓后

**隔离/快照实现清单**（§3.5 已定调：数据层不加锁、不做 COW，靠不可变快照隔离）
- 不可变**原始点快照容器** + 在其上的不可变 `IAutomationEvaluator` 实现（`Evaluate(times)` 对冻结点插值，查询轴秒）。
- 抽一份**共享纯采样函数**：live `IAutomation`/`IPiecewiseCurve` 与上面的冻结求值器共用同一套"锚点 → 取值"算法（逻辑一份，杜绝两套实现漂移）。
- **note 快照值**（StartTime/EndTime、Pitch、Lyric、`PinnedPhoneme[]`、Properties 值拷；有序列表索引对齐、不带邻居链）；automation 按无变形开窗规则（见 §3.5）取原始锚点。
- **tempo 快照**（`ITiming` 的冻结实现；实现家族居宿主 `TuneLab.Data.Timing`，SDK 只声明接口）。
- 可选：automation 切片**版本缓存**（按"曲线版本 + 区间"缓存不可变副本，`RangeModified` 命中才作废/重拷）。
- 契约钉死：`GetNextSegment` = 数据线程上廉价 peek（live 全量、定分片，报纯值边界）；`SynthesizeNext` 同步前缀在数据线程重算分块、经 `context.GetSnapshot` 拉取快照，再 offload。
- 前向兼容进程拆分：快照构造为自包含可序列化值树、曲线点用 blittable `Point`；不做真正的 IPC/共享内存（v2）。

**实现阶段的清理项**
- `IControllerConfig` 家族扁平化审计（§8）。
- `ISynthesisData.GetAutomation` → `TryGetAutomation` 等命名对齐。
- `SynthesizedPhoneme.ToString` 格式 bug。
- DataObject 补 `WillModify` 事件（NotifiableProperty 统一的一环）。

**缓后/独立**
- 宿主全局 `Pos → Tick` 重命名（独立 refactor，注意 Format 序列化 ABI）。
- **定点 tick**（`Tick` 结构体：int64 存 1/2ⁿ tick 定点数）：全局 tick 域 double→Tick 的横切 refactor（数据层 + Format 序列化，可与 `Pos → Tick` 重命名同做）。收益是加减/比较**零误差**与跨进程确定性（`==` 恢复语义、结果与量级无关），**非音质**——double 在现实工程规模下误差比可感知量小 9 个数量级以上。关键约束：**n ≤ 16**，否则与 double 互转重新引入舍入（double 仅能精确表示 ≤2⁵³ 的整数，须 `pos < 2^(53−n)` tick）；秒域保持 double（其精度参照物是采样点，由采样率换算而来，不归 tick 管）。**决策时限：对外发布冻结 SDK 前定案**——若 `Tick` 进 SDK 冻结面则影响接口签名；若仅做宿主内部存储、SDK 边界转 double（n 取小则无损），则与 SDK 解耦、随时可做。
- **RESOLUTION 维持常量 480**（= 2⁵·3·5，二/三/五连音至 128 分音符整数落格；与 MIDI PPQ 同概念——SMF 按文件头可变、DAW 内部固定常量，导入按 `480/filePPQ` 缩放）：不做可调（每个 tick 数值失去自释含义、跨工程粘贴要换算、进 Format ABI，全是横切成本而收益约等于零）。若需更细网格，将来定点化时顺路一次性升高常量（如 960），那次本就要动数据层与 Format。
- **简易合成框架**（双 SDK）：把宿主式分片/调度做成插件侧库，简单插件复用、自定义插件走原生托管。本设计先做核心协议，框架降优先级；它同时可收编 legacy 引擎的薄模型适配。
- **音频段内子区间增量**（`IAudioSegment` 段内增量）：`Write(offset, samples)` 本就带"段内哪段变了"的区间（中间态仅驱动进度/波形、不进 effect），宿主累积这些区间随 `Commit` 交 effect，effect 自行决定段内局部重合成 + 拼接（含上下文余量 / 淡化、跨级脏传播）。V1 按整段失效（段 Commit 即整段送 effect、不消费写区间），子区间增量是纯加性优化、缓后。
- **effect 收敛到本会话模型**（当前 effect 为 task 模型）。重构时一并处理：
  - ~~`IAutomationEvaluator` 与 `ISynthesisAutomation` 的合并/归属再审~~（已决：维持继承——is-a 成立，同一份采样例程同型吃活/冻两面；接口轴无关、轴由暴露面规定）。
  - `SynthesizedParameters` 的双重 `IReadOnlyList<Point>` 实为 piecewise 结构，届时考虑引入富类型（与 PiecewiseAutomation 概念对齐），两 SDK 同步换形。
  - ~~`IPropertyContext` 从 SDK.Voice 挪 SDK.Base（effect 条件面板复用）~~（已随 SDK 程序集合并消解：voice/effect 同居 `TuneLab.SDK` 顶层命名空间，effect 可直接复用）。
- ~~**动态声明面**：轨集合/属性声明运行中变化 + 既有轨用户数据的归宿~~（**已实现**：声明全部 context 驱动、纯函数，
  宿主在参数 commit 时按当前值重算并 diff——轨集合随参数显隐（`GetAutomationConfigs`/`GetPiecewiseAutomationConfigs`
  收 `IPartPropertyContext`），属性面板同 `GetPartPropertyConfig`/`GetNotePropertyConfig`；effect 侧 `IEffectEngine.GetPropertyConfig`/`GetAutomationConfigs` 同构（各收 effect 专属 `IEffectPropertyContext`——voice/effect context 分开以备 effect 将来追加 part 级官方字段而发散；effect 单层故用不带 Part 的 `GetPropertyConfig`）。
  voice 走材料化缓存（part 参数驱动重算），effect 走惰性 dirty 缓存（自身参数驱动），宿主聚合签名去抖、仅轨集合实变才刷新 UI。
  孤儿数据归宿定为**保留隐藏、轨复现即原样恢复**：数据层不因声明收缩而裁剪曲线，隐藏轨不参与合成。
  引擎自发的运行中变化（如异步模型加载后改轨集合，非参数驱动）若将来需要，再加声明级变更事件——当前 context 驱动已覆盖参数驱动的全部场景。）
- 动态立绘 / 动态全局背景图（宿主渲染能力，独立特性）。

- ~~**合成参数回显 + 可编辑分段轨**~~（**已实现**）：
  - 合成参数回显：`ISynthesisSession.SynthesizedParameters`（按轨 id 键、与音频/音高同一秒时间系、分段）端到端透传
    （pipeline→MidiPart），在参数栏按 id join 到同名 voice 轨上**只读叠加**（白色半透明、NaN 段断开），镜像合成音高回显。effect 无参数回显（输出仅音频）。
  - 可编辑分段轨：除 Pitch 外，声源/效果器在 `GetAutomationConfigs` 里声明分段轨（`AutomationConfig.DefaultValue=NaN` ⇒ `IsPiecewise`；见 §7 合并记），
    宿主按轨 id 存 `DataObjectMap<string, IPiecewiseAutomation>`（MidiPart + Effect 各一份；Pitch 仍是专属常驻通道、不入此 map），
    MidiPartInfo/EffectInfo 各加 `PiecewiseAutomations` 序列化槽（同 Pitch 形、孤儿数据整存）；参数栏列出、按 kind 渲染（段间 NaN 断开）、
    编辑交互镜像 pitch（绘制/擦除/锚点选移删插）。AutomationKey 保持纯路由，kind 由查 config map 现解析。
    引擎对分段轨的 DSP 消费（effect 分段轨回写、voice 分段轨参与合成）为后续需求，当前仅"可编辑 + 存盘 + 显示"。

- ~~**统一 automation config（连续/分段合一）**~~（**已实现**，见 §7）：合并为一个 `AutomationConfig` + 一个 `GetAutomationConfigs`，
  `DefaultValue` 为 NaN ⇒ 分段轨（`IsPiecewise`）。删去 `PiecewiseAutomationConfig` 与两处 `GetPiecewiseAutomationConfigs`。
  宿主保留两数据类型 + 两序列化槽，路由处按 `IsPiecewise` 现解析；唯一特判 NaN 的 automation 消费方是 `AutomationDefaultsController`（分段轨不显默认基线行）。

- **合成参数回显升级为只读回显轨**（待办，下一步）：现 `SynthesizedParameters` 是"按 id 叠加到同名编辑轨、借其 config 画白线"。
  拟改为**一等只读回显轨**：`ISynthesisSession` 加 `GetSynthesizedParameterConfigs(IPartPropertyContext) → IReadOnlyOrderedMap<string, AutomationConfig>`
  （回显是分段形、`DefaultValue=NaN`；context 驱动、可预声明 ⇒ 合成前就存在、显隐不抖），`SynthesizedParameters` 退回只承载曲线数据。
  宿主把这些 key 作**可显隐的只读轨**（独立 key 如 `energy`、自带 Min/Max/Color/DisplayText、用各自 config 色画、不可编辑），去掉"叠加到同名编辑轨"逻辑；
  显隐开关考虑放参数区**标题栏**（回显是 voice 级扁平集合，不入分源的底部 tabbar，避免臃肿）。线程契约（数据线程发布、发布即不可变、StatusChanged 单一刷新）补进三个曲线产物成员的注释。

- **phoneme 输出模型小复盘**（待议，独立）：现扁平 `SynthesizedPhoneme[]` + 出身 note 回指。重新审视：越界由 StartTime/EndTime 表达（非结构决定，legacy note→list 同样能表达），故扁平相对 note→list 的**唯一实质差异是无主音素**（Note=null 的自由换气，view-only/不可钉，但正确性上"不参与任何 note 伸缩"）。待定：是否砍掉无主、输出是否改回 per-note。与回显/统一无关，不阻塞。
