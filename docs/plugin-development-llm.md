# TuneLab 插件开发 · 面向 AI 的参考

> 本文为**喂给 AI 助手**的结构化事实清单，用于生成正确的 TuneLab V1 插件。
> 人类可读版见 [plugin-development.md](plugin-development.md)。

---

## 一句话提示语（复制给你的 AI 助手）

> 你要为 TuneLab 编写一个 V1 插件。插件是一个文件夹，根目录必须有 `description.json`（其 `id` 字段是新版判别标志，必须有，用反向域名）。代码插件类库目标框架锁 `net8.0`，**只引用** `TuneLab.Foundation` 与 `TuneLab.SDK.*`（绝不引用 `TuneLab.Hosting.Foundation` 或主程序），且 SDK 程序集不随包分发（宿主共享）。**插件身份写在 manifest、不用 attribute**：每个能力条目用 `classes`（候选类全名数组）列出入口类，宿主按本 `type` 所需接口扫描认领——format 找 `IImportFormat`/`IExportFormat`（导入/导出类，可两类可一类同实现两接口）+ 身份 `extension`；voice 找 `IVoiceEngine`、effect 找 `IEffectEngine`（对整段音频做离线变换）、agent-model 找 `IAgentModelEngine`，+ 身份 `engine`。所有入口类需有**无参构造函数**。每个能力条目还需 `type` 与 `assembly`（含这些类的程序集）；含代码时顶层 `sdk-version` 必填。严格按下方《事实清单》的 schema 与接口签名生成，不要臆造 API。

---

## 事实清单

### 包结构
- 插件包 = 一个文件夹；根目录必须有 `description.json`。
- 发布格式 `.tlx` = zip，`description.json` 在 zip 根目录。
- 私有依赖（第三方/原生库）放进包文件夹随包分发；SDK 程序集**不要**放进包。

### description.json schema
包级字段（顶层）：
- `id` (string, **必填**) — 唯一标识，反向域名。**有 id ⇒ V1**。
- `name` (string, **必填**) — 展示名。
- `version` (string) — semver，默认 `"1.0.0"`。
- `author` (string)、`description` (string) — 展示在扩展侧边栏：`author` 显示在卡片上，`description` 在卡片悬浮 tooltip 里。
- `icon` (string, 选填) — 包内相对路径的图标，位图（`.png`/`.jpg` 等）或矢量（`.svg`）均可，在侧边栏卡片**原样展示**（宿主不加背景/不裁圆角，圆角与透明由图标自定）；建议方形（≥64×64）。省略则用名称首字母占位。
- `sdk-version` (string, 含代码插件**必填**) — 如 `"1.0"`；宿主校验「插件要求 ≤ 宿主提供」。

插件级字段（一个条目 = 一个具体能力，身份内联）。单插件写在顶层；多插件放进 `extensions[]` 数组的每个元素：
- `type` (string, **必填**) — `"format"` | `"voice"` | `"effect"` | `"agent-model"` | 资源类（如 `"voicebank"`）。
- `engine` (string, voice/effect/agent-model **必填**) — 引擎类型 **id**（唯一、**不可变**、写进工程序列化，绝不本地化）。
- `extension` (string, format **必填**) — 文件扩展名 **id**（不带点；同属不可变身份）。
- `name` (string, 选填) — **显示名**（UI 用，可与 id 不同、可翻译）；省略则 UI 退回显示 id。
- `localizations` (object, 选填) — 翻译 `name`，如 `{ "zh-CN": { "name": "增益" } }`。
- `classes` (string[], 含代码**必填**) — **入口候选类全名数组**。宿主把数组里的类都扫一遍，按本 `type` 所需接口逐个匹配、命中即注册：voice→`IVoiceEngine` / effect→`IEffectEngine` / agent-model→`IAgentModelEngine`（首个命中者）；format→`IImportFormat`(注册导入)+`IExportFormat`(注册导出)，各扫一遍、至少命中其一，同一类可同实现两接口。无需精确登记哪个类干哪件事——列上候选、宿主按接口认领。每个候选类需无参构造函数。
- `assembly` (string, 含代码**必填**) — 含上述候选类的单个程序集（相对包根，所有候选类同居此程序集）；资源包不写。
- `platforms` (string[], 选填) — 如 `["win","osx","linux"]` 或 `["win-x64"]`；空=全平台。
- **身份 id vs 显示名**：`engine`/`extension` 是身份（注册键 + 序列化引用，不可变）；`name`/`localizations` 仅 UI 展示、可改可译。

规则：
- 有 `extensions[]` → 以它为准，顶层身份字段忽略。
- 无 `extensions[]`（简写）→ 顶层身份字段即那唯一插件；此时顶层 `name`/`localizations` **同时**是包名与该条目显示名（共用，**不要写两个 `name`**——同对象重复键会互相覆盖）。要让包名与条目显示名各不相同，改用 `extensions[]`、给条目单独写 `name`。
- 一个程序集多引擎/格式 → `extensions[]` 逐条列（同 `assembly`、各自 `engine`/`extension` + `classes`）。
- 身份在 manifest 单一真相，代码里**不写 attribute**；宿主扫 `classes` 候选类、按本 `type` 所需接口认领并实例化，不反射扫描。
- 无 `id` 的 manifest（或无文件）= **Legacy**，不要按此生成新插件。

### 工程配置
- `<TargetFramework>net8.0</TargetFramework>`（固定）。
- 引用：`TuneLab.Foundation`、`TuneLab.SDK`（单一程序集覆盖 format/voice/effect 全部插件类型）。
- **禁止**引用 `TuneLab.Hosting.Foundation`、`TuneLab`（主程序）、或任何 `TuneLab.Extensions.*`（那是 Legacy）。
- SDK 引用 `Private=false`（不复制输出、不随包分发）。

### Format 接口（命名空间 `TuneLab.SDK`）
```csharp
public interface IImportFormat { ProjectInfo Deserialize(Stream stream); }
public interface IExportFormat { Stream Serialize(ProjectInfo info); }
```
- 扩展名与实现类在 manifest：`{ "type":"format", "extension":"ext", "classes":["Ns.Importer","Ns.Exporter"], "assembly":"X.dll" }`（`extension` 不带点；`classes` 里至少有一个实现 `IImportFormat` 或 `IExportFormat`，可一类同实现两者）。
- 工程模型在 `TuneLab.SDK`（`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…）。实现类需无参构造函数。

### Voice 接口（命名空间 `TuneLab.SDK`）
> voice = **会话托管厚模型**：每种引擎一个 `IVoiceEngine`；宿主为每条 MidiPart 建一个 `ISynthesisSession`，
> 引擎承担声明（自动化轨/回显轨/属性面板，纯函数、不依赖会话）；会话承担默认歌词 + 逐步合成 + 产物（音高/回显/音素/音频/状态）。
> 时间量一律**全局秒**（tick 不外露）。
```csharp
public interface IVoiceEngine {
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; }   // 声库目录；须立即返回不阻塞（Init 期缓存）
    void Init();                                          // 无参；包目录经 Assembly.Location 自定位；失败抛异常
    void Destroy();
    ISynthesisSession CreateSession(string voiceId, ISynthesisContext context);   // 每 part 一个会话；context 随会话同生死
    // 声明均为 f(voiceId, part 当前值) 的纯函数（不碰会话运行时状态；voiceId 经 context.VoiceId）；宿主在参数 commit 时重算并 diff 到 UI（轨/控件可随参数显隐）。
    // 置于引擎而非会话：宿主在【建会话之前】即可求出声明，于是会话构造期 context.TryGetAutomation 对自己声明的轨必成（无构造期时序死环）。
    IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context);            // 自动化轨（连续/分段同一 map，DefaultValue=NaN ⇒ 分段）
    IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context);  // 只读回显轨声明（分段形 DefaultValue=NaN）
    ObjectConfig GetPartPropertyConfig(IPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(INotePropertyContext context);
}
public interface ISynthesisSession : IDisposable {
    string DefaultLyric { get; }   // 新建 note 默认歌词（会话级运行时取值；声明类 config 全在引擎上）
    // 调度（宿主驱动逐步合成）：peek 窗内下一待合成块（纯值边界、无副作用、null=窗内无待合成）→ 宿主调 SynthesizeNext 合成该块。
    SynthesisRange? GetNextSegment(double startTime, double endTime);  // 返回纯调度提示 SynthesisRange
    Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default);  // 入参=选中它那次 peek 的同一窗口（按它确定性重导出同一块，非回灌 SynthesisRange）；纯 Task：取消正常返回不抛 OCE；错误抛异常
    // 产物（数据线程发布、发布即不可变、StatusChanged 单一刷新信号）：
    SynthesizedPitch SynthesizedPitch { get; }                                 // 具名富类型 { Segments }；空=new(){Segments=[]}
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }  // 回显曲线数据，key 对齐 GetSynthesizedParameterConfigs
    IReadOnlyMap<ILiveNote, IReadOnlyList<SynthesisPhoneme>> SynthesizedPhonemes { get; }  // 按归属 note 键（origins[i]）；只报时长、无绝对位置；无主音素无契约
    IReadOnlyList<SynthesisStatusSegment> GetStatus();                         // 按段状态/进度/报错
    event Action? StatusChanged;                                              // 产物/状态有更新（任意线程触发，宿主 marshal）
}
public interface ISynthesisContext {        // 会话级输入活视图（宿主实现、随会话死、仅数据线程访问）
    IReadOnlyNotifiableLinkedList<ILiveNote> Notes { get; }   // 可重叠（和弦）、排序 StartTime↑→EndTime↓→插入序；去重叠是插件责任
    IReadOnlyNotifiablePropertyObject PartProperties { get; }
    bool TryGetAutomation(string key, out ILiveAutomation automation);
    ILiveAutomation Pitch { get; }             // 绝对约束（分段：有值=钉死、NaN=自由）
    ILiveAutomation PitchDeviation { get; }    // 加性偏差（连续、默认 0、永不 NaN；vibrato 等汇于此）。finalPitch=resolve(Pitch)+PitchDeviation
    SynthesisSnapshot GetSnapshot(IReadOnlyList<ILiveNote> notes, double startTime, double endTime);  // 仅 SynthesizeNext 同步前缀（offload 前）主动拉，可拉多份
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);             // 申请音频段握柄
    event Action? Committed;                   // 逻辑编辑收口（单条编辑也补发）；廉价标脏、此处一次性做重活
}
public interface IAudioSegment : IDisposable {   // Dispose=删段（重分片/改长度位置时重建）
    void Write(int offset, ReadOnlySpan<float> samples);   // 段内就地写（span 借用语义）；offset=段内相对采样位置
    void Commit();                                         // 送 effect 的唯一闸门；Commit 前的写只供进度/波形
}
```
快照与产物类型（`SynthesizeNext` 同步前缀物化、worker 只读；全是不可变值、可跨线程）：
```csharp
public sealed class SynthesisSnapshot {                    // context.GetSnapshot(notes, startTime, endTime) 的返回体
    IReadOnlyList<SynthesisNoteSnapshot> Notes { get; }    // 与递入 notes 【索引对齐】（产物归属契约）；邻居按索引导航、无 Next/Last
    SynthesisAutomationSnapshot Pitch { get; }             // 绝对音高约束（分段：有值=钉死、NaN=自由）
    SynthesisAutomationSnapshot PitchDeviation { get; }    // 加性偏差（连续、默认0、永不NaN）
    PropertyObject PartProperties { get; }                 // part 参数值拷
    bool TryGetAutomation(string key, out SynthesisAutomationSnapshot automation);  // 取声明过的可编辑轨
}
public sealed class SynthesisNoteSnapshot {                // 触底值类型、无活引用
    double StartTime { get; } double EndTime { get; }      // 全局秒
    int Pitch { get; } string Lyric { get; }
    IReadOnlyList<SynthesisPhoneme> Phonemes { get; }      // 物化副本（时长/权重/IsLead，位置不存）
    PropertyObject Properties { get; }                     // per-note 参数值拷（GetDouble/GetBool/GetInt/GetString/GetEnum 读，稀疏、读不到取默认）
}
public sealed class SynthesisAutomationSnapshot { IAutomationEvaluator Evaluator { get; } }
public interface IAutomationEvaluator { double[] Evaluate(IReadOnlyList<double> times); }  // times=全局秒；插值在宿主侧；连续轨永不NaN、分段轨段间NaN

// 音素描述符：进/出同型、方向无关；只报时长，位置/去重叠/melisma 全归宿主派生（引擎报绝对位置会让相接判据失真）。
public struct SynthesisPhoneme {
    string Symbol;
    double Duration;        // 辅音(w=0)固定长；核(w>0)此值被布局忽略（恒按填充派生）
    double StretchWeight;   // 0=刚性辅音 / >0=可伸核·元音（先让）；无音韵学知识填 Duration（均匀缩放）；Σw≤0 退化均匀
    bool   IsLead;          // 前置音素（核之前的引导辅音）：前置往左累积、核+后辅音往右
}
public struct VoiceSourceInfo { string Name; string Description; ImageResource? Portrait; }  // Portrait 用 FileImageResource(绝对路径)，null=无
public sealed class SynthesizedPitch { IReadOnlyList<IReadOnlyList<Point>> Segments { get; } }      // 音高回显：分段折线，段内 Point=(秒,半音)；与 SynthesizedParameter 有意不共类型
public sealed class SynthesizedParameter { IReadOnlyList<IReadOnlyList<Point>> Segments { get; } }  // 回显曲线：分段折线，段内 Point=(秒,值)，段间断开
```
- 引擎 id 与实现类在 manifest：`{ "type":"voice", "engine":"id", "classes":["Ns.MyVoiceEngine"], "assembly":"X.dll" }`（`engine` 唯一；宿主在 `classes` 找实现 `IVoiceEngine` 的类）。实现类需无参构造函数。
- 声明在引擎（gap：declaration timing）：4 个 GetConfig 在 `IVoiceEngine` 上、是 `f(voiceId, 选中成员当前值)` 纯函数。`IPartPropertyContext { string VoiceId; IReadOnlyList<PropertyObject> PartProperties }`（part 面板，可多选 part；多声库经 `VoiceId` 分流）。`INotePropertyContext`**独立不继承** `{ string VoiceId; PropertyObject PartProperties; IReadOnlyList<PropertyObject> NoteProperties }`——note 必属单 part 故 PartProperties 单数、NoteProperties 是各选中 note 列表。列表成员不在乎多选就 `context.NoteProperties.Merge()`（`PropertyMerge` 扩展方法@`TuneLab.Foundation`）还原成单个三态 `PropertyObject` 按单选写；要逐成员真值就遍历列表。voiceId 进 context 使 voice 的 context 与 effect 的 `IEffectPropertyContext`（无对等物）永久分叉。**会话构造期即可订阅自己声明的轨**：宿主在建会话前已据引擎声明填好轨集合，故 `context.TryGetAutomation(key,…)` 对你声明过的轨必成；漏订则该轨绘制后不触发重渲。
- 调度语义：一会话同时只合成一块；并行发生在不同 part 的不同会话间，并发上限由宿主管控。取消是正常调度结局（不抛 `OperationCanceledException`）；`await` 真正返回才释放槽位。
- 线程纪律：context（Notes/属性/automation）、`GetSnapshot`、`CreateAudioSegment` 仅可在 `SynthesizeNext` **同步前缀**（数据线程）读/调；之后 offload 只读已物化的 `SynthesisSnapshot`（不可变、可跨线程）。产物与 `CreateAudioSegment` 写入/Commit 在数据线程。
- 命名纪律：`ILive*`=活视图（仅数据线程）、`*Snapshot`=冻结物（可跨线程、无事件）。
- 快照（gap：snapshot）：`GetSnapshot(notes, startTime, endTime)` 仅在 `SynthesizeNext` 同步前缀（offload 前、数据线程）调，可拉多份；`notes`=段内+协同发音邻居（你自由圈定），`snapshot.Notes` 与之【索引对齐】=产物归属依据。automation 是冻结求值器（`Evaluator.Evaluate(times)`），不是裸点；想前缀采好就同步前缀 Evaluate 成 double[] 自存再 offload。快照不可变、只写一次；数据变了→标脏→下次拉全新快照（替换而非同步，无锁）。
- 音高（gap：pitch）：双通道 `finalPitch(t)=resolve(Pitch(t))+PitchDeviation(t)`。`Pitch` 分段（有值=用户钉死必守、NaN=自由区你自己生成，典型回退 note.Pitch）；`PitchDeviation` 连续永不NaN（vibrato 等汇于此，加在解析后绝对面上，自由区也生效）。按控制率布点→批量 `Evaluator.Evaluate(times)`→NaN处回退 note.Pitch 再加 deviation→逐采样插值。音高回显走 `SynthesizedPitch`（具名类型 `{ Segments }`，分段折线 Point=(秒,半音)；空=`new(){Segments=[]}`）。
- 音素（gap：phoneme I/O）：进/出**同一类型** `SynthesisPhoneme`（只报 Symbol/Duration/StretchWeight/IsLead，**无绝对位置**——定位/去重叠/melisma 全归宿主派生）。输入 `note.Phonemes` 整note钉死（非空=全钉、引擎守约束；空=从 Lyric 做 G2P+自由定时；不支持部分钉死）。输出 `SynthesizedPhonemes` = **按归属 note 键的 map**（键=按快照索引从你递入的活 note 列表 `origins[i]` 回取，仅身份token、合成中不读其属性）；脏/合成中的块不报其 note 音素（宿主留白）。**无「无主音素」契约**（时长模型下无锚不可定位）。核时长由宿主填充派生（报多少无所谓）、辅音填固定长；`StretchWeight` 无知识填 `Duration`。preview 纯显示、绝不当约束回喂。
- 属性约定（gap：note/part props）：per-note/part 专属参数全走 keyed `Properties`（不动 `ILiveNote` 固定面）——`GetNotePropertyConfig`/`GetPartPropertyConfig` 的 `ObjectConfig.Properties`（`OrderedMap<string, IControllerConfig>`）声明，合成时从 `SynthesisNoteSnapshot.Properties`/`snapshot.PartProperties` 用 `GetDouble/GetBool/GetInt/GetString/GetEnum(key, default)` 读（稀疏、读不到取默认）。控件：`SliderConfig`/`ComboBoxConfig`(值显分离)/`CheckBoxConfig`/`TextBoxConfig`。`AutomationConfig.DefaultValue=NaN⇒分段轨`。条件轨消失后宿主保留已画曲线（隐藏不删）。`VoiceSourceInfo.Portrait` 用 `FileImageResource(绝对路径)`。
- 原生依赖打包（gap：native/ONNX）：私有依赖（第三方托管库、native dll/so/dylib、模型、dict）放包文件夹→进本包专属 ALC（与其他插件隔离、版本不冲突）；SDK 程序集与 .NET 运行时由宿主共享、勿打进包。定位包内资源用 `Path.GetDirectoryName(typeof(MyVoiceEngine).Assembly.Location)`（勿用工作目录/`AppContext.BaseDirectory`=宿主目录）。native dll 与托管 dll 同目录便于 P/Invoke 探测；跨平台按目标分别提供 + manifest `platforms` 过滤。大模型权重勿塞 `.tlx`（即装即载会很重）：用独立资源包，或实现 `IExtensionSettings` 让用户配模型路径（密钥用 `TextBoxConfig{IsPassword=true}`）。加载放 `Init`、失败抛异常（宿主优雅降级）。
- 失效自管：构造订阅 context（`Notes` 增删用 `WhenAny` 自动接线 / `note.*.Modified` 字段 / `PartProperties.Modified` / `Pitch`+`PitchDeviation`+各轨 `RangeModified`(秒区间)）handler 只廉价标脏，重活（重分块）推迟到 `context.Committed`（逻辑编辑收口、单条也补发→批量编辑只重分块一次）。tempo 变化无独立信号（分解为 note 边界 `Modified` + 轨 `RangeModified`）。`Dispose` 退订 + Dispose 所有音频段。重叠 note(和弦) 分块判间隙用「组内最大结束」、块尾取 `Max(EndTime)`。
- 相关类型：`ISynthesisSession`、`ISynthesisContext`、`SynthesisRange`、`SynthesisSnapshot`、`SynthesisNoteSnapshot`、`SynthesisAutomationSnapshot`、`IAutomationEvaluator`、`ILiveNote`、`ILiveAutomation`、`IAudioSegment`、`SynthesisPhoneme`、`SynthesizedPitch`、`SynthesizedParameter`、`VoiceSourceInfo`、`FileImageResource`、`AutomationConfig`、`IPartPropertyContext`、`INotePropertyContext`、`SynthesisStatusSegment`。

### Effect 接口（命名空间 `TuneLab.SDK`）
> effect = 对**整段已合成音频**的离线变换（如 SVC 换声），非实时 VST。**会话托管厚模型**：每种引擎一个 `IEffectEngine`；
> 宿主为每条「effect 实例 × 一个上游音频段」建一个持久厚 `IEffectProcessor`，持本段 `IEffectContext`、**自订阅自管失效与重处理**。
```csharp
public interface IEffectEngine {
    // 条件声明：均为当前参数值（context.Properties）的纯函数，宿主在参数 commit 时按当前值重算并 diff 到 UI（控件/轨可随参数显隐）。静态的忽略 context 返回固定值。
    ObjectConfig GetPropertyConfig(IEffectPropertyContext context);                                              // 参数面板
    IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context);          // 自动化轨（连续/分段，DefaultValue=NaN⇒分段）
    IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IEffectPropertyContext context);// 只读回显轨声明（分段形 DefaultValue=NaN）；无回显返回空 map
    void Init();                                          // 无参；包目录经 Assembly.Location 自定位；失败抛异常
    void Destroy();
    IEffectProcessor CreateProcessor(IEffectContext context);   // 每「effect 实例 × 上游音频段」一个厚处理器；context 由宿主实现
}
public interface IEffectProcessor : IDisposable {
    // (重)处理本段：同步前缀（数据线程）抓 Input.Samples 引用 + 预采参数/自动化值，之后才 offload；产物经 context.CreateAudioSegment 写出并 Commit。
    // 返回纯 Task：取消正常返回不抛 OperationCanceledException；错误抛异常（宿主 catch → 该段 passthrough 降级）。无内部增量可做就任何信号→整段重处理。
    Task Process(CancellationToken cancellation = default);
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }   // 本段回显曲线数据，key 对齐 GetSynthesizedParameterConfigs；数据线程发布、宿主只读；无回显返回空 map
    event Action? ProcessingRequested;                    // 处理器自标脏后触发（恒数据线程）；宿主据此调度 Process
}
public interface IEffectContext {        // 本段输入上下文（宿主实现、随处理器死、仅数据线程访问）
    IUpstreamAudioSegment Input { get; }                  // 本段输入（上游 voice 输出，或链上前一 effect 输出），整段不可分割
    IReadOnlyNotifiablePropertyObject Properties { get; } // 该 effect 自身参数活视图（订阅 Modified 标脏）
    bool TryGetAutomation(string key, out ILiveAutomation automation);   // 该 effect 一条自动化轨活视图（查询轴=全局秒）
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);  // 产出段（可重分段、一进多出、采样率随段走）
    event Action? Committed;                              // 逻辑编辑收口：颗粒脏标完后一次性触发，处理器在此判脏触发 ProcessingRequested
}
public interface IUpstreamAudioSegment {                  // 上游音频段只读视图（已提交版本 PCM 不可变）
    long SampleOffset { get; } int SampleCount { get; } int SampleRate { get; }
    ReadOnlyMemory<float> Samples { get; }                // 同步前缀抓引用、worker 直读
    int CommitVersion { get; }                            // 重 Commit 递增，处理器据此判是否需重处理
    event Action? Committed;
}
public interface IEffectPropertyContext { PropertyObject Properties { get; } }   // GetPropertyConfig/GetAutomationConfigs/GetSynthesizedParameterConfigs 求值上下文
```
- 引擎 id 与实现类在 manifest：`{ "type":"effect", "engine":"id", "classes":["Ns.MyEffectEngine"], "assembly":"X.dll" }`（`engine` 唯一；宿主在 `classes` 找实现 `IEffectEngine` 的类）。实现类需无参构造函数。
- 失效自管：处理器构造时订阅 `Input.Committed` / `Properties.Modified` / 各 automation `RangeModified` 自算 dirty，于 `context.Committed` 一次性触发 `ProcessingRequested`；宿主据此调度 `Process`。与本段无关的变化 → 不标脏 → 本段输出不变、下游被跳过。
- 输出经握柄：`context.CreateAudioSegment(offset,count,rate)` 申请段，`Write` + `Commit`；段几何固定（位置/长度需变则 Dispose 重建）。
- 一个 MidiPart 上多个 effect 按声明顺序串行，上一个输出是下一个输入；链尾各段按绝对时间混音。启用/顺序由用户管理。
- 回显（可选）：`GetSynthesizedParameterConfigs` 声明 + `IEffectProcessor.SynthesizedParameters` 承载本段数据（宿主把同一 effect 各段按 key 拼接呈现），与 voice 回显同构、只读。
- 线程纪律：`context`（`Input`/`Properties`/automation）仅可在 `Process` **同步前缀**（数据线程）读；offload 后只读已物化的不可变值；`SynthesizedParameters` 与输出段在数据线程发布。

### 扩展设置（IExtensionSettings，命名空间 `TuneLab.SDK`）
> per-extension（per 能力实现者，如每个 voice/effect 引擎一份；安装包 ExtensionPackage 可含多个 extension）、随宿主持久化、跨工程共享的设置（API key / 模型路径 / 设备等）。**opt-in**：能力实现类**额外实现** `IExtensionSettings` 即接入；宿主 `x is IExtensionSettings` 探测，在「设置 > 扩展」分页渲染、按 extension 落盘、运行时回喂。
> 与属性面板（voice `GetPartPropertyConfig`/`GetNotePropertyConfig`、effect `GetPropertyConfig`）**不同**：那些是随工程序列化的实例/段级属性；本接口是扩展自身配置、单独落盘、与工程无关。两者复用同一控件配置词汇 `ObjectConfig`。
```csharp
public interface IExtensionSettings {
    ObjectConfig GetSettingsConfig(IExtensionSettingsContext context); // 声明 schema；是 context.Settings 当前值的纯函数（可条件显隐）、【Init 前可调】；密钥字段用 TextBoxConfig{IsPassword=true}
    void ApplySettings(PropertyObject settings);                       // 宿主回喂持久值：加载后一次（早于 Init）+ 用户保存后一次。实现者自存自用于 Init/CreateSession/CreateProcessor
}
public interface IExtensionSettingsContext { PropertyObject Settings { get; } } // 当前已填设置值（条件显隐据此）
```
- 任意能力（voice/effect…）可在其接口外**再实现** `IExtensionSettings`，如 `class MyVoiceEngine : IVoiceEngine, IExtensionSettings`。agent 模型引擎有独立侧边栏设置、不走此路。
- **动态项**：`GetSettingsConfig(context)` 据 `context.Settings`（当前值）返回 config，用户改值后宿主重算并 diff 到控件——可据已填值显隐字段。静态面板忽略 context 返回固定 config。
- 密钥：`TextBoxConfig { IsPassword = true }` → 宿主掩码显示 + 安全落盘（Win=DPAPI 密文就地 / macOS=钥匙串；无安全存储则**不保存该字段、绝不明文**+告警；官方支持 Win/macOS）。值类型仍是普通 string。
- 读值：`settings.GetString(key, default)` / `GetDouble` / `GetInt` / `GetBool(key, default)`。读不到按默认 fallback。
- `DisplayText` 由插件自译（按 `TuneLabContext.Global.Language`）；manifest **不**声明设置（纯代码）。
- 控件配置类型：`TextBoxConfig`（`DefaultValue`/`IsPassword`）、`CheckBoxConfig`（`DefaultValue`）、`ComboBoxConfig`、`SliderConfig`；容器 `ObjectConfig { Properties = OrderedMap<string, IControllerConfig> }`。

### 常见错误（避免）
- ❌ 漏 `id` → 被当成 Legacy。
- ❌ 引用 `TuneLab.Hosting.Foundation` 或主程序 → 不是插件契约，加载会出错。
- ❌ 把 SDK 程序集打进包 → 与宿主共享版本冲突，应共享宿主的。
- ❌ 目标框架非 net8.0。
- ❌ 插件类缺无参构造函数 → 无法实例化。
- ❌ 臆造 SDK API / 接口成员 → 严格按上方签名。

---

## 最小完整示例（format）

`description.json`：
```json
{
  "id": "com.example.myfmt",
  "name": "My Format",
  "version": "1.0.0",
  "sdk-version": "1.0",
  "type": "format",
  "extension": "myfmt",
  "classes": ["Example.MyFormat.MyFormatImporter", "Example.MyFormat.MyFormatExporter"],
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

`MyFormat.cs`（类全名要列进 manifest 的 `classes`；宿主按 `IImportFormat`/`IExportFormat` 接口认领；不写 attribute）：
```csharp
using System.IO;
using TuneLab.SDK;

namespace Example.MyFormat;

public class MyFormatImporter : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        // TODO: 解析 stream 填充 project
        return project;
    }
}

public class MyFormatExporter : IExportFormat
{
    public Stream Serialize(ProjectInfo info)
    {
        var stream = new MemoryStream();
        // TODO: 把 info 写进 stream
        stream.Position = 0;
        return stream;
    }
}
```
