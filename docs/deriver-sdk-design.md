# TuneLab Deriver 插件 SDK 设计

> 配套文档：[effect-migration.md](effect-migration.md)（离线整段音频处理与快照纪律参考）、
> [voice-sdk-design.md](voice-sdk-design.md)（产物值类型、快照/隔离纪律）、
> [sdk-api-evolution.md](sdk-api-evolution.md)（加性演化 / DIM 约定 / 冻结面纪律）。
> 本文只详述 deriver 专属面与它区别于其余类型的**本质**，同构机制指向对应文档、不复述。

## 0. 定位与为何要独立类型

Deriver 是**一次性、音频驱动的派生**插件类型：对一段固定音频跑一次离线模型，**从它派生出新的
工程材料**——可以是**提取**出的信息（audio→MIDI 转写、音高、节拍/速度、包络），**分离**出的成分
（声部 stem），或**生成**出的新内容（如变声 Singer A→B）。与 voice / instrument / effect / format 并列。
核心特征：

- **一次性动作，不是常驻推导轨**。用户显式触发一次，产物一旦落地即成为**用户拥有、可自由编辑
  的工程数据**（如新建 `MidiPart` 的音符 / 音高 / automation，或新 `AudioPart`）。它与合成三类
  （voice/instrument/effect）的「输入变则宿主重算、产物永远只读从属于输入」的反应式模型**相反**
  ——绝不监听输入变更自动重跑，否则会无情覆盖用户手改。
- **派生而非取代**：源音频**保留**，产物是**并存的新材料**（新 part / 新轨），不替换输入。这正是
  「deriver」得名之由——区别于 converter 隐含的「A 变成 B、源被丢弃」。
- **输入是固定音频、无时间线**。见 §1：产物一律以**物理时间（秒）**表达，宿主是 tick 网格的唯一主人。
- **产物镜像工程结构、是一袋可选槽**。见 §2.3：一个 deriver 只填自己专精的槽（转写型填音符、
  节拍型填速度、分离型填音频 part…），其余槽为 `null`。

> **命名**：`deriver`（派生）而非 `extractor` / `converter`。`extractor` 暗示「输出本就在输入里」，
> 对**生成型**产物（Singer A→B、变声）不成立；`converter` 暗示「A 变成 B、源被取代」，与本类型
> **源保留、产物派生并存**的事实相反。`deriver` 恰表达派生关系。

### 为何不是「通用 In→Out converter」类型

曾设想按 I/O 模态（audio→audio / text→audio / audio→midi）抽一个通用 `converter<In,Out>` 类型。
**该方案已否决**。原因：各模态的**交互本质**根本不同——

- audio→audio（同时间轴、等长替换的反应式链）已是 **effect**；
- note/text→audio（可指派的反应式声源）已是 **voice / instrument**；
- 文件↔工程 已是 **format**。

一个按 In→Out 抽的通用类型只能抽掉最不值钱的签名部分，而把真正区分它们的重量（反应式 vs 一次性、
产物只读 vs 用户拥有、落哪、怎么 undo、从哪进）压成最小公倍数（全是可选/被忽略的成员）。
**分类的正确轴是「交互模型」，不是「I/O 模态」**：deriver 收敛的是「输入统一为音频、交互统一为
一次性动作、产物统一为可编辑工程数据」这一簇——audio→MIDI、audio→pitch、audio→速度/拍号、
声部分离、变声（产新音频 part）都在簇内；而它们各自的差异只落在**产物槽**上，故一个类型足矣（§2.3）。

### 为何不并进 format 导入

format 是**无参数、无进度、无模型**的文件→`ProjectInfo` 纯反序列化（`IImportFormat.Deserialize(Stream)`）。
deriver 需要参数面板、进度/取消、模型生命周期，且作用于**工程内已有的音频 part**（而非外部文件）。
二者产物都**镜像工程的轨/part 结构**、共享「并入工程」的落地逻辑，但**元素 DTO 不同**（format 是
tick-based `ProjectInfo`；deriver 是物理秒、无工程全局状态的片段，§2.3），且**不共享插件契约**
——正确的接缝：共享结构与落地，不共享单位与交互契约。

### deriver 与 format 的本质分界：谁自带 grid

- **format**：文件里**自带 tempo / grid**（MIDI 文件有 tempo 事件、工程文件有时间线），
  故它吐 tick-based 的 `ProjectInfo` 天经地义。
- **deriver**：输入是裸音频、**无任何 grid**，算法结果天然是物理时间。它既不知道、也不该知道
  工程的 tick 网格——由此推出 §1 的单位纪律。

### 产物种类是槽维度、不是类型边界（音频与符号统一在一个类型）

本类型的产物是**一袋可选贡献**——符号槽（音符/音高/速度/automation）本就异质并存。**音频产物
（分离出的 stem，或生成出的新音频如变声）只是这袋里的另一种槽，不是另一个类型**。理由：

- **一个插件可能同时产音频与符号**（分离人声 stem + 同时转写 MIDI；隔离人声 + 输出其音高曲线供
  retargeting；变声 + 附带音高分析），真实非臆造。按产物种类拆成两类，就把一次本该单一的操作
  硬撕成两个插件。
- **提取型音频与生成型音频在 DTO 层无从区分**：stem（提取）与变声输出（生成）都是同一个音频
  `SecondBasedAudioPartInfo` 槽。故只要音频产物槽在，生成型 audio→audio 就**天然可表达、无法在结构上排除**
  ——这也是命名必须用「派生」而非「提取」的根因。
- **冻结 ABI 反过来支持统一**：类型可加性长成员，但**永远无法事后把两个类型合并**。既然「同产
  音频+符号」「提取+生成」都是真实用例，就该**现在把音频产物槽设计进同一个类型**，而非日后被迫
  （且不可能）合并。
- 故产物种类（音频 vs 符号、提取 vs 生成）是**结果 DTO 的槽维度**（与音符 vs 音高之别同性质），
  不构成类型边界。

### 真正的边界是交互模型——对 effect 依然锋利

即使本类型也能吐音频（含生成型，如变声），它与 effect 仍判然有别（边界从来是交互模型、不是模态）：

| | 本类型（一次性） | effect（反应式） |
|---|---|---|
| 触发 | 用户显式一次 | 输入变则宿主重算 |
| 关系 | **1→N，产新独立 part** | **1→1，同 part 就地** |
| 产物归属 | 用户拥有、可编辑、不从属输入 | 只读、永远从属输入 |

同一个变声模型可有两种交付：作 **effect** 是反应式、就地、随输入重导的只读处理级；作 **deriver**
是一次性**烘焙出独立可编辑的新 part**——是 DAW 里「插入实时效果 vs 烘焙成新轨」的熟悉双模式，
不冲突。音频产物**不会**让本类型塌进 effect。

---

## 1. 单位与时间线（deriver 的定义性纪律）

> **派生产物一律说物理量（秒 / BPM），宿主是 tick 网格的唯一主人，所有 秒↔tick 换算都在宿主侧做。
> 插件既不消费也不生产 tick 时间线。**

### 1.1 为何插件不需要、也拿不到工程时间线

多数 audio→MIDI 算法在**秒**域工作。若强令插件自己把秒转成 `MidiPartInfo` 的 tick，就把每个插件
耦合到 tempo-map 数学，且一旦派生**同时**想提出新速度就会陷入「用哪条 map 换算」的死结。
故插件只吐「音符在 1.23s–1.45s、pitch 60」，宿主拿**当前工程 tempo map** 换算落 tick。
**「插件拿不到工程 timeline」不是缺陷、是特性**——它让插件对 grid 完全解耦。

### 1.2 两种派生对时间线的角色

| 角色 | 例 | 处理 |
|---|---|---|
| **时间线消费者** | 音符/音高转写 | 不让插件消费：插件吐秒，宿主用当前工程 tempo 换算落 tick |
| **时间线生产者** | 速度/节拍检测 | 插件吐**检测到的物理速度图**（秒锚点→BPM、downbeat 秒位），宿主决定是否采纳 |

二者在插件层永不冲突，因为**换算权只在宿主手里**。

### 1.3 「保当前 vs 取产物」的治理时间线策略（宿主侧）

- **默认**：保当前工程时间线；派生的秒基音符按它换算落 tick。
- **若派生还检测了速度图 + 用户在对话框勾了「同时套用检测速度」**：宿主**先装检测到的速度图，
  再拿它换算音符的秒**——音符仍落在音乐意义正确的位置。这一步之所以自洽，正因为音符是秒、
  转换延后到宿主。

---

## 2. 顶层接口

> 命名空间一律平铺 `TuneLab.SDK`；文件建议落 `TuneLab.SDK/Deriver/` 分桶。签名为设计稿，
> 落地前随本文一并审。

### 2.1 `IAudioDerivationEngine`

```
void Init()                              // 懒加载模型，失败抛异常，宿主在调用边界 catch
void Destroy()
DerivationCapability Capability { get; }                             // 声明能产哪些槽（供 picker/对话框过滤）
ObjectConfig GetPropertyConfig()                                     // 参数面板（灵敏度/最短音符/onset 阈值…）
IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs()   // 声明能产哪些具名 automation 轨
Task<SecondBasedProjectInfo?> Derive(AudioDerivationInput input,
                               IProgress<SynthesisStatusSegment> progress,
                               CancellationToken cancellation = default)
```

- **engine 即 deriver**：一个 engine = 一个派生算法（形态近 effect，非 voice/instrument 的
  「一 engine 托多音源」）。engine 身份 id + 显示名经 manifest 声明。
- **无常驻 session**：deriver 是一次性动作，无 peek/commit 调度、无生命周期会话。`Derive` 收一份
  **不可变输入快照** → 返回一次产物；`null` = 取消或什么都没产出。
- **取消**：`CancellationToken` 尽力请求，取消是正常结局（返回 `null`、不抛 `OperationCanceledException`）；
  真正错误才抛异常，宿主 catch。
- **声明与结果分离**：`Capability` + `GetAutomationConfigs` 是声明侧（UI 用）；产物槽是运行时侧（§2.3）。
  结果里的 `Automations` 键必须来自 `GetAutomationConfigs` 声明集。

### 2.2 `AudioDerivationInput`（deriver 专属输入面：冻结、多声道、无变更信号）

```
int  SampleRate { get; }
int  ChannelCount { get; }                                     // 多声道；不下混，交由插件决定
long SampleCount { get; }                                       // 每声道样本数
void Read(int channel, long offset, Span<float> destination)    // 冻结音频随机访问，copy-out 到插件自有缓冲；越界非法
IReadOnlyPropertyObject Properties { get; }                     // 冻结的参数值
```

- **专属面，不复用 effect 的 `IEffectSynthesisAudio`**（此前的开放项 A 已决）。理由：effect 那面是
  **单声道**、是**只读活视图**、且带**内容变更账本 `RangeModified`**（为反应式局部重合成的缓存服务）；
  deriver 是**离线长耗时、输入固定**的一次性任务——不需要变更信号与活视图语义，反而**必须支持多声道**。
  强行共用会把 effect 的反应式包袱塞给 deriver、又把多声道需求压给 effect。故各自独立输入面。
- 宿主在**数据线程**物化本快照（音频 copy-out、参数冻结），再 offload 给 worker 跑 `Derive`——
  与合成三类的快照纪律同源：worker 只读快照，永不回碰宿主活数据。**冻结不可变**：`Derive` 全程
  音频不变，故不带任何 `RangeModified` 之类信号。

### 2.3 `SecondBasedProjectInfo`（独立精简秒基家族）

产物是一个**独立的秒基工程表示家族**（`SecondBased*Info`），与 tick 工程 DTO（`ProjectInfo`/
`MidiPartInfo`/…）**平行、不共类型**。镜像 轨→part 层级（支持多轨、多分片 part、midi+audio 混合），
但：**全程物理秒**、且**只留 deriver 产得出的字段**（丢 gain/pan/color/soundsource/effects/vibrato/
properties/offsets/phonemes 等创作字段——宿主转 tick 并入工程时以默认填）。`Derive()` 返回
`SecondBasedProjectInfo`：

```
sealed class SecondBasedProjectInfo
{
    IReadOnlyList<SecondBasedTrackInfo>? Tracks;                 // 多轨/多分片
    IReadOnlyList<SecondBasedTempoInfo>? Tempos;                 // 工程级时间线（检测型才填）
    IReadOnlyList<SecondBasedTimeSignatureInfo>? TimeSignatures;
    // null = "这项我不产"
}

sealed class SecondBasedTrackInfo { string? Name; IReadOnlyList<SecondBasedPartInfo> Parts; }   // 无 gain/pan/color/…

abstract class SecondBasedPartInfo { double StartTime; }        // 秒（绝对音频内容时间）；host 转 Pos
sealed class SecondBasedMidiPartInfo : SecondBasedPartInfo
{
    IReadOnlyList<SecondBasedNoteInfo>? Notes;
    IReadOnlyList<IReadOnlyList<Point>>? Pitch;                  // 音高曲线，专用通道
    IReadOnlyMap<string, SecondBasedAutomationInfo>? Automations;
    // 无 SoundSource/Effects/Vibratos/Properties/StartOffset/EndOffset —— 创作字段宿主默认填
}
sealed class SecondBasedAudioPartInfo : SecondBasedPartInfo
{
    // 音频交付（stem / 变声）：缓后，机制见 §8（倾向宿主给路径 GetXxxAudioPath）
}

sealed class SecondBasedNoteInfo          { double StartTime; double EndTime; int Pitch; string? Lyric; }  // 秒/MIDI number；无 Properties/phonemes
// Pitch 曲线直接用 IReadOnlyList<IReadOnlyList<Point>>：段升序、互不重叠，段内 Point=(秒, 半音 float)，gap=NaN
sealed class SecondBasedAutomationInfo    { double DefaultValue; IReadOnlyList<Point> Points; }           // Point=(秒,值)；DefaultValue=NaN⇒分段
sealed class SecondBasedTempoInfo         { double Time; double Bpm; }                                     // 秒 → BPM
sealed class SecondBasedTimeSignatureInfo { double Time; int Numerator; int Denominator; }                // 秒 + n/d（形态待定：也可能是重拍图，见 §8）
```

- **为何独立家族、不复用 tick 类型**：结构上其实**只有 `TimeSignatureInfo`（BarIndex 小节制）无法表达秒**；
  其余位置字段都是 `double`、数值可装秒。但若复用 tick 类型、只把 `double` 当秒，则同一 `MidiPartInfo`
  在此处是秒、在 `ProjectInfo` 里是 tick——**类型系统拦不住「秒基喂进 tick 代码」的潜雷**。故取**独立、
  型安全家族**：`SecondBased*Info` ≠ `*Info`，编译期即隔离。代价是并列类型，收益是零单位混淆 + 精简契约。
- **命名**：`SecondBased*` 采 `X-Based` 地道构词（based on seconds），概念上对立 tick-based；与 tick 家族
  `*Info` 平行。
- **中性、可复用给秒基导入**：本家族不含 deriver 私有语义。将来 format 类型的**秒基文件导入**（§0）——
  外源文件在**非 0 落点**导入时，秒→tick 依落点 tempo 而变，故须保秒到宿主、在已知落点转——**可复用
  本表示 + 宿主的 秒→tick@落点 换算**，不必另造一套。
- **坐标系**：所有位置（part `StartTime`、note 时间、Pitch/Automation 的 `Point.X`）**一律绝对音频内容秒**；
  part 内偏移的 tick 重基、part `Pos` 换算全在宿主侧（§1）。
- **Pitch 与 Automation 是两回事**：Pitch 专用音高通道（MIDI 半音、落 `MidiPart.Pitch`）；Automation 通用
  具名轨（落 `MidiPart.Automations`）。两独立成员，不合并。
- **音频 part 与 midi part 平级**（`SecondBasedAudioPartInfo` 是 part 联合一支）——「产物种类是槽维度、
  不是类型边界」（§0）的落地。
- **能力位** `DerivationCapability`（flags：`Notes / Pitch / Automations / Tempo / TimeSignature / Audio`）
  声明「能产什么」，供对话框勾选 / picker 分组；宿主对全空结果 no-op + 提示。

---

## 3. 产物值类型的身份、命名与共享原语

deriver 的秒基音高曲线（`SecondBasedMidiPartInfo.Pitch`）与合成侧的 `SynthesizedPitch` /
`SynthesizedParameter`（`Synthesis/`）**同形**（皆 `IReadOnlyList<IReadOnlyList<Point>>` 段折线），
但**有意不共类型**。同理，整个 `SecondBased*Info` 秒基家族也与 tick 的 `*Info` 家族并列不共类型（§2.3）。

### 3.1 为何不统一

它们只是同形、不同命，至少三条身份分歧：

| | 时间基 | 方向 / 生命周期 |
|---|---|---|
| `SynthesizedPitch/Parameter` | **全局工程秒（绝对）** | 反应式回显、常驻 pipeline、Changed 发布、不持久 |
| deriver 的秒基曲线 | **音频相对秒（位置无关）** | 一次性、transient、用户目录缓存、喂一次 merge 即弃 |

一个 `Point` 在 `SynthesizedPitch` 里是「工程第 12.3 秒」，在 deriver 产物里是「这段音频内第 0.5 秒」
——**两个不同坐标系**。给它们共同基类/类型，恰好抹掉 §1 辛苦显式化的界线，并诱导代码把音频相对点
喂进要全局秒的地方——bug 温床，非 DRY 收益。

`SynthesizedPitch` 与 `SynthesizedParameter` 之所以本身就是两个类（尽管同形），亦是同一原则：pitch 是
固定专属通道（宿主全知其色/量程、将来长 pitch 专属维度），parameter 是动态 keyed（自带 Min/Max/Color），
沿各自轴演进。**在冻结 ABI 上这更是唯一正确解**：一个类可加性长成员，但永远无法事后劈成两个而不破 ABI，
故「今天同形、明天异命」的类型必须一开始就分。

### 3.2 该共享的、共享在正确海拔

- 真正共享的原语是 **`Point`** 与「nested-segments」编码约定（段升序、不重叠、gap=NaN），
  已在原语层共享——这是共享的正确高度，不要再往上抽基类（且违反仓库「值 DTO 一律扁平 sealed、
  不引入继承层级」的房规）。
- **命名传达同族、类型区隔身份**：合成侧用 `Synthesized*` 前缀（反应式引擎回显），deriver 侧用
  `Derived*` 前缀（一次性派生产物），呼应仓库既有命名规范；每个类型注释钉死其**时间基**，作防混
  坐标系的第一道防线。

---

## 4. 结果生命周期与缓存

### 4.1 工程里只认落地的 part

派生结果 `SecondBasedProjectInfo`（物理量、相对音频自身时间基）**不存进工程**。工程里认的唯一真相
是**落地后的 part**（可编辑、可撤销、可移植），与任何普通 part 无异。**不存 provenance 链**
（「此 part 源自派生那段音频」）——一致于一次性、非反应式、干净 schema。part 与其派生结果的关联
**按内容 hash 现算**（hash part 内容 → 查缓存），无需在工程里存链或缓存条目（§4.3 / §5.7）。

### 4.2 两阶段拆分

- **阶段一（贵，模型）**：`AudioDerivationInput`（run-inputs）→ `SecondBasedProjectInfo`。**缓存这个**（§4.3）。
- **阶段二（廉价，宿主侧）**：`SecondBasedProjectInfo` + apply-inputs（§5.2：治理时间线/过滤/落点）→ 新 part。
  **不缓存**，每次应用按当前工程状态重导。

### 4.3 缓存规格（位置 / 形态 / 键 / 一 part 多结果）

- **位置**：`%APPDATA%\TuneLab\` 下的**派生缓存目录**（宿主内部，非工程、非 Settings；照
  `RecentSoundSourceManager` 的宿主记忆范式，但为目录）。per-user、**内容寻址** → 同音频跨工程命中。
- **形态**：索引（JSON/manifest）+ blob。符号产物（notes/pitch/automation/tempo）体积小、可入索引或
  紧凑二进制；**音频产物（stem/变声）是大 PCM，存为独立音频文件、索引只存引用**——JSON 装不下 PCM。
  **有界 + LRU/容量上限淘汰**（缓存可弃，淘汰不损正确性）。
- **键 = run-inputs 身份**：`hash(派生区间 PCM) + engineId + 插件 manifest version + run-参数 hash`。
  - 派生区间被内容 hash **蕴含**（hash 覆盖恰好那段样本），不必单列；
  - **只含 run-inputs、不含 apply-inputs**（治理时间线/过滤/落点只影响廉价 stage-2、可重选，§5.2）；
  - **模型版本位取插件 manifest 的 `version`**（无需新 SDK API、发布即变、作者不会漏 bump），令旧
    模型算出的结果不被误服用；代价是非模型改动的发布也会失效缓存（over-invalidate，但缓存可弃、只多
    一次重算，安全）。若日后 over-invalidate 成痛点，再考虑引擎自报的精确 `CacheVersion` token（精确
    但增 API、且依赖作者记得 bump——漏 bump 会服用旧模型的错结果，故不作默认）；
  - hash 在提交 copy-out 时**顺带算**（反正要拷样本），查表在跑之前——命中即跳过模型；
  - v1 `requestedOutputs` 不入键（产物皆廉价、全算）；将来有贵产物（stem）再入键或用超集匹配。
- **一 part 多结果、多任务并发**：键是**（内容+engine+参数）、不是 audio-part 身份**，故同一 part 可有
  **多个共存条目**（不同 engine/参数/任务），也可**同时跑多个任务**（各独立后台任务、各按自己的键
  读写，互不冲突，§5.6 并发）。内容相同的不同 part 甚至共享同一条目。若按 part 身份做键则只能存一个
  ——故**必须内容寻址**。

### 4.4 位置无关与「移动后重应用」

- **位置无关（三层保证）**：①输入在提交时**冻结拷贝**（§5.3）——run 不看位置；②键是**内容 hash 非
  位置**——移动 part（只改 Pos）→ 同 hash → **必命中**；③落点在**应用时**按源当前位置解析（§5.5）——
  落到新位置正确。故**分析中移动音频，产物依旧有效、缓存命中、落位自适应**。
- **内容变了才失效**：不同 hash → miss → 重跑（正确——要新内容本就该重跑）。运行中的任务用提交时
  快照，其结果缓存在**提交时内容**的键下（§5.6 解耦）。
- **「重新应用」= 重触发**：用户再触发一次派生 → 阶段一命中缓存 → 待应用 → 应用（按当前位置）；
  非独立机制、无独立 UI 状态。
- 缓存是**可弃优化、非正确性依赖**：清空/换机器 → miss → 重跑一次，不影响已落地的 part。
- 因「总是新建 part」（§5），重派生 = **再产新 part**、不覆盖旧的——旧的（可能已手改）原封保留。

---

## 5. 交互与调用流程

### 5.1 入口与对话框

- **入口**：右键工程内音频 part → 该 engine 的动作项（任务专属文案，如「提取为 MIDI…」「分离声部…」
  「变声…」）；拖音频文件进空轨时的「作为音频导入 / 派生…」二选一。可只对**范围选区**派生。
- **对话框**：engine 选择器 + `GetPropertyConfig` 参数面板 + 按 `Capability` / `GetAutomationConfigs`
  的可选产物勾选。对话框**收完入参即关闭**，不在运行期占用（运行是后台非阻塞的）。

### 5.2 调用入参：run-inputs（缓存键）vs apply-inputs（可重选）

对话框收集的入参分两组，分界线**正是 §4.2 的缓存键边界**：

| 组 | 内容 | 归属 |
|---|---|---|
| **run-inputs** | `engineId`、源音频+区间（→copy-out 冻结样本）、引擎参数（→`Input.Properties`）、`requestedOutputs`（`Capability` 子集，尽力跳过贵产物的提示） | 冻结进 `AudioDerivationInput`，**是缓存键**，喂给引擎（stage 1） |
| **apply-inputs** | 治理时间线选择（§1.3）、落地过滤（并入哪些槽/轨）、落点策略 | 宿主 stage-2 消费，**非缓存键**，可随时重选而不重跑 |

改 apply-inputs → 只重跑廉价 stage-2（命中缓存）；改 run-inputs → cache miss、重跑模型。
这把「重新应用 = 重触发、缓存兜底」（§4.3）落成精确机制。

### 5.3 提交：冻结输入 + 与源解耦（工程零改动）

**提交任务的那一瞬**（数据线程）只做两件事，且**不改动工程**（不压任何 undo 命令）：

1. **冻结输入快照**：copy-out 源音频区间 + 冻结参数 → `AudioDerivationInput`。
2. **与源解耦**：任务此后**与源再无关系**——源被移动/编辑/删除都**不影响本次运行的结果**（无「陈旧」
   概念）；要新内容就发起新任务（新快照，可能 cache miss）。

> 曾设想「提交即压入延迟 undo 命令」（submit-anchor），**已废弃**：它把「插入 track」这一共享结构
> 变更追溯到更早的历史位置，而运行期间记录的 track-move 等**基于 index** 的命令是按「尚无该新 track」
> 的列表编码的，完成时真插入会使其 index 全部错位（无法在不 rebase 后续命令的前提下修）。故改为
> **完成后经显式应用、在栈顶落地**（§5.4）。

### 5.4 完成 → 待应用 → 显式应用（落地时机与 undo）

- **执行**：后台非阻塞任务，进度显示在目标位置（复用 `SynthesisStatusSegment` 词汇：
  `Pending / Synthesizing / Failed / Progress[0,1]`）。**取消 = 进度 UI 取消按钮**；取消即丢弃任务，
  工程从未被改、无 undo 可虑。
- **待应用态**：完成时结果进入**「待应用」**（可预览），**工程仍零改动**。此时才敲定 apply-inputs
  （§5.2：治理时间线 / 落地过滤 / 落点），可在预览下调整。
- **显式应用**：用户点「应用」→ worker 结果 **marshal 回数据线程** → stage-2 换算 → 作**一条普通栈顶
  undo 命令**插入**新 track/part**（§2.3 apply）。源音频 part 保留作参考。
- **为何这样最稳**：
  - 应用前工程零改动，用户在别处的操作与 undo 栈**完全不受打扰**——根治「自动落地后误撤回」的
    surprise（这正是 submit-anchor 想解决、却因 index 问题失败的目标，此处以「不提前落地」干净达成）。
  - 应用是**普通栈顶命令**：运行期间记录的 track-move 等 index 命令**先记录、其 index 正确**，本命令
    后压、不追溯、不错位。撤回 = 移除刚插入的新 track（隔离新数据，干净）。
  - 「完成但未应用」永不落地：用户可晾着，关工程也不影响（结果仍进内容 hash 缓存，下次秒出）。

### 5.5 落点解析与无-track 产物

- **垂直（落哪条轨）**：**应用时**按**源轨身份**解析（非冻结 index）——插在源轨**当前**位置之下；
  多轨产物（如 stems）作连续块插入。**不记录插入 index**：index 是对可变轨列表的位置引用，提交到
  应用之间用户增/删/移轨会使它失效；身份引用则对其他轨的增删移天然免疫。
- **水平（落哪个时间）**：按源 part 当前 Pos（§5.6「移动」行）。
- **源轨被删**：回退——追加到轨列表末尾 + 提醒（与 §5.6 源删除一致）。
- **无-track 产物**（如纯节拍/速度检测）：无轨可插、落点无意义。这类产物是**工程级时间线合并**
  （Tempos/TimeSignatures），**不是隔离新数据**——故：
  - 它**不享有** §5.4 的「隔离新 track」保证：合并会**改动共享的工程时间线**、可能与用户运行期间的
    tempo 编辑冲突。
  - 按 §1.3 治理策略：**仅在用户显式勾选**「套用检测速度/拍号」时才合并（否则只可用、不落），
    合并作一次可撤销的时间线变更（可能覆盖并发编辑，undoable）。**不静默合并共享状态。**
- **空结果**（什么都没检出）：no-op + 提醒。

### 5.6 边界情形

| 提交→应用之间发生 | 靠哪条决策化解 | 应用时行为 |
|---|---|---|
| 源音频**移动** | 位置无关（§4.3） | 按源**当前** Pos 落位 |
| 源音频**内容被改** | 提交即解耦（§5.3） | **无关**——本次是提交时快照的忠实结果；不检查、不阻拦 |
| 源音频**被删除** | 结果自包含，仅缺落点锚 | **仍能应用** + 回退落点（源最后位置或 playhead）+ 提醒「源已删」 |
| 目标轨/结构变了 | **总是新建轨/part** + 身份落点（§5.5） | 不依赖既有目标/index，天然健壮 |
| 工程 tempo 变了 | 换算在 stage-2、应用时算 | 用**应用时**的当前时间线换算 |
| 运行/待应用期 undo/redo | 应用前工程零改动（§5.4） | 与本任务无关——无命令可撞 |

**并发**：多个派生各是独立后台任务、各自落新 track，互不冲突；缓存去重相同 run；取消 →
`Derive` 返回 `null` → 不落地。

### 5.7 任务状态与呈现面（多任务、移动、删除）

- **两类状态，都不进工程**：
  - **缓存条目**（完成的 stage-1 结果）：持久、`%APPDATA%` 内容寻址（§4.3）。
  - **运行中/待应用任务**：**宿主运行时**任务管理器持有，**会话态、不持久**（关工程即弃）——但结果
    已在缓存，重触发秒出，不算丢失。
- **part → 其结果的关联是「算出来的」**：给定 AudioPart，hash 其内容 → 查缓存该 hash 的所有条目
  （跨 engine/参数）即得。故 UI 能显示「此 part 有 N 个缓存结果 / 在跑的任务」而工程 schema 零负担
  （呼应 §4.1 不存 provenance）。
- **呈现面**：
  - **中央派生任务面板（权威面）**：列出所有任务（运行中/待应用/失败），逐条 status + 动作
    （运行中→取消；待应用→应用/丢弃）。**位置无关**，天然扛住「多任务」「part 移动」「part 删除」。
  - **on-part 徽标（便利面，可选）**：按**源 part 身份**锚定 → part 移动则徽标**跟随**到当前位置（与
    §5.5 落点同理，身份非位置）；多任务显示**计数**、点开过滤到该 part；**part 被删则徽标消失，但
    面板条目仍在**（应用走 §5.6 回退落点）。
- **多待应用/多运行**：任务管理器并行持有多条；同一 part 的多个待应用各自应用 → 各产**独立新 part**
  （§5，每次应用一个新 part，互不冲突）。
- v1 任务态**会话内存活**；跨重启持久化任务队列属**缓后**（结果本就在缓存、可重触发恢复）。

---

## 6. 加性演化与已知边界

### 6.1 结构型 vs 曲线/属性型产物（v1 落点边界）

| 产物 | 类别 | v1 落点 | 理想落点 |
|---|---|---|---|
| Track / Part（midi + audio）/ Tempos / TimeSignatures | **结构型**（建新工程数据） | 新建轨/part、速度图 | 同左，完美契合「总是新建」 |
| Part 内的 Pitch / Automations / Lyric | **曲线/属性型**（天然想套进已有 part） | 落进**新建 part 的对应槽**（可能是只含曲线、无音符的新 part，可用但略奇怪） | **retargeting 到用户指定的已有 part**——推迟专题 |

**已知边界**：逐音符属性/曲线抽取（歌词回填到已有音符、音高/包络重定向到已有 part）是**修改已有
用户数据**，不吻合「产新片段」模型，与「总是新建」冲突，故 **v1 推迟**。retargeting 通道做成后，
曲线型产物才能落到用户指定的已有 part 上。automation / pitch / lyric 现在就进 DTO 与声明面**没问题**，
只是「理想落点」等 retargeting——本表即其显式契约，不含糊带过。

### 6.2 加性演化纪律

- **宿主实现、插件只读的面**（`AudioDerivationInput`）：加成员纯加性、连旧插件二进制都不受影响。
- **插件实现的面**（`IAudioDerivationEngine` / 产物 DTO 供插件构造）：产物 DTO 新增可空槽是加性的
  （旧插件不填即 `null`）；引擎接口新增成员一律用**默认接口方法（DIM）**兜底。详见
  [sdk-api-evolution.md](sdk-api-evolution.md)。

---

## 7. 宿主集成

- **注册**：manifest 新增 `type="deriver"`；`ExtensionManager.RegisterEntry` 加 `case "deriver"`，
  `IsCodeKind` 纳入；新增 `DeriversManager`（多包并存、惰性 `Init`）。
- **冲突路由**：`ExtensionRouting` 新增 `"deriver"` 行，多包同 engine id 并存、用户在「Extension Routing」
  矩阵选活实现，选择存 `Settings.json`。
- **落地命令**：宿主拿 `SecondBasedProjectInfo` → 定治理时间线（§1.3）→ 秒→tick 换算 → 建新 part
  （part 锚点、Pitch 相对 X 等 tick 细节全在宿主侧算，插件零参与）→ 一条 undo 命令并入当前工程。
- **缓存**：`AudioDerivationCacheManager`（用户目录 JSON，内容 hash 键，§4.2）——宿主内部模块，非插件面。

---

## 8. 待办与缓后

- v1：audio→MIDI（音符 + 音高）转写；可选 automation / 速度 / 拍号检测（按 `Capability` 声明）；
  总是新建轨/part；用户目录内容 hash 缓存。
- 缓后（同一类型、加性）：音频 part 产物（声部分离 stem、变声等生成型音频）、retargeting 通道
  （曲线/属性型产物落已有 part，§6.1）、派生结果落位量化（吸到工程网格）。
- 已决：①类型名 `deriver`（派生非取代，§0）；②专属输入面 `AudioDerivationInput`（冻结/多声道/
  无变更信号），不复用 `IEffectSynthesisAudio`（§2.2）；③调用流程：提交时仅**冻结输入 + 与源解耦、
  工程零改动**（`submit-anchor` 延迟命令**已废**——会使运行期 index 型 move 命令错位）；完成后进
  **待应用态**，用户点「应用」才作**普通栈顶 undo 命令**落地——根治 surprise undo 且 move 命令 index
  正确（§5.3–5.4）；源删除仍能应用 + 回退落点提醒。④落点**应用时按源轨身份解析**（非 index），多轨
  产物连续块插源轨之下、源轨删则末尾追加；无-track 的时间线产物按 §1.3 opt-in 合并、非隔离数据（§5.5）。
  ⑤秒基产物族 = **独立精简 `SecondBased*Info` 家族**（型安全、非复用 tick 类型、中性可复用给秒基导入，§2.3）。
- 缓后 / 倾向已定：音频产物交付机制——**排除**「插件自选路径」；**倾向宿主给路径**（`GetXxxAudioPath`，
  宿主控缓存位置）——但属**缓后**（v1 不产音频、`SecondBasedAudioPartInfo` 先只占位无负载字段，加性补），
  实现音频输出时再定死。
- 相关前置（已提交，独立于 deriver）：`ProjectInfo` 剥离 editor/export（`release/2.0.0`，commit `3bf6d68`）——
  `ProjectInfo` 现为纯 musical tick 契约，与本秒基家族互为 tick/秒 姊妹表示。
- 参考实现：`tests/plugins` 下补一个最小 deriver 样例（如基于自相关的单声部 pitch→note 转写）
  作为 AI 参考与回归夹具（随实现补该类型接口的专属 AI 参考文档）。
