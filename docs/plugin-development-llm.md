# TuneLab 插件开发 · 面向 AI 的参考

> 本文为**喂给 AI 助手**的结构化事实清单，用于生成正确的 TuneLab V1 插件。
> 人类可读版见 [plugin-development.md](plugin-development.md)。

---

## 一句话提示语（复制给你的 AI 助手）

> 你要为 TuneLab 编写一个 V1 插件。插件是一个文件夹，根目录必须有 `manifest.json`（其 `id` 字段是新版判别标志，必须有，用反向域名）。代码插件类库目标框架锁 `net8.0`，**只引用** `TuneLab.Foundation` 与 `TuneLab.SDK.*`（绝不引用 `TuneLab.Hosting.Foundation` 或主程序），且 SDK 程序集不随包分发（宿主共享）。**插件身份写在 manifest、不用 attribute**：每个能力条目用 `class`（**唯一**入口类全名）指明实现类，宿主校验它实现了本 `type` 所需接口、不匹配即加载错误（不猜、不扫候选）——`format` 的方向由后缀字段决定：声明了导入后缀就要 `IImportFormat`、导出后缀就要 `IExportFormat`、两个都声明就两个都要；voice 要 `IVoiceSynthesisEngine`、instrument 要 `IInstrumentSynthesisEngine`、effect 要 `IEffectSynthesisEngine`（对整段音频做离线变换），身份是 `engine`。入口类需有**无参构造函数**。每个能力条目还需 `type` 与 `assembly`（含这些类的程序集）；含代码时顶层 `sdk-version` 必填。严格按下方《事实清单》的 schema 与接口签名生成，不要臆造 API。

---

## 事实清单

### 包结构
- 插件包 = 一个文件夹；根目录必须有 `manifest.json`。
- 发布格式 `.tlx` = zip，`manifest.json` 在 zip 根目录。
- 私有依赖（第三方/原生库）放进包文件夹随包分发；SDK 程序集**不要**放进包。

### manifest.json schema
包级字段（顶层）：
- `id` (string, **必填**) — 唯一标识，反向域名。**有 id ⇒ V1**。
- `name` (string, **必填**) — 展示名。
- `version` (string) — semver，默认 `"1.0.0"`。
- `author` (string)、`description` (string) — 展示在扩展侧边栏：`author` 显示在卡片上，`description` 在卡片悬浮 tooltip 里（详情窗头部也有）。`description` 讲的是**整个包**；具体某个能力是什么，写在该条目自己的 `introduction` 里；这句绝不顶替它。
- `icon` (string, 选填) — 包内相对路径的图标，位图（`.png`/`.jpg` 等）或矢量（`.svg`）均可，在侧边栏卡片**原样展示**（宿主不加背景/不裁圆角，圆角与透明由图标自定）；建议方形（≥64×64）。省略则用名称首字母占位。
- `sdk-version` (string, 含代码插件**必填**) — 宿主校验「插件要求 ≤ 宿主提供」。写你实际用到的那一档：`"1.0"`（TuneLab 2.0.0 起，V1 初始面）；`"1.1"`（TuneLab 2.1.0 起，新增 `PathPickerConfig`/`FileTypeFilter` 与 `TextBoxConfig.WithMultiline`）；`"1.2"`（尚未发布：`TagConfig`/`TagItem`、`ButtonConfig` 与 `IDialogService`/`IProgressReporter`、声明失效 `InvalidatePart`/`InvalidateNotes` 及 `PartId`/note `Id`、`+` 菜单与下拉的分组/`Separator`；以及实验中的画布 `CanvasConfig` / `TimelineCanvasConfig` 一族）。写高了只会被老宿主跳过，换不来任何东西。

插件级字段（一个条目 = 一个具体能力，身份内联）。单插件写在顶层；多插件放进 `extensions[]` 数组的每个元素：
- `type` (string, **必填**) — `"format"` | `"voice"` | `"instrument"` | `"effect"` | 资源类（如 `"voicebank"`）。**方向不进 type**：format 恒为一个值，读写各认哪些后缀由下面三个字段说。宿主不认识的 type 若还声明了 `assembly`/`class`，会被判为「本宿主不支持的插件类型」而跳过。
- `engine` (string, voice/instrument/effect **必填**) — 引擎类型 **id**（唯一、**不可变**、写进工程序列化，绝不本地化）。
- `suffixes` (string[], format) — 「两个方向都认这些后缀」的**简写**（不带点，如 `["mid","midi"]`），等价于 import-suffixes 与 export-suffixes 同时取此值。与下面两个**互斥**。
- `import-suffixes` / `export-suffixes` (string[], format) — 各方向认的后缀。**没声明的方向就是不存在**（只导入/只导出就这么写）。不对称也在这写：`import-suffixes:["mid","midi"] + export-suffixes:["midi"]` = 读两种只写一种，仍是**一个**条目、一份实现、一个设置桶。至少一个方向非空。
- 多个后缀是**别名**：共用本条目的实现类、全部说明与那一份设置；但注册与路由仍**逐(方向,后缀)独立**（用户可让别的包接管其中一个）。真的两种不同格式 → 写两个条目。
- `name` (string, 选填) — **显示名**（UI 用，可与 id 不同、可翻译）；省略则 UI 退回显示 id。
- `introduction` (string, 选填) — 包内相对路径，指向一份 markdown **介绍**（能干什么、怎么开始用、有什么坑）。宿主在扩展详情窗渲染它，agent 按需拉取（`get_extension_introduction`）。**强烈建议写清每个字段的含义与取值**（结构化值必写，如画布字段的值形状）——agent 只能从这里得知。
- `localizations` (object, 选填) — 按语言覆盖 `name` / `introduction`，如 `{ "zh-CN": { "name": "增益", "introduction": "Introduction.zh-CN.md" } }`。`introduction` 的语言变体就走这里（各语言可指不同文件名），**没有** `<base>.<lang>.md` 那种隐式后缀约定。
- `class` (string, 含代码**必填**) — 本条目**唯一**入口类的全名。宿主只校验、不扫描：voice→`IVoiceSynthesisEngine` / instrument→`IInstrumentSynthesisEngine` / effect→`IEffectSynthesisEngine` / format→**它声明的每个方向所需的接口**。需无参构造函数。**一个条目 = 一个实现类 = 一份 introduction = 一份扩展设置**；两份实现写两个条目，且**同一个类不得出现在两个后缀相交的条目里**（加载错误）。
- `assembly` (string, 含代码**必填**) — 含该入口类的单个程序集（相对包根）；资源包不写。
- `platforms` (string[], 选填) — 如 `["win","osx","linux"]` 或 `["win-x64"]`；空=全平台。
- **身份 id vs 显示名**：`engine`/`suffixes` 是身份（注册键 + 序列化引用，不可变）；`name`/`localizations` 仅 UI 展示、可改可译。
- **`introduction` 是条目级、也是你唯一需要写的说明**：AI 要的"一句话摘要"由它自己从这份全文提炼，不必你另写一行（你不清楚模型要什么，写出来多半成了产品文案）。多能力包请给每个条目各写一份。
- **不写 introduction 的后果**：宿主【不会】拿包级 `description` 冒充它。agent 只会看到"该能力没有自己的介绍"，外加一句**明确标注为降级参考**的包级自述——并被告知那讲的是整个包、不等于这个能力。于是 AI 对你插件的了解仅止于一个名字，也就不会主动推荐它。
- **README 不是元数据**：宿主只认 manifest 声明的 `introduction`。包里的 `README.md` 是你留给仓库读者的文件（build 步骤、license、贡献指南），宿主不读、不展示——两者受众不同，不必也不该复用同一份。

规则：
- 有 `extensions[]` → 以它为准，顶层身份字段忽略。
- 无 `extensions[]`（简写）→ 顶层身份字段即那唯一插件；此时顶层 `name`/`localizations` **同时**是包名与该条目显示名（共用，**不要写两个 `name`**——同对象重复键会互相覆盖）。要让包名与条目显示名各不相同，改用 `extensions[]`、给条目单独写 `name`。
- 一个程序集多引擎/格式 → `extensions[]` 逐条列（同 `assembly`、各自 `engine`/`suffixes` + `class`）。
- 身份在 manifest 单一真相，代码里**不写 attribute**；宿主校验 `class` 实现了本 `type` 所需接口后实例化，不扫描、不推断。
- 无 `id` 的 manifest（或无文件）= **Legacy**，不要按此生成新插件。

### 工程配置
- `<TargetFramework>net8.0</TargetFramework>`（固定）。
- 引用：`TuneLab.Foundation`、`TuneLab.SDK`（单一程序集覆盖 format/voice/effect 全部插件类型）。
- **禁止**引用 `TuneLab.Hosting.Foundation`、`TuneLab`（主程序）、或任何 `TuneLab.Extensions.*`（那是 Legacy）。
- SDK 引用 `Private=false`（不复制输出、不随包分发）。

### Format 接口（命名空间 `TuneLab.SDK`）
```csharp
public interface IImportFormat { ProjectInfo Deserialize(Stream stream); }
public interface IExportFormat { void Serialize(Stream output, ProjectInfo info); }  // 宿主给流、插件写入（勿 Dispose/Seek）
```
- 扩展名与实现类在 manifest。**一个类**（无论读写还是单向）→ 一个条目：`{ "type":"format", "suffixes":["ext"], "class":"Ns.MyFormat", … }`；不对称就 `{ "import-suffixes":["mid","midi"], "export-suffixes":["midi"], … }`；只导入就只写 `import-suffixes`。**两个类** → 两个条目，各写各的 `class` 与方向后缀（各自的说明与设置桶）。后缀不对称**不是**拆条目的理由。
- 工程模型在 `TuneLab.SDK`（`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…）。实现类需无参构造函数。

### Voice 接口（命名空间 `TuneLab.SDK`）
> voice = **会话托管厚模型**：每种引擎一个 `IVoiceSynthesisEngine`；宿主为每条 MidiPart 建一个 `IVoiceSynthesisSession`，
> 引擎承担声明（自动化轨/回显轨/属性面板，纯函数、不依赖会话）；会话承担默认歌词 + 逐步合成 + 产物（音高/回显/音素/音频/状态）。
> 时间量一律**全局秒**（tick 不外露）。
```csharp
public interface IVoiceSynthesisEngine {
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; }   // 声库目录；须立即返回不阻塞（Init 期缓存）
    IReadOnlyList<VoiceSourceLayoutItem> VoiceSourceLayout => [];            // 选择器分组树（可选,DIM []=平铺）；节点=Voice(id)|Group(name,items),可交织/嵌套；未覆盖 id 宿主顶层兜底
    void Init();                                          // 无参；包目录经 Assembly.Location 自定位；失败抛异常
    void Destroy();
    IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context);   // 每 part 一个会话；声库烘在 context.VoiceId、不单列；context 随会话同生死
    // 声明均为 f(voiceId, part 当前值) 的纯函数（不碰会话运行时状态；voiceId 经 part 视图：note 面 context.Part.VoiceId / part 面 context.Parts[i].VoiceId）；宿主在参数 commit 时重算并 diff 到 UI（轨/控件可随参数显隐）。
    // 置于引擎而非会话：宿主在【建会话之前】即可求出声明，于是会话构造期 context.Automations 已含自己声明的轨（TryGetValue/枚举均可，无构造期时序死环）。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context);            // 自动化轨（连续/分段同一 map，DefaultValue=NaN ⇒ 分段）
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context);  // 只读回显轨声明（分段形 DefaultValue=NaN）
    ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context);
    // per-phoneme 自定义属性声明（**required**，与 GetNotePropertyConfig 一样必须实现）。**复用 note 声明上下文 IVoiceSynthesisNotePropertyContext**（不再有独立 phoneme context）——每个 IVoiceSynthesisNoteView 带音素双列表 LeadingPhonemes / BodyPhonemes。返回**按核相对 slot 键控**的 schema map：键=slot（0=核、<0=引导辅音、>0=核后；口径=PhonemeSlots：slot=下标−LeadingPhonemes.Count），值=该角色在整个选区上的 schema。多选合并归引擎（schema 依赖当前值时自行对同 slot 各成员值三态 Merge，同 GetNotePropertyConfig 契约）；宿主只按 slot 并值。空 map=所有音素无属性；缺席 slot=该角色无属性。voice-only。
    IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context);
}
public interface IVoiceSynthesisSession : IDisposable {
    string DefaultLyric { get; }   // 新建 note 默认歌词（会话级运行时取值；声明类 config 全在引擎上）
    // 调度（宿主驱动逐步合成）：peek 窗内下一待合成块（纯值边界、无副作用、null=窗内无待合成）→ 宿主调 SynthesizeNext 合成该块。
    SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime);  // 返回纯调度提示 SynthesisRange
    Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default);  // 入参=选中它那次 peek 的同一窗口（按它确定性重导出同一块，非回灌 SynthesisRange）；纯 Task：取消正常返回不抛 OCE；错误抛异常
    // 产物（数据线程发布、发布即不可变、StatusChanged 单一刷新信号）：
    SynthesizedPitch SynthesizedPitch { get; }                                 // 具名富类型 { Segments }；空=new(){Segments=[]}
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }  // 回显曲线数据，key 对齐 GetSynthesizedParameterConfigs
    IReadOnlyMap<string, SynthesizedSyllable> SynthesizedPhonemes { get; }  // 键=归属 note 的 Id（snapshot.Notes[i].Id，值语义、worker 可持有）；值=SynthesizedSyllable{LeadingPhonemes,BodyPhonemes,BodyOffset}；只报时长、无绝对位置；无主音素无契约
    IReadOnlyList<SynthesisStatusSegment> Status { get; }                         // 按段状态/进度/报错
    IActionEvent StatusChanged { get; }                                       // 产物/状态有更新（任意线程触发，宿主 marshal）
}
public interface IVoiceSynthesisContext {        // 会话级输入活视图（宿主实现、随会话死、仅数据线程访问）
    string VoiceId { get; }   // 选中声库（VoiceSourceInfos 的键）；烘在 context 故 CreateSession 不收它
    string PartId { get; }    // part 运行期身份（= IVoiceSynthesisPartView.PartId；对象存活期间恒定、跨会话重建不变、不持久）
    IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> Notes { get; }   // 可重叠（和弦）、排序 StartTime↑→EndTime↓→插入序；去重叠是插件责任
    IReadOnlyNotifiablePropertyObject PartProperties { get; }
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }
    ISynthesisAutomation Pitch { get; }             // 绝对约束（分段：有值=钉死、NaN=自由）
    ISynthesisAutomation PitchDeviation { get; }    // 加性偏差（连续、默认 0、永不 NaN；vibrato 等汇于此）。finalPitch=resolve(Pitch)+PitchDeviation
    VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes);  // 仅 SynthesizeNext 同步前缀（offload 前）主动拉，可拉多份；automation/pitch 全量冻结不开窗（真实采样范围依赖音素时长、前缀无从圈窗）
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);             // 申请音频段握柄（全局轴 long、段内 int）
    IActionEvent Committed { get; }            // 逻辑编辑收口（单条编辑也补发）；廉价标脏、此处一次性做重活
    void InvalidatePart();                     // 声明失效：本 part 全部声明过期（向下级联到 note）；任意线程、宿主合拍
    void InvalidateNotes(IEnumerable<string> noteIds);   // 声明失效：这些 note 的声明过期（不向上级联）；见下"声明失效"条
}
public interface IAudioSegment : IDisposable {   // Dispose=删段（语义整改换新时重建，下游身份随之重建）
    void Write(int offset, ReadOnlySpan<float> samples);   // 段内就地写（span 借用语义）；offset=段内相对采样位置；宿主记账写入区间传导下游
    void Commit();                                         // 送 effect 的唯一闸门；Commit 前的写只供进度/波形
    void Resize(long sampleOffset, int sampleCount);       // 内容延续的扩展/裁剪：身份不变（下游缓存存活）；交集内容按绝对位置保留、新增清零、回未提交态（写完新增区重 Commit 收口）
}
```
快照与产物类型（`SynthesizeNext` 同步前缀物化、worker 只读；全是不可变值、可跨线程）：
```csharp
public sealed class VoiceSynthesisSnapshot {                    // context.GetSnapshot(notes) 的返回体
    IReadOnlyList<VoiceSynthesisNoteSnapshot> Notes { get; }    // 与递入 notes 【索引对齐】（产物归属契约）；邻居按索引导航、无 Next/Previous
    SynthesisAutomationSnapshot Pitch { get; }             // 绝对音高约束（分段：有值=钉死、NaN=自由）
    SynthesisAutomationSnapshot PitchDeviation { get; }    // 加性偏差（连续、默认0、永不NaN）
    PropertyObject PartProperties { get; }                 // part 参数值拷
    IReadOnlyMap<string, SynthesisAutomationSnapshot> Automations { get; }  // 声明过的可编辑轨
}
public sealed class VoiceSynthesisNoteSnapshot {                // 触底值类型、无活引用
    string Id { get; }                                     // 运行期身份 = SynthesizedPhonemes 产物 map 的键
    double StartTime { get; } double EndTime { get; }      // 全局秒
    int Pitch { get; } string Lyric { get; }   // Lyric = 歌词原文 或 用户显式发音覆盖；宿主不归一到单一音系（汉字可能原样到达），G2P 归引擎
    // 无延音字段（live/快照都没有）：延音判定权完整归引擎（IVoiceSynthesisSession.IsContinuation，见下"延音判定"行）；快照域需要身份就在同步前缀对 live note 自判后随自有快照冻结
    IReadOnlyList<VoiceSynthesisPhonemeSnapshot> LeadingPhonemes { get; } BodyPhonemes { get; }  // 钉死音素双列表（引导/主体）；非钉死(G2P)note 皆空。元素=带属性的 VoiceSynthesisPhonemeSnapshot。刻意不设合并的 Phonemes 投影（消费方自行拼接）
    double BodyOffset { get; }                             // 主体起点(结合线)相对 note 头的有符号偏移；junction=noteStart+BodyOffset
    PropertyObject Properties { get; }                     // per-note 参数值拷（GetDouble/GetBoolean/GetString 读，稀疏、读不到取默认）
}
public readonly struct VoiceSynthesisPhonemeSnapshot {     // 合成快照里一个钉死音素的冻结表项（几何字段平铺 + per-phoneme 属性值快照）
    string Symbol { get; }                                // 几何字段平铺直读；喂 PhonemeLayout.Resolve 时按字段重建 SynthesizedPhoneme
    double Duration { get; }
    double StretchWeight { get; }
    PropertyObject Properties { get; }                     // 该音素经 GetPhonemePropertyConfigs 声明的属性冻结值；未声明/未设=PropertyObject.Empty（GetDouble/GetBoolean/… 读，稀疏取默认）
    // 引导/主体归属由所属列表给（VoiceSynthesisNoteSnapshot.LeadingPhonemes/BodyPhonemes + BodyOffset），不落每音素标志
}
public sealed class SynthesisAutomationSnapshot { IAutomationEvaluator Evaluator { get; } }
public interface IAutomationEvaluator { void Evaluate(IReadOnlyList<double> positions, Span<double> results); }  // positions=查询位置（一维；轴由求值器提供方定、当前均全局秒；须非降序）；调用方备 results 缓冲（等长、写入）；旧式返数组用扩展 evaluator.Evaluate(positions)。插值在宿主侧；连续轨永不NaN、分段轨段间NaN

// 音素描述符：进/出同型、方向无关；只报时长，位置/去重叠/melisma 全归宿主派生（引擎报绝对位置会让相接判据失真）。
public readonly struct SynthesizedPhoneme {                // readonly struct + init 属性（非可变裸字段）
    string Symbol { get; init; }
    double Duration { get; init; }        // 辅音(w=0)固定长；核(w>0)原长（单核被抵消恒填满核空间、多核定基准比例）
    double StretchWeight { get; init; }   // 缩放比 len/d=r^w：0=刚性辅音 / >0=可伸核·元音；无音韵学知识全填同一正值(如1,等比)；全w=0退化整体等比
    // 引导/主体归属不落每音素：由所属列表（SynthesizedSyllable 两列表）给
}
// 合成产物 map 值型：引导/主体双列表 + 有符号 BodyOffset（junction=noteStart+BodyOffset，左负右正；0⇒主体起点精确落头、支持跨拍）。不设合并的 Phonemes 投影。
// sealed class（非 struct）：TryGetValue 未命中给 null，而不是两列表为 null 的结构体；构造经 ctor 保证两列表非空。
public sealed class SynthesizedSyllable { public SynthesizedSyllable(IReadOnlyList<SynthesizedPhoneme> leadingPhonemes, IReadOnlyList<SynthesizedPhoneme> bodyPhonemes, double bodyOffset); IReadOnlyList<SynthesizedPhoneme> LeadingPhonemes { get; } IReadOnlyList<SynthesizedPhoneme> BodyPhonemes { get; } double BodyOffset { get; } }
public sealed class VoiceSourceInfo { required string Name { get; init; } required string Description { get; init; } ImageResource? Portrait { get; init; } }  // sealed class + init（加字段纯加性）；Portrait 用 FileImageResource(绝对路径)，null=无
public sealed class SynthesizedPitch { IReadOnlyList<IReadOnlyList<Point>> Segments { get; } }      // 音高回显：分段折线，段内 Point=(秒,半音)；与 SynthesizedParameter 有意不共类型
public sealed class SynthesizedParameter { IReadOnlyList<IReadOnlyList<Point>> Segments { get; } }  // 回显曲线：分段折线，段内 Point=(秒,值)，段间断开
```
- 引擎 id 与实现类在 manifest：`{ "type":"voice", "engine":"id", "class":"Ns.MyVoiceEngine", "assembly":"X.dll" }`（`engine` 唯一；宿主校验 `class` 实现 `IVoiceSynthesisEngine`）。实现类需无参构造函数。
- 声明在引擎（gap：declaration timing）：5 个 GetConfig 在 `IVoiceSynthesisEngine` 上、是 `f(voiceId, 选中成员当前值)` 纯函数（含 `GetPhonemePropertyConfigs`，**全部 required、均须实现**）。`IVoiceSynthesisPartPropertyContext { IReadOnlyList<IVoiceSynthesisPartView> Parts }`（part 面板，可多选 part；多声库经 part 视图的 `VoiceId` 分流）。`IVoiceSynthesisNotePropertyContext`**独立不继承** `{ IVoiceSynthesisPartView Part; IReadOnlyList<IVoiceSynthesisNoteView> Notes }`——note 必属单 part 故 Part 单数、Notes 是各选中 note 的值视图列表。`IVoiceSynthesisPartView { string VoiceId; string PartId; PropertyObject PartProperties; IReadOnlyList<IVoiceSynthesisNoteView> Notes; IReadOnlyMap<string, IAutomationEvaluator> Automations }`。phoneme 属性**复用 note 声明上下文**（不再有独立 phoneme context）：`GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext) : IReadOnlyMap<int, ObjectConfig>`——音素序列挂到 note 值视图上，`IVoiceSynthesisNoteView { string Id; double StartTime; double EndTime; int Pitch; string Lyric; PropertyObject Properties; IReadOnlyList<IVoiceSynthesisPhonemeView> LeadingPhonemes; BodyPhonemes; double BodyOffset }`（全序列=引导++主体：前置辅音→核→后辅音，位置=拼接序索引）。返回**按核相对 slot 键控**的 schema map（键=slot：0=核、<0=引导、>0=核后，即音素角色；值=该角色在整个选区上的 schema；空 map=所有音素无属性、缺席 slot=该角色无属性——键控自描述、无位置对齐契约），schema 授给角色、可按核 vs 辅音差异化；多选合并归引擎（schema 依赖当前值时对同 slot 各成员值三态 Merge 再条件化，同 GetNotePropertyConfig 契约）、宿主只按 slot 并值；slot 口径与助手（`PhonemeAt`/`UnionSlots`；核下标即 `LeadingPhonemes.Count`）= SDK 共享纯函数 `PhonemeSlots`，引擎与宿主同用一份；phoneme 声明上下文与 note 声明上下文本就等价、复用同一接口不重复造类型。`IVoiceSynthesisPhonemeView { string Symbol; double Duration; double StretchWeight; PropertyObject Properties }`（引导/主体归属由 note 视图 `LeadingPhonemes`/`BodyPhonemes` 列表成员给、非每音素标志）。不在乎多选就 `context.Notes.Select(n => n.Properties).Merge()`（`PropertyObjectExtensions` 扩展方法@`TuneLab.Foundation`，收 `IEnumerable<PropertyObject>`）还原成单个三态 `PropertyObject` 按单选写；要逐成员真值就遍历列表。voiceId 进 context 使 voice 的 context 与 effect 的 `IEffectSynthesisPropertyContext`（无对等物）永久分叉。**会话构造期即可订阅自己声明的轨**：宿主在建会话前已据引擎声明填好轨集合，故构造期 `context.Automations` 已含你声明过的轨（`TryGetValue`/枚举）；漏订则该轨绘制后不触发重渲。
- 调度语义：一会话同时只合成一块；并行发生在不同 part 的不同会话间，并发上限由宿主管控。取消是正常调度结局（不抛 `OperationCanceledException`）；`await` 真正返回才释放槽位。
- 线程纪律：context（Notes/属性/automation）、`GetSnapshot`、`CreateAudioSegment` 仅可在 `SynthesizeNext` **同步前缀**（数据线程）读/调；之后 offload 只读已物化的 `VoiceSynthesisSnapshot`（不可变、可跨线程）。产物与 `CreateAudioSegment` 写入/Commit 在数据线程。
- 命名纪律：活视图类型（`IVoiceSynthesisContext`/`IVoiceSynthesisNote`/`ISynthesisAutomation`）仅数据线程访问；`*Snapshot`=冻结物（可跨线程、无事件）。
- 快照（gap：snapshot）：`GetSnapshot(notes)` 仅在 `SynthesizeNext` 同步前缀（offload 前、数据线程）调，可拉多份；`notes`=段内+协同发音邻居（你自由圈定），`snapshot.Notes` 与之【索引对齐】=产物归属依据（键用 `snapshot.Notes[i].Id`）。**automation/pitch 全量冻结、不开窗**：真实采样范围依赖音素时长（offload 后才知），前缀圈窗必错；冻的是原始锚点、廉价。automation 是冻结求值器（`Evaluator.Evaluate(times)`），不是裸点；想前缀采好就同步前缀 Evaluate 成 double[] 自存再 offload。快照不可变、只写一次；数据变了→标脏→下次拉全新快照（替换而非同步，无锁）。
- 音高（gap：pitch）：双通道 `finalPitch(t)=resolve(Pitch(t))+PitchDeviation(t)`。`Pitch` 分段（有值=用户钉死必守、NaN=自由区你自己生成，典型回退 note.Pitch）；`PitchDeviation` 连续永不NaN（vibrato 等汇于此，加在解析后绝对面上，自由区也生效）。按控制率布点→批量 `Evaluator.Evaluate(times)`→NaN处回退 note.Pitch 再加 deviation→逐采样插值。音高回显走 `SynthesizedPitch`（具名类型 `{ Segments }`，分段折线 Point=(秒,半音)；空=`new(){Segments=[]}`）。
- 音素（gap：phoneme I/O）：进/出**同一类型** `SynthesizedPhoneme`（只报 Symbol/Duration/StretchWeight，**无绝对位置**——定位/去重叠/melisma 全归宿主派生）；引导/主体归属做成**结构化双列表** `LeadingPhonemes`/`BodyPhonemes`（成员即分类、抗抖）、几何收进有符号 `BodyOffset`（junction=noteStart+BodyOffset，左负右正；0⇒主体起点精确落头、支持跨拍），两者正交。输入 `note.LeadingPhonemes`/`note.BodyPhonemes` 整note钉死（皆空=从 Lyric 做 G2P+自由定时；不支持部分钉死）。输出 `SynthesizedPhonemes` = **按归属 note 键的 map**，值=`SynthesizedSyllable{ LeadingPhonemes, BodyPhonemes, BodyOffset }`（键=`snapshot.Notes[i].Id`，字符串值语义、worker 可安全持有比较）；**键在与否即语义（三态）**：键不在=还没答案(脏/合成中)⇒宿主留白**且相接邻居一并留白**；键在+零音素音节=答案是"本note没有音素"(已算完)⇒本note留白但**邻居照常显示**；键在+有音素=常态。两空态不可互换(拿零音素当"没算完"会让邻居跟着消失)。**无「无主音素」契约**（时长模型下无锚不可定位）。核时长由宿主填充派生（报多少无所谓）、辅音填固定长；`StretchWeight` 无知识填 `Duration`。preview 纯显示、绝不当约束回喂。要把标称时长解析成真实时序驱动音频帧（避免重叠双算撑长），调 SDK 纯函数 `PhonemeLayout.Resolve(IReadOnlyList<PhonemeLayoutNote>)`（`PhonemeLayoutNote{ FillStart=音符头, FillEnd=你算的前向铺末, LeadingPhonemes, BodyPhonemes, BodyOffset }`，返回同构交错数组 `PhonemeTiming[][]`{Start,End,Duration}，`result[i][j]`=全序列(Leading++Body)第 j 个落点）——与宿主显示同一份代码、WYSIWYG、随宿主演进不漂移。**只接管定位/去重叠;标称时长(G2P/分组/dur 模型/padding)仍引擎专属、不被消掉。** `FillEnd` 取法：要 WYSIWYG 须与宿主同口径=自己有效末 + 仅延续乘客(你自己的会话 IsContinuation 判定,见下行) melisma、真发声 note 间空隙停在自己末(不铺过)。「错位非致命」只对**纯显示**成立——用 Resolve 驱动音频时 FillEnd 偏离口径(如填过空隙)→ 音频与显示分叉、**听得见**。
- 延音判定（gap：continuation）：会话实现 `bool IsContinuation(IVoiceSynthesisNote note)`——**必须实现、刻意无默认体**（判定与合成行为成对绑定，任何默认体都替合成许诺未必实现的语义、沉默继承即静默分叉）；不做延音语义就如实 `=> false`（每个note都是内容）。**判定权完整归引擎、判定优先级最高**（布局第一步即延音判定，判定为延续的note其音素数据根本不被读取）：宿主零判据、照单消费（显示布局/编辑手势同源），自定义记号（"-"/"+"/「ー」/词典驱动铺音节）自动获宿主 UI 支持。参考语义（编辑器 "-" 约定，宿主对无声源 part 也按此显示；完整代码见样例 tests/plugins/V1.Voice，十行）：歌词 "-" ∧ 经不断裂相接链回溯到内容note（相接=前末≥后起严格比较；空隙断链/链头缺失→孤儿 false）∧ 本note无钉死音素（钉死即内容、退出乘客并为合法链头）。链/相接/记号全是引擎语义空间（如"小间距视为相接"），SDK 刻意不提供判定助手（判定绑定合成行为，须完全自有语义）。约束：数据线程同步、不留存note引用、观测确定性（同当前数据→同答案）、**禁依赖合成进度/产物**。**绑定性**：判定为延续的 note 不得回传音素——区段发音(含melisma末尾辅音)全挂**链头note**；违约回传落账但被**忽略**不显示(兜底)，音频与显示的分叉属引擎自身矛盾。钉死音素是判定输入之一而**非宿主强制条件**：默认语义=钉死即内容退出乘客；自定义判定可自由决定钉死地位,宿主照单尊重(判定为延续则显示透明,含带钉死者,其语义由引擎向用户定义)。判定域=live数据：快照可能裁链头,要把身份带进worker就在SynthesizeNext同步前缀对live note自判、随自有快照冻结(按note间隙分块⇒块内自判等价)。
- 属性约定（gap：note/part props）：per-note/part 专属参数全走 keyed `Properties`（不动 `IVoiceSynthesisNote` 固定面）——`GetNotePropertyConfig`/`GetPartPropertyConfig` 的 `ObjectConfig.Create(IReadOnlyOrderedMap<PropertyKey, IControllerConfig>)` 声明（`PropertyKey`=`(id, 可选显示名)`，从裸 string 或元组隐式转换），合成时从 `VoiceSynthesisNoteSnapshot.Properties`/`snapshot.PartProperties` 用 `GetDouble/GetBoolean/GetString(key, default)` 读（稀疏、读不到取默认）。控件：`SliderConfig`/`ComboBoxConfig`(值显分离)/`CheckBoxConfig`/`TextBoxConfig`/`PathPickerConfig`(路径选择)。`AutomationConfig.DefaultValue=NaN⇒分段轨`（成员只读 + 流式 `WithColor`（不设=默认全透明 `#00000000`=宿主从调色板确定性分配不撞色的颜色，alpha 0 一律算不设）/`WithDefault`/`WithRandomizable`/`WithMinLabel`/`WithMaxLabel`/`WithFormat`/`WithShownByDefault(bool)`（用户没设过时在标签栏显不显示，默认 true；只是默认值、用户显式设过按用户的；只管标签栏、照常合成——过时 / 高级参数用）；**轨显示名不在 config 上、随 `PropertyKey` 走**）；`AutomationConfig.Create(INormalizedScale)` 可传自定义标度（`NormalizedScale.Integer(min,max)`=整数轨）。**离散标度⇒信号处处落格**：宿主在求值+渲染把 Hermite 连续输出投影回标度（曲线阶梯、`Evaluate` 返回落格最终值、覆盖 load/preset/回喂），引擎无需自行取整；连续标度投影=恒等（仅 ULP 级浮点扰动）。**标度只定义值轴形状+格点、不是值域保证**：宿主不把求值结果钳进 `[MinValue,MaxValue]`（钳位不可兑现——单调仅是约定、`Custom` 收任意 lambda、非单调时端点不是极值），越界值原样到达 `Evaluate`，引擎按需自校验（下标/查表/除数）。越界来源：存量数据（改过量程的旧工程/preset/引擎回喂）+ **加性模型**（锚点存相对 `DefaultValue` 的偏移 ⇒ 事后改默认值滑条即整批平移出界；vibrato deviation 加性叠加 ⇒ 贴近上界的曲线加 vibrato 即出界）——加性是刻意设计非缺陷。落笔本身不越界（拖拽/锚点输入钳在量程内）、面板绘制裁剪到面板内（越界锚点贴边）。**二值区间轨（band）**：分段+退化量程 `AutomationConfig.Create(v,v)`（默认 NaN⇒分段、min==max⇒无值轴）⇒ 宿主渲染满高开关色带（段=开、gap=关）、横拖涂开/右键涂关；消费=段存在性 `!IsNaN(Evaluate(t))`、段内值不读。条件轨消失后宿主保留已画曲线（隐藏不删）。`VoiceSourceInfo.Portrait` 用 `FileImageResource(绝对路径)`。
- per-phoneme 属性（gap：phoneme props；voice-only）：音素也可声明用户可编辑自定义属性，与 note 平行——`GetPhonemePropertyConfigs(context)` **复用 note 声明上下文** `IVoiceSynthesisNotePropertyContext`（不再有独立 phoneme context），音素挂在 `IVoiceSynthesisNoteView` 的双列表 `LeadingPhonemes`/`BodyPhonemes` 上（全序列 = 引导 ++ 主体，视图刻意不设合并投影，跨列表遍历用 `PhonemeSlots`）。返回**按核相对 slot 键控**的 `IReadOnlyMap<int, ObjectConfig>`（键=slot：0=核、<0=引导辅音、>0=核后，即音素**角色**；值=该角色在整个选区上的 `ObjectConfig` schema；空 map=所有音素无属性、缺席 slot=该角色无属性）。schema 授给角色而非单个音素：多选 note 时宿主把各 note 同 slot 音素并到一行共用该 schema，单选时 slot 一一对应逐音素、表达力无损；按核 vs 辅音差异化即 `slot == 0 ? vowelCfg : consonantCfg`。**多选合并归引擎**（同 `GetNotePropertyConfig` 契约）：schema 依赖当前属性值时自行对同 slot 各成员值三态合并——`context.Notes.Select(n => n.PhonemeAt(slot)?.Properties)` 非空者 `Merge()`。slot 口径与遍历助手 = SDK 共享纯函数 `PhonemeSlots`（`note.PhonemeAt(slot)`/`notes.UnionSlots()`；核下标即 `LeadingPhonemes.Count`，引擎与宿主同用一份、永不漂移）。**required**（不声明就返回空 map `[]`）。合成时从 `VoiceSynthesisPhonemeSnapshot.Properties`（两列表元素的 `.Properties`）用 `GetDouble/GetBoolean/…(key, default)` 读；几何走平铺字段 `ph.Symbol`/`ph.Duration`/`ph.StretchWeight`（引导/主体归属由所属列表给）；喂 `PhonemeLayout.Resolve` 时按字段重建 `SynthesizedPhoneme`。**只在钉死音素上**（用户数据）：引擎 G2P 的自动音素无属性。pay-as-you-go——数据层音素属性 lazy（未编辑零开销）、空时不序列化。活视图 `IVoiceSynthesisNote` 的 `LeadingPhonemes`/`BodyPhonemes` 仍是 `IReadOnlyList<SynthesizedPhoneme>`（不带属性）；属性只在合成快照 `VoiceSynthesisNoteSnapshot` 两列表（`VoiceSynthesisPhonemeSnapshot`）上出现。编辑 UI：侧栏逐 slot 面板已落地（符号标签+控制器，多选同 slot 并行、值三态合并编辑扇出）；音素的**选中模型**仍待做（音素尚无自己的选中态，属宿主侧、不在 SDK 面上；宿主以选中 note 全体音素为面板范围）。
- 原生依赖打包（gap：native/ONNX）：私有依赖（第三方托管库、native dll/so/dylib、模型、dict）放包文件夹→进本包专属 ALC（与其他插件隔离、版本不冲突）；SDK 程序集与 .NET 运行时由宿主共享、勿打进包。定位包内资源用 `Path.GetDirectoryName(typeof(MyVoiceEngine).Assembly.Location)`（勿用工作目录/`AppContext.BaseDirectory`=宿主目录）。native dll 与托管 dll 同目录便于 P/Invoke 探测；跨平台按目标分别提供 + manifest `platforms` 过滤。大模型权重勿塞 `.tlx`（即装即载会很重）：用独立资源包，或实现 `IExtensionSettings` 让用户配模型路径（路径用 `PathPickerConfig.CreateFolder()`/`CreateFile()`，密钥用 `TextBoxConfig.Create().WithPassword()`）。加载放 `Init`、失败抛异常（宿主优雅降级）。
- 失效自管：构造订阅 context（`Notes` 增删用 `WhenAny` 自动接线 / `note.*.Modified` 字段 / `PartProperties.Modified` / `Pitch`+`PitchDeviation`+各轨 `RangeModified`(秒区间)）handler 只廉价标脏，重活（重分块）推迟到 `context.Committed`（逻辑编辑收口、单条也补发→批量编辑只重分块一次）。tempo 变化/part 平移无独立信号也无增量通知——宿主整体重建会话（旧会话 Dispose、新 context 新会话即新秒值），正确实现 Dispose 即天然正确；边界 `Modified`/轨 `RangeModified` 只在 note/曲线自身编辑时触发。`Dispose` 退订 + Dispose 所有音频段。重叠 note(和弦) 分块判间隙用「组内最大结束」、块尾取 `Max(EndTime)`。
- 相关类型：`IVoiceSynthesisSession`、`IVoiceSynthesisContext`、`SynthesisRange`、`VoiceSynthesisSnapshot`、`VoiceSynthesisNoteSnapshot`、`VoiceSynthesisPhonemeSnapshot`、`SynthesisAutomationSnapshot`、`IAutomationEvaluator`、`IVoiceSynthesisNote`、`ISynthesisAutomation`、`IAudioSegment`、`SynthesizedPhoneme`、`PhonemeLayout`、`PhonemeLayoutNote`、`PhonemeTiming`、`PhonemeSlots`、`SynthesizedPitch`、`SynthesizedParameter`、`VoiceSourceInfo`、`FileImageResource`、`AutomationConfig`、`IVoiceSynthesisPartPropertyContext`、`IVoiceSynthesisNotePropertyContext`、`IVoiceSynthesisNoteView`、`IVoiceSynthesisPhonemeView`、`SynthesisStatusSegment`。
- 声明失效（gap：声明依赖插件自有派生状态，如后台歌词解析/声调分析）：声明可另依赖你自己算的派生状态（宿主看不见），派生状态一变须在会话里调 `IVoiceSynthesisContext.InvalidateNotes(IEnumerable<string> noteIds)` / `InvalidatePart()` 宣告，宿主才重新调 `Get*Config`。任意线程可调、宿主 marshal+合拍（连调只刷一次）；会话销毁后调用被忽略、未知 id 忽略。范围：`InvalidatePart`=part 面板+自动化轨集+回显轨集并**向下级联**到 note/音素声明；`InvalidateNotes`=这些 note 的 note/音素声明、**不向上级联**（影响 part 面板就另调 `InvalidatePart`）。**只作废声明回答**：不宣告用户数据变化、不使任何区段待重合成（合成失效自管）；**不含 `IsContinuation`**（延音判定须由当前数据同步答出，不许"先答后改"）；派生状态只能是用户数据的函数、不得依赖声明输出（防环）。身份对号：`IVoiceSynthesisNoteView.Id`=`IVoiceSynthesisNote.Id`=`VoiceSynthesisNoteSnapshot.Id`、`IVoiceSynthesisPartView.PartId`=`IVoiceSynthesisContext.PartId`；对象存活期间恒定、跨会话重建（换声库/改速度）不变、不持久——结果存引擎上按 id 查回。`PartId` 空串=声明未绑定具体 part（静态 schema 枚举），按"无派生状态"作答。参考 `tests/plugins/V1.Voice` 的 `LyricAnalysis`。
- 属性值的语义（所有声明面板通用）：**默认值 = 未设状态并定字段类型**（稀疏存储，读不到即默认，勿另设 Null 哨兵；默认值须为真实值，`PropertyValue.Null`/`Multiple` 不行——tag 与画布构造报错，下拉框缺省或所给项不能选时取第一个能选的选项）；**恢复默认 = 删键**（设成与默认相同 ≡ 没设过，勿依赖"存没存过"）；**存值类型与默认不同按默认处理**（滑条/数字框=数、复选框=布尔、文本/路径=字符串、tag/列表=数组、对象容器=对象；下拉框例外：选项可混合类型、存什么读什么、不在选项里原样保留琥珀显示）——这是对非法数据的兜底、**不可用于迁移**；**读值**：数值 `GetDouble`/布尔 `GetBoolean`/字符串 `GetString`，**勿用 `GetValue(key, 0)`**（推断成 int，SDK≤1.1 宿主恒返回默认），形状不定的值用 `props.Map.TryGetValue(key, out var v)`（`GetValue(key, PropertyValue.Null)` 老宿主同样恒返回默认），读到的一律校验；**多选**：声明 context 给各成员值、合并自己 `.Merge()`；面板显示标量整值比较、画布由插件自己合并、列表按下标、tag 取并集；编辑同值写全部（列表/tag 按元素扇出）；**`Multiple` 只是多选读出时"各对象不一致"的标记、不是值**：不能作默认、不能写回（宿主拒写；画布提交它时不写并记错误），没改值就别提交。
- 声明里的闭包与字段演进（所有声明面板通用：part/note/音素、instrument、effect、扩展设置）：**闭包只捕获值**——按钮回调、画布工厂都在声明调用结束后才执行，而 context/视图只在调用期间有效 ⇒ 在 `Get*Config` 里先取出 PartId、note Id、音素符号、同级字段已填的值等，让闭包捕获这些值，不捕获 context/视图。依赖 context 的 config 每次声明现建（闭包廉价），其余照样缓存；宿主既不要求共用也不要求现建。闭包与其建出的实例只活一次声明：用户改值/选中变化宿主自动重新声明，插件自算数据变了（解析出的声调、合成后缓存的分析结果）调 `InvalidatePart`/`InvalidateNotes`。**字段形状演进用新键**：改了值形状（对象→数组、对象内换字段）就把新形状放新键，别在旧键上换形状——宿主对类型不符的存值只做兜底（按缺省、显示声明默认值），判据是数据类型，形状只差在对象成员、类型相同时察觉不到，不得依赖它迁移。检测到旧键有旧形状值时二选一：旧键继续返回旧形状 config（可只在有内容时显示、只删不加），合成按旧逻辑读；或把旧值转成新形状、作为新键 config 的**默认值**返回（用户看到转换后的值，一编辑即以新形状存新键；合成有新键用新键，否则同一套逻辑转换旧键）。多选时默认值整次声明共用：各成员转换结果不一致就返回原本的默认值。原键换形状在新旧类型相同时必错：面板编辑是在当前显示值上改再整体写回，宿主照原样显示旧值，转换结果根本不会出现。

### Instrument 接口（命名空间 `TuneLab.SDK`）
> instrument = **多声部音源**（合成器/采样器）。与 voice **机制同构**（引擎/会话/调度/隔离快照/音频段/effect 链/扩展设置一致），接口族 `IInstrument*` 平行、与 voice 无继承。**仅三处实质不同**：
- **note 满末、不去重叠**：`IInstrumentSynthesisNote.EndTime`/`InstrumentSynthesisNoteSnapshot.EndTime` = `Pos+Dur`（宿主不钳到下一 note）；`Notes` 直传原始可重叠 note（和弦/多声部），引擎自行叠加混音（每个 note 按其 `Pitch` 各发一段、求和）。
- **无歌词/音素**：`IInstrumentSynthesisNote` 无 `Lyric`，也没有音素面（`LeadingPhonemes`/`BodyPhonemes`/`BodyOffset` 皆无）；会话无 `DefaultLyric`、不产 `SynthesizedPhonemes`。
- **无 pitch 曲线、产物仅音频**：`IInstrumentSynthesisContext` 无 `Pitch`/`PitchDeviation`（纯按 note 整数 `Pitch` 发声）；会话不产 `SynthesizedPitch`。仍可声明 automation 轨 + `SynthesizedParameters` 回显。
- 接口：`IInstrumentSynthesisEngine`（`InstrumentSourceInfos` 按 id 键的音色目录 + `Init/Destroy/CreateSession` + 声明四函数）/ `IInstrumentSynthesisSession`（`GetNextPendingSynthesisRange`/`SynthesizeNext`/`SynthesizedParameters`/`Status` + 两事件）/ `IInstrumentSynthesisContext`（`Notes`/`PartProperties`/`Automations`/`GetSnapshot`/`CreateAudioSegment`/`Committed`）/ `IInstrumentSynthesisNote`/`InstrumentSynthesisSnapshot`/`InstrumentSynthesisNoteSnapshot`/`InstrumentSourceInfo`/`IInstrumentSynthesisPartPropertyContext`/`IInstrumentSynthesisNotePropertyContext`。失效自管同 voice（无 `Pitch`/`PitchDeviation` 订阅、无音素/歌词字段订阅）。
- 声明失效同 voice：`IInstrumentSynthesisContext.InvalidatePart()`/`InvalidateNotes(ids)`，身份 `IInstrumentSynthesisNote.Id`=`IInstrumentSynthesisNoteView.Id`、`IInstrumentSynthesisContext.PartId`=`IInstrumentSynthesisPartView.PartId`（无音素级联）。
- 容器式发布（一引擎多音色，如 Kontakt）：`Init()` 扫已装资源包填 `InstrumentSourceInfos`，`InstrumentId` 选具体乐器；一插件一乐器 = 单条目。音色多时实现 `InstrumentSourceLayout`（与 `VoiceSourceLayout` 同构）分组，否则平铺。
- 参考实现 `tests/plugins/V1.Instrument`（sine/square 两音色、多声部叠加）；完整契约见 `docs/instrument-sdk-design.md`。

### Effect 接口（命名空间 `TuneLab.SDK`）
> effect = 对**整段已合成音频**的离线变换（如 SVC 换声），非实时 VST。**会话托管厚模型**：每种引擎一个 `IEffectSynthesisEngine`；
> 宿主为每条「effect 实例 × 一个上游音频段」建一个持久厚 `IEffectSynthesisSession`，持本段 `IEffectSynthesisContext`；
> **失效判定与调度归宿主**（作用域信号保守标脏），会话被调到 `Process` 就按当前真相干活（电平语义）、自比缓存早退去重。
```csharp
public interface IEffectSynthesisEngine {
    // 条件声明：均为当前参数值（context.Properties）的纯函数，宿主在参数 commit 时按当前值重算并 diff 到 UI（控件/轨可随参数显隐）。静态的忽略 context 返回固定值。
    ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context);                                              // 参数面板
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context);          // 自动化轨（连续/分段，DefaultValue=NaN⇒分段）
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context);// 只读回显轨声明（分段形 DefaultValue=NaN）；无回显返回空 map
    void Init();                                          // 无参；包目录经 Assembly.Location 自定位；失败抛异常
    void Destroy();
    IEffectSynthesisSession CreateSession(IEffectSynthesisContext context);   // 每「effect 实例 × 上游音频段」一个会话（与 voice 会话同族的持久有状态实体，作用域差异由 context 绑定表达）；context 由宿主实现
}
public interface IEffectSynthesisSession : IDisposable {
    // (重)处理本段：同步前缀（数据线程）经 Input.Read 把所需区间拷出到自有缓冲 + 预采参数/自动化值，之后才 offload；产物经 context.CreateAudioSegment 写出并 Commit。
    // 返回纯 Task：取消正常返回不抛 OperationCanceledException；错误抛异常（宿主 catch → 该段 passthrough 降级，状态带呈琥珀 Degraded）。无内部增量可做就任何信号→整段重处理。
    Task Process(CancellationToken cancellation = default);
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }   // 本段回显曲线数据，key 对齐 GetSynthesizedParameterConfigs；数据线程发布、宿主只读；无回显返回空 map
    IActionEvent SynthesizedParametersChanged { get; }    // 回显有更新（与 voice 会话对称、与 StatusChanged 信号分离；任意线程触发宿主 marshal；仅 Process 收尾发布的引擎可不触发——宿主收尾兜底重聚合）
    IReadOnlyList<SynthesisStatusSegment> Status { get; }    // 状态声称时间线（与 voice 同词汇；全局秒、主语=本会话自己的产物、不受输入几何约束）。Synthesizing 段带 Progress；Synthesized 段是"声称完成"（宿主呈现为非最终软色，最终绿只来自链尾音频事实）。不报返回空列表=宿主按调度事实兜底
    IActionEvent StatusChanged { get; }                   // 声称有更新：允许任意线程触发（worker 就地报）；宿主 marshal 后拉 Status。引擎宜自持粒度（如每整数百分比一报）
}
public interface IEffectSynthesisContext {        // 本段输入上下文（宿主实现、随会话死、仅数据线程访问）
    IEffectSynthesisAudio Input { get; }                  // 输入音频面（上游 voice 输出或前一 effect 输出，整段不可分割）；Process 被调到即已就绪（无「已提交」脉冲）
    IReadOnlyNotifiablePropertyObject Properties { get; } // 该 effect 自身参数活视图（订阅 Modified 标脏）
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }   // 该 effect 声明的连续自动化轨活视图（查询轴=全局秒；分段轨不在此）
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);  // 产出段登记表（与 voice 同构；1→N 合法如 splitter；不得重分布时间轴；采样率随段走）
    EffectSynthesisSnapshot GetSnapshot(double startTime, double endTime);  // 跨线程冻结快照（仅同步前缀；参数值+自动化冻结求值器；音频不在此列=Input.Read 自物化）——worker 自定采样时刻用；前缀可枚举时刻则直接 Evaluate 成数组即可
}
public interface IEffectSynthesisAudio {                  // 音频只读活视图（copy-out，永不暴露宿主存储引用；宿主可无缝换分页）
    long SampleOffset { get; } int SampleCount { get; } int SampleRate { get; }   // 全局轴 long、段内 int
    void Read(int offset, Span<float> destination);       // 区间 copy-out 到调用方缓冲（仅数据线程/同步前缀）
    IActionEvent<long, int> RangeModified { get; }        // 内容变更区间账本 (start=绝对采样位置,count)（可选）：上游实际写入的区间；绝对轴 ⇒ 上游 Resize 前后旧积累区间无需重定基；完备且忠实于裁剪——上游 Resize 对称差照报（裁剪区「从有到无」也是边界上下文变更），区间可越段界；消费方外扩自己的上下文余量∩段界**自决**重算量（点态引擎自决为零：Resize 输出跟随+空提交）；真无变更空提交才静默；整段仅强制失效（首建/率变）；缓存型引擎累积、按账本收窄重算窗、成功产出后清账（取消退账）——局部重合成范式见参考实现 Slow Gain
}
}
public interface IEffectSynthesisView {                   // 单个 effect 实例的只读值视图（声明面）
    PropertyObject Properties { get; }
    IReadOnlyMap<string, IAutomationEvaluator> Automations { get; }   // 现存曲线数据的轨求值器（全局秒；连续/分段皆在含孤儿，分段无曲线处 NaN）——可据曲线条件化 schema；未绘制的已声明轨不在 map（值=引擎自知默认；声明集=f(context)，按声明枚举是自引用）
}
public interface IEffectSynthesisPropertyContext { IReadOnlyList<IEffectSynthesisView> Effects { get; } }   // 声明求值上下文（多选壳：多选 part 时对应槽位各实例一个视图，单选=1；config 是选区纯函数，三态合并归引擎 .Merge()）
```
- 引擎 id 与实现类在 manifest：`{ "type":"effect", "engine":"id", "class":"Ns.MyEffectEngine", "assembly":"X.dll" }`（`engine` 唯一；宿主校验 `class` 实现 `IEffectSynthesisEngine`）。实现类需无参构造函数。
- 失效判定权归宿主：宿主按作用域信号（本段输入重 Commit / 本 effect 参数 settled 变更 / 自动化变更区间与本段相交）保守调度 `Process`；会话零上报义务。`Process` 是电平语义（让输出与当前输入一致）；缓存型引擎自比缓存早退、不重 Commit 即跳过下游。颗粒事件保留为可选缓存提示，最简引擎零订阅。
- 输出经握柄、登记表语义：`context.CreateAudioSegment(offset,count,rate)` 申请段，`Write`+`Commit`，各段独立生灭；**1→N 合法**（如按静音切分的 splitter，为下游重建段粒度）；输入面恒单段。红线：**不得重新分布时间轴**（automation/回显与 part 显示共全局秒轴）；几何微差（补边、加尾）合法。
- 一个 MidiPart 上多个 effect 按声明顺序串行，上一个输出是下一个输入；链尾各段按绝对时间混音。启用/顺序由用户管理。
- 回显（可选）：`GetSynthesizedParameterConfigs` 声明 + `IEffectSynthesisSession.SynthesizedParameters` 承载本段数据（宿主把同一 effect 各段按 key 拼接呈现），与 voice 回显同构、只读。
- 线程纪律：`context`（`Input`/`Properties`/automation）仅可在 `Process` **同步前缀**（数据线程）读；offload 后只读已物化的不可变值；`SynthesizedParameters` 与输出段在数据线程发布。

### 按钮 ButtonConfig（含点击期间专用的弹窗服务，命名空间 `TuneLab.SDK`）
- `ButtonConfig.Create(string text, Func<IButtonClickContext, Task> onClick)`（`Text` / `OnClick`）：config 族里**没有值**的成员（不实现 `IValueConfig`；恢复默认/preset/设置落盘跳过、CLI/agent 设不了）。任何声明面板都能放（扩展设置、part/note/音素属性、effect 参数），只能作 `ObjectConfig` 成员（任意深度、含列表元素里的对象），直接当 Array/List 元素或 ExtensibleObject 可加键不支持。**闭包只捕获值**（通用规则见「声明里的闭包与字段演进」；PartId、note Id、已读出的值；设置页 `context.Settings` 含未保存的值），不捕获声明 context/视图（点击可能很久以后）。**按钮不改工程数据**，只动插件自己的状态。**`OnClick` 在主线程调用**（与 Init/CreateSession/声明函数/会话事件同线程），await 后默认回主线程（除非 `ConfigureAwait(false)`），只在主线程访问的资源无需加锁；**await 是交错点**：期间宿主可能调用插件其他方法（合成调度/声明/会话事件），须保证状态跨 await 一致（如清缓存要么一步完成不 await，要么 await 前后状态都可用）；插件自开后台线程的并发自负；**别阻塞主线程**（宿主拦不住，瞬间操作同步做即可，耗时的 await 或放 RunWithProgress，`.Result` 等弹窗死锁）；**执行期间宿主照常可操作**（非模态，只该按钮置灰不可重入、面板重建后仍置灰；弹窗显示时才挡住按钮所在窗口），用户可能编辑/删除工程内容，闭包里的 PartId/note Id 可能已失效、按 id 查要容忍查不到；异常宿主记日志并弹错误（标明插件）；**执行完宿主自动重新调用所在面板的声明函数**（影响别处声明再调 `InvalidatePart`/`InvalidateNotes`）。占一行：有标题（显示名，缺省用 id）时按钮在右；键的显示名显式给空串 `("run", "")` 则无标题、按钮铺满整行。无其他布局属性，不可收起。
- 弹窗服务 `IDialogService`：按钮经回调参数 `IButtonClickContext.DialogService` 取得（画布经 `ICanvasHost.DialogService`，同一套、有效期不同，见画布一节）：`Task ShowMessage(title, message)`、`Task<bool> Confirm(title, message)`、`Task RunWithProgress(title, Func<IProgressReporter, CancellationToken, Task> work)`（`IProgressReporter.Report(double? fraction, string? status = null)`，进度窗从 0% 开始；fraction=null 不确定进度（滚动条）；status=进度条下方一行文字（自译），null/省略=保持上次文字、"" 清空；fraction 无"保持不变"写法，只更新文字须传当前进度；`IProgressReporter.SetCancellable(bool)` 关键段（替换文件/写索引）前置 false 使取消按钮变灰、出关键段置回 true（默认可取消；应用退出 token 照样取消，关键段须能应对进程被结束）；任意线程高频调用宿主按帧合并；恒带取消按钮，用户取消/应用退出时 token 取消；work 异常原样传回）、`Task<PropertyObject?> PromptInputs(title, ObjectConfig config)`（向用户索取输入，与脚本工具 inputs 同一表单窗；要随填写的值变化（条件字段、依赖当前值的画布）就传函数 `PromptInputs(title, Func<PropertyObject, ObjectConfig>)`——同属性面板，值一变即以表单当前值（只含改过的字段，读不到即默认）重新调用；返回**完整值**，没改的字段为默认值、初始值写进默认值；取消 null；不进工程撤销）。以按钮所在窗口为父模态显示、标明来源插件；有效期 = 回调开始到其 Task 完成，事后再用抛 `InvalidOperationException`；任意线程可调；**禁同步阻塞等待**（`.Result`/`.Wait()` 在 UI 线程死锁）。
- 插件自己的设置界面（自己布局、自己管数据）：**做成独立进程**，作者自写 exe、按钮只负责启动（可 `await process.WaitForExitAsync()` 让按钮在其关闭前置灰、关闭后面板刷新）；与宿主无关——不提供父窗口、不做模态，数据交换自定。**进程内 UI 框架不在契约里**：宿主 Avalonia 不对插件开放，WinForms/WPF 宿主进程里没有（不带桌面运行时、引用即运行时缺程序集），硬要进程内引入由作者自保不冲突。参考 `tests/plugins/V1.CustomWindow`+`V1.CustomWindow.Editor`（按钮启动 WinForms exe）、`V1.Settings`（设置页各种按钮+弹窗+PromptInputs）、`V1.Voice`（part/note/音素按钮）、`V1.Effect`（effect 按钮+进度）。

### 画布 CanvasConfig（插件自绘的值字段，命名空间 `TuneLab.SDK`）
- **SDK `1.2`，尚未发布、实验中**（API 发布前可能调整）；用到画布 manifest 须声明 `"sdk-version": "1.2"`。
- `CanvasConfig.Create(PropertyValue defaultValue, double preferredWidth, double preferredHeight, Func<ICanvasHost, ICanvasControl> createControl)`、`.WithSummary(string?)`：**默认值 = 未设状态且定字段类型**（存值类型不同按默认处理 ⇒ 值的类型要固定），不能是 `PropertyValue.Null`/`Multiple`（Create 报参数错误）；**建议顶层用对象**（默认 `{}`，以后加成员不换形状），只存一个数时标量也行。**值字段**（实现 `IValueConfig`；恢复默认/preset/撤销/多选照常）。值形状插件自定（任意 PropertyValue）、宿主不解读——**读值一律校验**（撤销/preset/粘贴可带来任意形状，不合形状按默认）。**值在声明时取**：声明 context 给每个选中对象各自的值（读不到=默认值，与 Create 用同一默认值常量），取出让工厂捕获交给控件，**宿主没有读值接口**（`ICanvasHost` 无 `Value`）。多选：拿到的是各对象的值，合并与呈现插件自定（如逐点比较、不一致的点画空心），提交时同一个值写到全部目标（不一致时从哪个值改起也自定）。收起摘要：`WithSummary(text)` 在声明时按当时的值算好（值变即重新声明），多选写什么也自定；不给则"默认/已修改/多值"。CLI/agent 原样读写（任意 JSON，宿主不校验；agent 被引导先读 introduction、读不到再照当前值/默认值推断）⇒ **introduction 里必须写清该字段的值形状与含义**（成员、范围、随状态变化的形状）。只能作 `ObjectConfig` 成员。**尺寸**：首选宽高 = 插件心目中的最佳尺寸（都必须是正的有限数，宽度眼下没有位置取用也要认真给）；实际尺寸由宿主按位置定（侧栏：宽随面板、高取首选），读 `host.Width`/`host.Height`、随时可变、无尺寸事件，每次 Render 按当前尺寸排版。
- **生命周期**：工厂不在声明时调用（显示时才建，收起不建）；**值一变实例即作废**：工程里本字段的值变了（自己的提交、撤销、别处改了同一份数据）当前实例立即作废——不再收事件、写回不生效、屏幕只留最后一帧——随即按新声明（带新值）用新工厂建新实例 ⇒ 一个实例生命周期内工程值不变、构造时拿到的值始终有效；**实例寿命 = 一次声明**——每次面板按新声明更新（提交后、外部改值后、选中变化）都用新工厂重建，旧实例的 host 失效（`SetValue` 返回 false）⇒ **工厂要廉价**、控件只放事件与绘制逻辑、重资源插件级缓存；悬停等临时状态不跨实例，要能从值与事件位置重算；没宣告编辑也没写草稿的拖动可能中途换实例，新实例收不到那次抬起，按"没在拖动"处理；需要上下文（所属 part/note、同级字段的值、自算数据如波形）就在声明时取值让工厂捕获，数据变了调 `InvalidatePart`/`InvalidateNotes` 重新声明（见「声明里的闭包与字段演进」）；**事件严格成对**：每实例 Enter/Leave 严格交替（换实例或画布出现在静止指针下时宿主主动发 Enter、不等动鼠标）、**每个 Enter 后紧跟同位置的 Move**（补发的也是）⇒ 悬停等按位置算的状态只在 OnMouseMove 里算、每个 Up 都有本实例收到过的对应 Down、捕获意外丢失时宿主补发 Up（IsClick=false）。
- **编辑状态、草稿与提交**（两件事分开管）：**编辑状态由插件宣告、宿主不猜**——`host.BeginEdit()`/`EndEdit()` 成对（计数、可嵌套，全部结束才解锁；多余的 EndEdit 空操作并记错误），期间所在窗口属性面板不刷新、实例不被换掉；作用域写法 `mEdit = host.EditScope()` … `mEdit.Dispose()`（扩展方法，重复 Dispose 无副作用）；忘了结束则窗口面板一直不刷新。**值**：`SetValue` 写草稿（宿主代管、不进工程、别处不可见、提交时写进工程；**不读回给控件**，插件自己记着画，返回 true 才记），**只在值真的变了时写、只写真实的值**——`PropertyValue.Multiple`（或内含它的值）`SetValue` 返回 false 不写并记错误；没改值就要锁画布用 `BeginEdit`；有草稿也算面板在用（与 BeginEdit 同时起作用），滚轮/键盘这类无"按下"的操作直接写草稿即锁；草稿可长期挂着（如等画布上的"确认"）。`CommitValue` 写进工程成一个撤销步，`DiscardValue` 丢弃草稿回到工程值（插件自己记的草稿也清掉，回到声明给的值）；两者只管值、不结束 BeginEdit 的编辑，无草稿则空操作；取消拖动 = DiscardValue 再结束编辑。**宿主从不代为提交、不替插件结束编辑**；实例作废（外部改了本字段值、面板被拆、画布离开界面）时草稿与编辑状态一并作废。拖动示例：按下 `mEdit = host.EditScope()`，移动 `SetValue(新值)`，松开 `CommitValue()` 再 `mEdit.Dispose()`。无"松手"的连续操作（滚轮）每格 SetValue、`await Task.Delay(…)` 等停顿再 `CommitValue()`（await 后回主线程可直接用 host）。
- `ICanvasHost`（宿主实现）：`Width`/`Height`、`Invalidate()`（自身状态变了请求重画；值/草稿/尺寸变化宿主自动重画）、`bool SetValue(PropertyValue)`（写草稿）、`CommitValue()`（草稿进工程、一个撤销步骤；单击类操作 SetValue+CommitValue 连调）、`DiscardValue()`（丢弃草稿）、`DevicePixelRatio`（物理像素 / 逻辑像素，自生成位图按 宽×它 的像素数；变化后宿主重调 Render）、`Rect VisibleBounds`（画布此刻露出的部分，画布坐标，看不见时宽高 0；**只是提示**、不处理照样正确，逐像素采样/自生成位图的按它只算露出部分；变化后宿主重调 Render）。只在主线程用。
- 弹出与弹窗（`ICanvasHost`）：`Task<string?> PopupTextBox(text, Rect bounds, CanvasTextStyle? style=null, CanvasTextBoxOptions? options=null)`（多行=`CanvasTextBoxOptions.Create().WithMultiline()`，不传=单行）（几何同 `DrawText`：文字区=给定矩形、按 style 的对齐排、字号同 style 字体随界面，传画字的那个矩形与样式即盖在字上；矩形给"可用的地方"别给当前文字宽，框不随内容变大、装不下框内滚动不折行；太小按锚点扩到一行高与最小宽，宽高 0=以点为锚；Enter/点别处=确认返回文字，Esc=取消返回 null，开多行时 Shift+Enter 换行、文字里是 `\n`）、`Task<PropertyValue?> PopupMenu(Rect anchor, IReadOnlyList<ComboBoxItem> items)`（矩形下方弹出、放不下翻上方，宽高 0=在该点弹出如右键菜单；选项同下拉框：分组=子菜单、分隔线、叶子多自动搜索；选中返回值，点别处/Esc 返回 null；当操作菜单就把值写成命令名自己分派）。**只返回结果、宿主不写值**，改值照常 SetValue+CommitValue；结果在弹出关掉后才交回，await 后可直接写。弹出期间算"在用"（与 BeginEdit、草稿是三个独立状态，任一存在即锁画布；不占 BeginEdit 计数）；同时至多一个，再弹先关前一个（返回 null）；实例作废则关闭并返回 null，await 回来实例已作废则 SetValue 返回 false。`IDialogService DialogService`：与按钮同一套，以画布所在窗口为父、标明来源插件；**本实例有效期内都能用**，实例作废后再调抛 `InvalidOperationException`；弹窗开着同样算"在用"。
- `ICanvasControl`（插件实现，**全部成员带空默认体**，只实现关心的）：`Render(ICanvasDrawing)`、`OnMouseDown`/`OnMouseUp(CanvasMouseButtonEvent)`（`Button`、`IsDoubleClick`、`IsClick`）、`OnMouseMove`/`OnMouseEnter`/`OnMouseLeave(CanvasMouseEvent)`（`Position`: Foundation `Point`、`Modifiers`: `CanvasModifierKeys`，macOS Cmd 记作 Ctrl）、`OnScroll(CanvasWheelEvent)`（`Delta`，上/左为正）、`OnKeyDown`/`OnKeyUp(CanvasKeyEvent)`（`Key`: `CanvasKey` 收录子集=字母/数字 `Digit*`/F1–F12/方向键 `Arrow*`/Enter/Escape/Tab/Backspace/Delete/Home/End/PageUp/PageDown/Space/Ctrl/Alt/Shift，取值=通行虚拟键码，其余键不转发；`Modifiers`；`IsRepeat`=系统按住重复；画布有焦点才收到，点一下即获得；严格成对，失焦/离开界面补发 Up，新实例的第一个按下 IsRepeat=false；按住连调=首按写草稿、抬起提交；无文字输入）。全部事件参数继承 `CanvasEvent`，带 `bool Handled`：处理了置 true ⇒ 宿主不做默认动作，默认 false = 交还宿主；有默认动作的是滚轮（滚动所在面板）与键盘（冒泡给快捷键/外层面板）——**只在真正用到时置位**，别整块吞（吞滚轮则滚面板时指针扫过画布即卡住，吞键盘则画布有焦点时空格播放/撤销等快捷键失灵）；鼠标按键/移动置不置无差别。按下后自动捕获鼠标。全部回调在主线程；回调抛异常宿主记日志（标明插件）并停用该实例直到重建。
- `ICanvasDrawing`（只在 Render 期间有效；逻辑像素、左上原点、裁剪在画布内、宿主先画输入框同款深色圆角底）。参数一律"画什么、在哪 → 用什么画"；样式对象都不可变、构造时校验、**建好存静态字段复用**（宿主按对象缓存）：`CanvasPen.Create(Color, width=1)`（`WithColor`/`WithWidth`/`WithLineCap(Flat|Round|Square)`/`WithLineJoin(Miter|Round|Bevel)`，默认圆头圆角；width 须正有限）、`CanvasBrush.Solid(Color)`（填充与文字色）、`CanvasTextStyle.Create()`（`FontSize` 为 `double?`，null=跟随宿主；`WithFontSize`/`WithAlignment(CanvasAlignment)`，默认 Center；DrawText/MeasureText/PopupTextBox 的 style 都可不传=默认样式）。几何量与颜色用 Foundation 的只读 record struct：`Rect(X, Y, Width, Height)`（`Left`/`Top`/`Right`/`Bottom`、`Center`、`Contains(Point)` 含边、`Inflate(dx, dy)` 负数=向内缩、`Rect.FromLTRB(l, t, r, b)`；宽高为负=空）、`Size(Width, Height)`、`Color`（无公开构造、只走工厂：`FromRgb(r,g,b)`/`FromArgb(a,r,g,b)`/`FromHex("#RRGGBB"|"#AARRGGBB")`，SDK 颜色一律 Alpha 在前=工程文件 8 位十六进制语义；`WithAlpha`；按分量判等；`default`=全透明黑）；形状 `CanvasGeometry.Polyline/Polygon(IReadOnlyList<Point>)`（点列建时拷贝；Polygon 自动闭合）/`Rectangle(Rect, cornerRadius=0)`/`Ellipse(Point center, rx, ry)`/`Circle(Point center, r)`，填充与裁剪按围出的区域算（不闭合的折线也首尾相连围出），退化形状画不出、裁剪则什么都不留。原语：`DrawLine(Point from, Point to, CanvasPen)`、`DrawGeometry(CanvasGeometry, CanvasBrush? fill, CanvasPen? stroke)`（可只给一个，都给=先填后描）、`DrawText(text, Rect bounds, CanvasBrush, CanvasTextStyle? style=null)`（按 style 对齐在矩形内，宽高 0 = 以点为锚；`\n` 换行、多行时每行也按横向对齐，不自动折行；字体随界面）、`Size MeasureText(text, CanvasTextStyle? style=null)`（排版按量出的尺寸，别按字号推算）、`DrawImage(CanvasImage, Rect)`（缩放进矩形、不保持宽高比；SVG 按绘制尺寸清晰栅格化）、`IDisposable PushOpacity(opacity)`/`PushClip(CanvasGeometry)`/`PushTransform(System.Numerics.Matrix3x2)`（用 using；栈式后进先出、与外层复合；Render 结束宿主撤销残留；**变换不作用于鼠标坐标**）。扩展方法（`CanvasDrawingExtensions`，`Draw*`=按给的 fill+stroke、`Fill*`=只填、`Stroke*`=只描）：`DrawPolyline(points, pen)`、`DrawPolygon/FillPolygon/StrokePolygon`、`DrawRectangle/FillRectangle/StrokeRectangle(Rect, …, cornerRadius=0)`、`DrawEllipse/FillEllipse/StrokeEllipse(center, rx, ry, …)`、`DrawCircle/FillCircle/StrokeCircle(center, r, …)`、`PushClip(Rect, cornerRadius=0)`。颜色别漏 alpha；画布代码不需要 `using System.Drawing`。
- `CanvasImage`（sealed、IDisposable）：`FromEncodedData(ReadOnlySpan<byte>)`（PNG/JPEG/WebP/SVG；SVG 不加载外部引用；无法识别抛 ArgumentException）、`FromPixelData(int width, int height, ReadOnlySpan<byte> rgba)`（RGBA 非预乘、逐行无填充、长度须 = w×h×4）、`Width`/`Height`（自然尺寸：位图=像素、SVG=声明尺寸）。**不可改写**：换内容=新建一张并 Dispose 旧的（先 Dispose 再新建）；已画出的帧不受 Dispose 影响；任意线程可创建/Dispose，绘制只在 Render；**插件级缓存**别每次解码。参考 `tests/plugins/V1.Voice/StretchCurveCanvas.cs`、`tests/plugins/V1.Voice/ImageDemoCanvas.cs`。

### 时间轴画布 TimelineCanvasConfig（命名空间 `TuneLab.SDK`）
- `TimelineCanvasConfig.Create(PropertyValue defaultValue, Func<ITimelineCanvasHost, ICanvasControl> createControl)`、`.WithColor(string)`、`.WithSummary(string)`（不设=默认全透明 `#00000000`=宿主从调色板分配，同 `AutomationConfig`）。**只在参数面板里、沿时间线排布**：每个声明了它的对象（note 级=每个 note、音素级=每个音素）在自己时间段上占一块（宽=对象时间段、高=面板高）；侧栏只显示一行摘要；标签栏按各 note 声明的并集常驻、用户不用钉。可放在 note/音素属性声明的**任何位置**（顶层、对象/定长数组/列表/键控容器内任意嵌套），参数面板按路径各成一条 lane，标签=末段显示名+路径序号（「曲线 2」），重名才补祖先名，悬停显示完整路径（叶子显示名尽量别重名）；列表下标随增删调序变。侧栏在其位置显示一行「字段名 + 摘要 + 编辑按钮（跳参数面板）」，摘要 `.WithSummary(text)` 声明时按值算（多选自定），不给则宿主兜底「默认/已修改/多值」；无首选尺寸。值语义、实例寿命、控件/绘制/事件/草稿/弹出与 `CanvasConfig` 完全相同。
- 选型：界面属于侧栏、与时间无关（或只按对象内相对位置画）→ `CanvasConfig`；必须与时间轴对齐、逐个对象编辑（如音素混合）→ `TimelineCanvasConfig`；同一字段只能选一种（侧栏可多选、多对象无共同时间段，故时间换算只有时间轴画布有）。
- `ITimelineCanvasHost : ICanvasHost` 多 `ICanvasTimeAxis TimeAxis`（一定有）：`TimeToX(double seconds)`/`XToTime(double x)`，全局秒（同 `IVoiceSynthesisNoteView.StartTime`）↔ 画布坐标；跟钢琴窗时间轴、速度变化处不均匀，**别假设线性**；画布覆盖 `XToTime(0)`..`XToTime(Width)`，未必等于对象起止（note 钳到下一 note 起点、音素显示时间由宿主排版），按自己数据的绝对时间换算。**所见即所闻**：按秒定义的曲线逐像素采样（每列 `XToTime(x)` 再按秒求值），别只换算控制点连直线。**以宿主换算为准**、别用自己合成算出的音素时间画（合成回填后两者一致，过渡期宿主更新显示即重调 Render）。按尺寸排版（可能极窄/极矮/上万像素宽），宿主不告知位置。默认值宜中性或按对象内相对位置定义（未钉住音素长度随合成变）。
- 宿主侧呈现（插件无需操作）：每块正好覆盖对象时间段、**相邻块不留缝**、透明底（分隔自己画）；作**背景参数**时照常 Render、宿主压暗垫在激活参数下、不收输入；**画布不叠画布**（激活是画布时别的画布不画；一块面板里画布可见互斥）；写音素级字段时宿主先固定该音符音素再写值、一个撤销步。用户也可把侧栏 `CanvasConfig` 钉进参数面板（每对象一块、宽=对象时长、高=面板高，host 仍是 `ICanvasHost` 无时间轴）。参照实现 `tests/plugins/V1.Voice/TimelineDemoCanvas.cs`（按秒存点+逐像素采样+只算 VisibleBounds 列、加/拖/删点、点符号弹 PopupTextBox、右键 PopupMenu、窄矮只画曲线）、`tools/ScreenshotBot/demo-plugins/DemoVoice/BreathCanvas.cs`（只展示的最小写法）。

### 扩展设置（IExtensionSettings，命名空间 `TuneLab.SDK`）
> per-extension（per 能力实现者，如每个 voice/effect 引擎一份；安装包 ExtensionPackage 可含多个 extension）、随宿主持久化、跨工程共享的设置（API key / 模型路径 / 设备等）。**opt-in**：能力实现类**额外实现** `IExtensionSettings` 即接入；宿主 `x is IExtensionSettings` 探测，在「设置 > 扩展」分页渲染、按 extension 落盘、运行时回喂。
> 与属性面板（voice `GetPartPropertyConfig`/`GetNotePropertyConfig`、effect `GetPropertyConfig`）**不同**：那些是随工程序列化的实例/段级属性；本接口是扩展自身配置、单独落盘、与工程无关。两者复用同一控件配置词汇 `ObjectConfig`。
```csharp
public interface IExtensionSettings {
    ObjectConfig GetSettingsConfig(IExtensionSettingsContext context); // 声明 schema；是 context.Settings 当前值的纯函数（可条件显隐）、【Init 前可调】；密钥字段用 TextBoxConfig.Create().WithPassword()
    void ApplySettings(PropertyObject settings);                       // 宿主回喂持久值：加载后一次（早于 Init）+ 用户保存后一次。实现者自存自用于 Init/CreateSession
}
public interface IExtensionSettingsContext { PropertyObject Settings { get; } } // 当前已填设置值（条件显隐据此）
```
- 任意能力（voice/effect…）可在其接口外**再实现** `IExtensionSettings`，如 `class MyVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings`。agent 模型引擎有独立侧边栏设置、不走此路。
- **动态项**：`GetSettingsConfig(context)` 据 `context.Settings`（当前值）返回 config，用户改值后宿主重算并 diff 到控件——可据已填值显隐字段。静态面板忽略 context 返回固定 config。
- 密钥：`TextBoxConfig.Create().WithPassword()` → 宿主掩码显示 + 安全落盘（Win=DPAPI 密文就地 / macOS=钥匙串；无安全存储则**不保存该字段、绝不明文**+告警；官方支持 Win/macOS）。值类型仍是普通 string。
- 读值：`settings.GetString(key, default)` / `GetDouble` / `GetBoolean(key, default)`。读不到按默认 fallback。
- `DisplayText` 由插件自译（按 `TuneLabContext.Global.Language`）；manifest **不**声明设置（纯代码）。
- **format 条目**（`IImportFormat`/`IExportFormat` 之外再实现本接口）：单位是【条目】不是后缀——多后缀别名共**一行**、值只存**一份**（路由仍逐(方向,后缀)，是另一根轴）；方向不对称也不劈开它。**两份实现两份设置**：两个类是两个条目、各一个桶、各一份 schema。format 注册的是**工厂**（每次导入/导出现 new），故 ①`ApplySettings` **每造一个实例响一次**（就在构造之后、`Deserialize`/`Serialize` 之前），不是"设置变了"的通知，要轻；②宿主会多造一个**探测实例**只为问 schema（不参与导入导出）→ 无参构造函数保持轻量；③桶键 = `<推出的kind>:<两方向后缀并集按声明序用 | 拼接>`（双向 `format:mid|midi`、只导出 `format-export:midi`），增/删/重排任一后缀即**换桶且不迁移旧值**（回默认、密钥要重填）——后缀清单发布前定死。
- 控件配置类型（值配置构造函数均已封、只走静态工厂）：`TextBoxConfig.Create(default)`（`.WithPassword()` 掩码；`.WithMultiline(最多行数，缺省 0=不封顶)` 多行框（框随内容长、到顶则框内滚动；Enter 确认、Shift+Enter 换行），值仍是一个 string、换行在里面；与掩码互斥，同时声明则仍为单行）、`PathPickerConfig.CreateFile(default)`/`CreateFolder(default)`（路径选择：文本框 + 浏览按钮开系统选择器，值仍是普通 string；`.AppendFileType(名称, "*.exe")` 类型过滤、`.WithPickerTitle(标题)`；宿主不校验路径存在性，插件自校）、`CheckBoxConfig.Create(default)`、`ComboBoxConfig.Create(options)`（`.WithDefault(option)`，缺省或所给项不能选时取第一个能选的叶子、跳过分组与分隔线、都没有则默认为空；选项值可混合类型、存值不在选项里原样保留并琥珀显示；`new ComboBoxItem(组名, 子项)` 分组=子菜单可嵌套；`ComboBoxItem.Separator(标签?)` 分隔线顶层/分组内皆可（顶层亦可 `.AppendSeparator`）；叶子≥10 宿主自动加搜索框）、`SliderConfig`（`SliderConfig.Linear(default,min,max)` 连续 / `SliderConfig.Integer(default,min,max)` 整数 / `SliderConfig.Create(default, INormalizedScale)` 自定义标度；流式 `.WithFormat(INumberFormat)`、`.WithRandomizable()` 声明可随机（宿主在右侧给随机入口）、`.WithMinLabel(text)`/`.WithMaxLabel(text)` 量程端点描述文本（滑条两端 + 参数面板上下界，插件自译、可只设一端，同 `AutomationConfig` 语义））；容器 `ObjectConfig.Create(IReadOnlyOrderedMap<PropertyKey, IControllerConfig>)`（构造函数已封、只走工厂；键 `PropertyKey` 自带显示名）；`ListConfig`/`ExtensibleObjectConfig` 的 + 候选同构分组：`new AddableElement(组名, 子项)`/`new AddableKey(组名, 子项)`，分隔线 `AddableElement.Separator`/`AddableKey.Separator`（键控隐藏已加键后空段分隔线随之隐藏；分组/分隔线无 `Template`、`AddableKey` 的无键 Id，先判 `SubItems`；`ExtensibleObjectConfig` 条目按返回的 `Properties` 序显示，数据键无序勿照搬、按候选目录排，需用户调序用对象元素的 `ListConfig`）；多选标签 `TagConfig.Create(IReadOnlyList<TagItem>)`（chip 横排换行 + 末尾 `+`；值 = 所选候选值的 `PropertyArray`，有序、不重复：新加追加末尾、可拖动调序、已选值不进 `+` 菜单；`TagItem` 形同 `ComboBoxItem`（值/显示分离、任意基础类型值、`new TagItem(组名, 子项)` 分组、`TagItem.Separator(标签?)`）但为独立类型；`.WithDefault([值…])` 默认已选；候选值与默认所选须为真实值，`PropertyValue.Null`/`Multiple` 构造即报参数错误；多选显示并集、部分有的半透明；候选外的值保留并琥珀色显示；与元素为 `ComboBoxConfig` 的 `ListConfig` 同数据形状但后者允许重复，按语义二选一；改老字段为 tag 时数据形状变：tag 用新键，旧数据兼容见「声明里的闭包与字段演进」）。

### 常见错误（避免）
- ❌ 漏 `id` → 被当成 Legacy。
- ❌ 引用 `TuneLab.Hosting.Foundation` 或主程序 → 不是插件契约，加载会出错。
- ❌ 把 SDK 程序集打进包 → 与宿主共享版本冲突，应共享宿主的。
- ❌ 目标框架非 net8.0。
- ❌ 插件类缺无参构造函数 → 无法实例化。
- ❌ 臆造 SDK API / 接口成员 → 严格按上方签名。
- ❌ 用 `PropertyValue.Null` 等特殊值当"未设"哨兵 → 默认值本身就是未设状态；Null 作默认值的字段永远存不进值。
- ❌ 存值类型与默认值不同（如默认是数、存了字符串）→ 读成默认值。值的类型要固定。
- ❌ `GetValue(key, 0)` 读数值 → `0` 推断成 int，老宿主恒返回默认值；用 `GetDouble`。
- ❌ 把多选读出的 `Multiple` 写回去 → 它不是值，宿主拒绝写入；没改值就别提交。
- ❌ 改了字段的值形状仍用原键 → 换新键（见「声明里的闭包与字段演进」）；宿主的类型兜底不能用来迁移。
- ❌ 闭包里捕获声明 context / 视图 → 只捕获取出的值，context 只在声明调用期间有效。
- ❌ 声明依赖插件自算的数据，数据变了却不调 `InvalidatePart`/`InvalidateNotes` → 面板停在旧样子。
- ❌ `cond ? null : PropertyValue.Create(x)` → `PropertyValue` 是结构体，裸 `null` 编译不过，写 `(PropertyValue?)null`。
- ❌ 以为画布的弹出（`PopupTextBox`/`PopupMenu`）会替你写值 → 只返回结果，改值照常 `SetValue` + `CommitValue`。

---

## 最小完整示例（format）

`manifest.json`：
```json
{
  "id": "com.example.myfmt",
  "name": "My Format",
  "version": "1.0.0",
  "sdk-version": "1.0",
  "type": "format",
  "suffixes": ["myfmt"],
  "introduction": "Introduction.md",
  "class": "Example.MyFormat.MyFormat",
  "assembly": "MyFormat.dll"
}
```

`MyFormat.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="TuneLab.Foundation" Private="false" />
    <Reference Include="TuneLab.SDK" Private="false" />
  </ItemGroup>
</Project>
```

`MyFormat.cs`（类全名写进 manifest 的 `class`；`type:"format"` 要求它同时实现 `IImportFormat` 与 `IExportFormat`；不写 attribute）：
```csharp
using System.IO;
using TuneLab.SDK;

namespace Example.MyFormat;

public class MyFormat : IImportFormat, IExportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        // TODO: 解析 stream 填充 project
        return project;
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        // 宿主拥有 output 生命周期，插件只顺序写入、不得 Dispose/Close/Seek/重置 Position。
        // TODO: 把 info 写进 output
    }
}
```

> 若导入与导出是**两个类**，就写两个条目（都是 `type:"format"`，一个只给 `import-suffixes`、一个只给 `export-suffixes`），各写各的 `class`；它们各有独立的 introduction 与扩展设置桶。
