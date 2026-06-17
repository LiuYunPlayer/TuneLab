# TuneLab 插件开发指南

> 适用于 TuneLab 新版扩展系统（V1）。本文介绍如何编写、打包、发布一个 TuneLab 插件。
> 老插件（Legacy）的兼容说明见文末「附录：Legacy 插件」。

---

## 1. 核心概念

- **插件包 = 一个文件夹**。它是部署、安装、卸载的原子单位，被放进 TuneLab 的扩展目录（见 §7）。
- **`description.json` 是包的身份标识**，必须放在包文件夹的**最外层**。新版（V1）插件**必须**带它；TuneLab 先读这个文件，再据其内容**有选择地**加载包内程序集——不会盲目扫描整个文件夹。
- **一个包可以含多个插件**。若你有一套共用的基建程序集，可以把基于它开发的多个插件打进同一个包，基建只分发一份、运行时也只加载一份（它们共享同一个加载上下文）。
- **插件类别（type）**：当前支持 `format`（工程文件导入/导出）、`voice`（歌声合成引擎）与 `effect`（效果器，对合成音频做整段离线变换，如换声）。包还可以是纯**资源包**（无代码，如音色资源）。

每个插件包在加载时被放进一个**独立的 AssemblyLoadContext（ALC）**：

- TuneLab 的 SDK 契约程序集（`TuneLab.Foundation` + `TuneLab.SDK.*`）和 .NET 运行时由**主程序统一提供、所有插件共享**——你引用它们即可，**不要**把它们打进包里。
- 你的**私有依赖**（第三方库、原生 dll 等）放进包文件夹，会被加载进你这个包专属的 ALC，**与其他插件隔离**，因此不同插件捆绑不同版本的同一个库**不会冲突**。

---

## 2. description.json

### 2.1 字段

包级（最外层）：

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | ✅ | 包的唯一标识，建议用反向域名，如 `com.example.myplugin`。**它也是 V1 的判别标志**——有 `id` 即按新版加载。 |
| `name` | ✅ | 展示名 |
| `version` | | 包版本（semver），默认 `1.0.0` |
| `author` | | 作者（展示在扩展侧边栏） |
| `description` | | 一句话简介（展示在扩展侧边栏） |
| `icon` | | 包内相对路径的图标，位图（`.png`/`.jpg` 等）或矢量（`.svg`）均可。**原样展示**——侧边栏不给它加背景、不裁圆角，所以圆角/透明/留白都由你自定（想要圆角就自己画进图标）。建议提供**方形**图标（如 64×64 及以上）。省略则侧边栏用名称首字母 + 深色圆角方块占位。 |
| `sdk-version` | 含代码插件时✅ | 你编译时使用的 SDK 版本（如 `"1.0"`）。TuneLab 据此做兼容校验：插件要求的版本高于宿主提供的版本则被跳过。资源包可省略。 |

插件级（描述「这个包提供什么」）：

| 字段 | 必填 | 说明 |
|---|---|---|
| `type` | ✅ | 类别：`format` / `voice` / `effect` / 资源类 |
| `assemblies` | | 要加载并扫描的程序集（相对包文件夹的路径）。**写了**→只加载并扫描这几个（更快、更明确）；**没写**（代码插件）→扫描包内全部 `*.dll` 找入口；资源包不填。 |
| `platforms` | | 平台过滤，如 `["win", "osx", "linux"]` 或带架构 `["win-x64"]`。留空 = 全平台。 |

### 2.2 单插件（最常见）

直接把插件级字段写在顶层即可，不需要数组：

```json
{
  "id": "com.example.myformat",
  "name": "My Format",
  "version": "1.0.0",
  "author": "Example",
  "description": "Import/export .myfmt files",
  "sdk-version": "1.0",
  "type": "format",
  "assemblies": ["MyFormat.dll"]
}
```

### 2.3 一包多插件

用 `extensions[]` 数组，每个元素是一个独立插件的元数据。包级字段（id/name/version/author/description/sdk-version）写在顶层、共用：

```json
{
  "id": "com.example.suite",
  "name": "Example Suite",
  "version": "2.0.0",
  "sdk-version": "1.0",
  "extensions": [
    { "type": "format", "assemblies": ["Example.Format.dll"] },
    { "type": "voice",  "assemblies": ["Example.Voice.dll"], "platforms": ["win"] }
  ]
}
```

> `Example.Format.dll`、`Example.Voice.dll` 可以共同引用同一个 `Example.Common.dll`（放进包里），它只需分发一份。
>
> 规则：有 `extensions[]` 时以它为准，顶层的 `type`/`assemblies` 被忽略。

### 2.4 资源包（无代码）

省略 `assemblies`，用 `type` 声明用途。TuneLab 只登记它、不加载代码，由对应引擎在运行时去发现包内资源：

```json
{
  "id": "com.example.mybank",
  "name": "My Voicebank",
  "version": "1.0.0",
  "type": "voicebank"
}
```

---

## 3. 工程配置

新建一个 .NET 类库工程，引用 SDK 程序集：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>   <!-- SDK ABI 地板，锁 net8 -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <!-- 引用你需要的 SDK 程序集；这些由 TuneLab 提供，打包时不要带上 -->
    <Reference Include="TuneLab.Foundation" />
    <Reference Include="TuneLab.SDK" /> <!-- format/voice/effect 全部插件类型同一程序集 -->
  </ItemGroup>
</Project>
```

规则：

- **目标框架锁 `net8.0`**（SDK 的 ABI 地板）。宿主未来升级 .NET 时，按此地板编译的插件无需重编即可运行。
- **只引用 `TuneLab.Foundation` 和 `TuneLab.SDK`**。**不要**引用 `TuneLab.Hosting.Foundation` 或主程序——它们不是插件契约。
- SDK 程序集设为「不复制到输出」（`Private=false` / Copy Local = No），打包时**别把它们放进包**，宿主会共享自己的那一份。
- 你的**私有第三方依赖**正常引用、随包分发即可。

---

## 4. 编写 Format 插件

实现 `IImportFormat`（导入）和/或 `IExportFormat`（导出），用 attribute 声明文件扩展名。需要**无参构造函数**。

```csharp
using System.IO;
using TuneLab.SDK;

[ImportFormat("myfmt")]              // 文件扩展名（不带点）
public class MyFormatImporter : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        // 把 stream 解析成 TuneLab 工程模型
        var project = new ProjectInfo();
        // ... 填充 project ...
        return project;
    }
}

[ExportFormat("myfmt")]
public class MyFormatExporter : IExportFormat
{
    public Stream Serialize(ProjectInfo info)
    {
        var stream = new MemoryStream();
        // ... 把 info 写进 stream ...
        stream.Position = 0;
        return stream;
    }
}
```

工程模型（`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…）定义在 `TuneLab.SDK`。

---

## 5. 编写 Voice 插件

voice 是**歌声合成引擎**（如 SVS 模型）。它是**会话托管厚模型**：实现 `IVoiceEngine`（每种引擎类型一个，`[VoiceEngine("type")]` 声明、需无参构造函数），宿主为工程里**每条 MidiPart** 调 `CreateSession` 建一个 `ISynthesisSession`——会话承担声明（默认歌词 / 自动化轨 / 回显轨 / 属性面板）、宿主驱动的逐步合成、以及产物（音高 / 回显 / 音素 / 音频 / 状态）。会话订阅宿主递入的 `ISynthesisContext`（该 part 的输入活视图）自管失效。插件侧时间量一律**全局秒**（tick 是宿主乐谱内部表示、不外露）。

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

[VoiceEngine("MyEngine")]            // 引擎类型标识（唯一）
public class MyVoiceEngine : IVoiceEngine
{
    // 声库目录（菜单/选择器用）：必须立即返回不阻塞——应在 Init 期扫描缓存，get 仅返回缓存引用。
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    // 无参：包目录经 Assembly.Location 自定位（无需宿主递路径）。失败直接抛异常，宿主在调用边界 catch。
    public void Init() { /* ... 扫描声库、加载模型 ... */ }
    public void Destroy() { /* 释放资源 */ }

    // 每条 part 一个会话：voiceId 选定声库（VoiceSourceInfos 的 key），context 为该 part 输入活视图、随会话同生死。
    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context) => new MySession(voiceId, context);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}
```

会话是合成的主体。宿主**驱动逐步合成**：先用 `GetNextSegment(startTime, endTime)` peek 窗内"下一块待合成"的纯值边界（无副作用、`null` = 窗内无待合成），再调 `SynthesizeNext` 合成那一块。`SynthesizeNext` 的**同步前缀**（数据线程）经 `context.GetSnapshot(...)` 拉取本次所需的不可变快照，之后才 offload 到后台线程算——worker 只读快照、永不回碰活视图。产物经 `context.CreateAudioSegment` 写出音频，曲线/音素经会话属性发布，状态变化经 `StatusChanged` 通知宿主重读。

```csharp
class MySession : ISynthesisSession
{
    public MySession(string voiceId, ISynthesisContext context)
    {
        mVoiceId = voiceId;
        mContext = context;
        // 订阅 context（Notes 增删 / 属性 / automation.RangeModified）标脏，在 context.Committed 一次性收口、做重活（如重分块）。
        mContext.Committed += OnCommitted;
    }

    public string DefaultLyric => "a";

    // 声明均为当前 part 参数值的纯函数（context 驱动）：宿主在参数 commit 时重算并 diff 到 UI（轨/控件可随参数显隐）。
    // 连续轨与分段轨同在一张 map（AutomationConfig.DefaultValue 为 NaN ⇒ 分段轨）。
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context) => mAutomationConfigs;
    // 只读回显轨声明（引擎产出的只读曲线，如 energy；分段形 DefaultValue=NaN，自带 DisplayText/Min/Max/Color）。无回显返回空 map。
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context) => mReadbackConfigs;
    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => mPartConfig;
    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => mNoteConfig;

    // peek：窗内下一待合成块的秒边界（确定性分片，数据线程上廉价执行）。
    public SynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        // ... 基于 mContext.Notes 做分片决策，返回下一块的 [start,end]，或 null ...
        return null;
    }

    // 合成 peek 报出的这一块：同步前缀拉快照，之后 offload。返回纯 Task（取消正常返回、不抛 OperationCanceledException；错误抛异常）。
    public async Task SynthesizeNext(SynthesisSegment segment, CancellationToken cancellation = default)
    {
        // —— 同步前缀（数据线程）：圈定本块所需 note（段内 + 协同发音邻居），拉不可变快照 ——
        var notes = /* 从 mContext.Notes 圈定 */ new List<ILiveNote>();
        var snapshot = mContext.GetSnapshot(notes, segment.StartTime, segment.EndTime);
        int rate = 44100;
        var output = mContext.CreateAudioSegment(/*sampleOffset*/ (long)(segment.StartTime * rate), /*sampleCount*/ rate, rate);

        // —— offload：worker 只读 snapshot 算 PCM ——
        var pcm = await Task.Run(() => Render(snapshot), cancellation);

        // —— marshal 回数据线程发布产物（换引用即不可变）——
        output.Write(0, pcm);
        output.Commit();                 // 送下游 effect 的唯一闸门
        mStatusChanged?.Invoke();
    }

    // 产物（数据线程发布、发布即不可变；StatusChanged 单一刷新信号）。
    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => mPitch;                          // 分段折线（秒, 半音）
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mReadback;          // 回显曲线，key 对齐 GetSynthesizedParameterConfigs
    public IReadOnlyList<SynthesizedPhoneme> Phonemes => mPhonemes;
    public IReadOnlyList<SynthesisStatusSegment> GetStatus() => mStatus;                            // 按段状态/进度/报错
    public event Action? StatusChanged { add => mStatusChanged += value; remove => mStatusChanged -= value; }

    void OnCommitted() { /* 廉价标脏后在此一次性重分块；StatusChanged 通知宿主 */ }

    public void Dispose() { mContext.Committed -= OnCommitted; /* 释放模型句柄等 */ }

    // ... 字段：mAutomationConfigs / mReadbackConfigs / mPartConfig / mNoteConfig / 产物缓存 / Render(...) ...
}
```

要点：

- **会话生命周期**：绑定一条 part，活到 part 删除（`Dispose`）；换声源时宿主丢弃旧会话、用新 `voiceId` 重建（context 随会话重建）。重模型加载应是懒的。
- **逐步合成**：一个会话同时只合成一块；并行发生在不同 part 的不同会话之间，并发上限由宿主管控。取消是正常调度结局（`await` 真正返回才释放槽位），**不要**抛 `OperationCanceledException`；进度经 `SynthesisStatusSegment.Progress` + `StatusChanged` 上报。
- **输入活视图 `ISynthesisContext`**：`Notes`（可重叠、和弦——去重叠是插件责任）、`PartProperties`、`TryGetAutomation`、`Pitch`（绝对约束、NaN=自由）+ `PitchDeviation`（加性偏差，`finalPitch = resolve(Pitch) + PitchDeviation`）。仅可在 `SynthesizeNext` 同步前缀（数据线程）读，并在那里 `GetSnapshot` 物化；offload 后只读快照。
- **音频产物**：`context.CreateAudioSegment(offset, count, rate)` 申请段，`Write` + `Commit`（Commit 是送下游 effect 的唯一闸门，Commit 前的写只供进度/波形）；采样率随段走，可逐段不同。
- **回显轨（可选）**：`GetSynthesizedParameterConfigs` 声明 + `SynthesizedParameters` 承载曲线数据，宿主作一等只读轨绘制（参数区填充面积、可独立显隐），与 effect 回显同构。
- **命名纪律**：`ILive*` = 活视图（仅数据线程）、`*Snapshot` = 冻结物（可跨线程、无事件）。

> ⚠️ **自动化参数名避开宿主保留名**：`GetAutomationConfigs` 的键会和宿主内置自动化合并展示，若与内置项**重名会被内置项占用、你的参数显示不出**。已知保留名：**`Volume`**、**`VibratoEnvelope`**。请用自己的独特名（如 `Breathiness` / `Growl` / 加前缀）。

---

## 6. 编写 Effect 插件

效果器（effect）对**已合成的整段音频**做变换。它面向**耗时较长的离线模型**（如 SVC 换声、神经音色转换），不是实时的 VST 式效果器。

实现 `IEffectEngine`，用 `[EffectEngine("type")]` 声明类型标识。需要**无参构造函数**。引擎是每种效果器类型一个；宿主为工程里每条「effect 实例 × 上游音频段」创建一个**持久厚处理器** `IEffectProcessor` 驱动它。处理器持有自己那一段的上下文 `IEffectContext`、**自订阅、自管失效与重处理**——引擎私有的失效图（哪条参数/哪段自动化标脏触发哪些内部重算）落在处理器内部，宿主无从复制，故为厚模型。

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

[EffectEngine("MyEffect")]            // 效果器类型标识（唯一）
public class MyEffectEngine : IEffectEngine
{
    // 参数面板 / 自动化轨 / 回显轨：均为当前参数值（context.Properties）的纯函数——宿主在参数 commit 时按当前值重算
    // 并 diff 到 UI，故控件/轨可随参数显隐（条件声明）。静态的忽略 context 返回固定值即可（如下例）。
    public ObjectConfig GetPropertyConfig(IEffectPropertyContext context) => mPropertyConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context) => mAutomationConfigs;

    // 合成参数回显轨声明（只读、独立于可编辑自动化轨）：处理产出的只读曲线（如 loudness）暴露为一等只读轨，
    // 分段形（DefaultValue=NaN）、自带 DisplayText/Min/Max/Color。无回显的引擎返回空 map 即可。
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IEffectPropertyContext context) => mReadbackConfigs;

    // 无参：包目录经 Assembly.Location 自定位（无需宿主递路径）。失败直接抛异常，宿主在调用边界 catch → passthrough 降级。
    public void Init() { /* ... 加载模型 ... */ }
    public void Destroy() { /* 释放资源 */ }

    // 每条「effect 实例 × 一个上游音频段」一个持久厚处理器；context 由宿主实现、暴露本段输入 + 参数/自动化 + 产出口 + 收口事件。
    public IEffectProcessor CreateProcessor(IEffectContext context) => new MyEffectProcessor(context);

    readonly ObjectConfig mPropertyConfig = new()
    {
        Properties = new OrderedMap<string, IControllerConfig>
        {
            { "amount", new SliderConfig { DefaultValue = 1.0, MinValue = 0.0, MaxValue = 2.0 } },
        },
    };
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, AutomationConfig> mReadbackConfigs = new()
    {
        { "loudness", new AutomationConfig { DisplayText = "Loudness", DefaultValue = double.NaN, MinValue = 0, MaxValue = 2, Color = "#00B0FF" } },
    };
}
```

处理器在构造时拿 `IEffectContext` 自订阅（`Input.Committed` / `Properties.Modified` / 各 automation `RangeModified`），自算 dirty，在 `context.Committed`（逻辑编辑收口）一次性触发 `ProcessingRequested`；宿主据此调度 `Process`。`Process` 的**同步前缀**（数据线程）抓 `Input.Samples` 引用 + 预采参数/自动化值，之后才可 offload 到 worker；产物经 `context.CreateAudioSegment` 写出并 `Commit`。**没有内部增量可做的引擎，任何信号 → 整段重处理即可。**

```csharp
class MyEffectProcessor : IEffectProcessor
{
    public MyEffectProcessor(IEffectContext context)
    {
        mContext = context;
        mContext.Input.Committed += OnDirty;          // 上游音频重提交
        mContext.Properties.Modified += OnDirty;      // 本 effect 参数变
        mContext.Committed += OnCommitted;            // 逻辑编辑收口（颗粒脏标完后一次性发）
    }

    public event Action? ProcessingRequested;

    // 本段回显曲线（key 与 GetSynthesizedParameterConfigs 对齐）：数据线程发布、宿主只读、收尾随产物一并重读。无回显返回空 map。
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mReadback;

    public Task Process(CancellationToken cancellation = default)
    {
        // —— 同步前缀（数据线程）：抓输入 PCM 引用 + 预采参数/自动化值 ——
        var input = mContext.Input;
        var src = input.Samples;                       // 已提交版本不可变整段 PCM
        int rate = input.SampleRate;
        long offset = input.SampleOffset;
        int count = src.Length;
        double amount = mContext.Properties.GetValue("amount", PropertyValue.Create(1.0)).ToDouble(out var a) ? a : 1.0;

        // 自动化（可选）：按采样时间点取值，查询轴 = 全局秒（与音频同一时间系）。
        double[]? env = null;
        if (mContext.TryGetAutomation("intensity", out var automation) && count > 0)
        {
            double segStart = rate > 0 ? (double)offset / rate : 0;
            var times = new double[count];
            for (int i = 0; i < count; i++) times[i] = segStart + (double)i / rate;
            env = automation.Evaluate(times);
        }

        // —— 此后可 offload 到 worker（只读上面物化的不可变值，永不回碰宿主活数据）——
        var dst = DoProcess(src.Span, amount, env);

        // 产出：申请输出段（可重分段、长度/采样率可与输入不同），写入并 Commit。
        var outSegment = mContext.CreateAudioSegment(offset, dst.Length, rate);
        outSegment.Write(0, dst);
        outSegment.Commit();

        // 回显：与输出同步在数据线程换引用（此例 Process 全同步，直接换即可）。
        mReadback = BuildLoudness(dst, rate, offset);
        return Task.CompletedTask;                      // 错误抛异常（宿主 catch → passthrough），此处不吞
    }

    void OnDirty() => mDirty = true;
    void OnCommitted() { if (mDirty) { mDirty = false; ProcessingRequested?.Invoke(); } }

    public void Dispose()
    {
        mContext.Input.Committed -= OnDirty;
        mContext.Properties.Modified -= OnDirty;
        mContext.Committed -= OnCommitted;
        /* 释放该段常驻状态、输出段句柄 */
    }

    readonly IEffectContext mContext;
    bool mDirty;
    IReadOnlyMap<string, SynthesizedParameter> mReadback = new Map<string, SynthesizedParameter>();
}
```

要点：

- **厚处理器、自管失效**：`CreateProcessor(context)` 为「该 effect × 该上游段」建一个长生命周期实例，持有 `context` 自订阅、跨 `Process` 复用内部缓存；段销毁 / 删 effect / 重分段 / 换采样率时宿主 `Dispose`。**不要**把状态做成每次 `Process` 重建。与本段无关的变化（如自动化改在别的段时间区间）→ 不标脏、不触发处理 → 本段输出不变、宿主据版本不变跳过下游。
- **输入是整段不可分割**：`context.Input`（`IUpstreamAudioSegment`）= 上游 voice 输出或链上前一个 effect 的输出，已提交版本 PCM 不可变（重 Commit 换新缓冲、`CommitVersion` 递增）。
- **产出经握柄**：`context.CreateAudioSegment(offset, count, rate)` 申请输出段，`Write` + `Commit`；可一段进多段出、自由重分段，采样率随段走（与工程率不同时宿主套一层重采样）。
- **回显轨（可选）**：引擎产出的只读曲线经 `GetSynthesizedParameterConfigs` 声明 + `IEffectProcessor.SynthesizedParameters` 承载本段数据（宿主把同一 effect 各段按 key 拼接呈现）。只读、不可编辑、不进数据层、不序列化；与 voice 回显同构、在参数区标题栏按源显隐。
- **条件声明**：`GetPropertyConfig` / `GetAutomationConfigs` / `GetSynthesizedParameterConfigs` 是当前参数值的纯函数（同输入同输出、无副作用、轻量），参数 commit 时重算——可据此让控件/轨随参数显隐。轨从声明消失后宿主**保留其已画曲线**（隐藏不删），参数回退即原样恢复。
- **效果链**：一条 MidiPart 上可挂多个 effect，按声明顺序**串行**——上一个输出是下一个输入；链尾各段按绝对时间混音。链顺序、bypass、增删由用户在属性面板管理。
- **失败优雅降级 / 取消**：抛异常时宿主把该段当直通（passthrough），不中断播放；取消经 `cancellation` 请求、正常返回（**不要**抛 `OperationCanceledException`），`await` 真正返回才释放调度槽位。
- **线程纪律**：`context`（`Input` / `Properties` / 自动化）仅可在 `Process` **同步前缀**（数据线程）读取；offload 后只读已物化的不可变值；`SynthesizedParameters` 与输出段须在数据线程发布。

相关接口都在 `TuneLab.SDK`：`IEffectEngine` / `IEffectProcessor` / `IEffectContext` / `IUpstreamAudioSegment` / `IAudioSegment` / `IEffectPropertyContext` / `ILiveAutomation`。

---

## 7. 打包、安装、卸载

- **包格式**：把包文件夹打成 zip，扩展名改为 **`.tlx`**，要求 `description.json` 在 zip 的**根目录**。
- **安装**：在 TuneLab 里把 `.tlx` 拖进窗口，或用扩展侧边栏的「Install Extension」。安装即解压到扩展目录并**立即加载**（无需重启）。
- **扩展目录**：`%AppData%/TuneLab/Extensions/<包名>/`（Windows）。侧边栏「Open Extensions Folder」可直接打开。
- **卸载**：扩展侧边栏每个条目的「Uninstall」。卸载在**编辑器关闭后**由独立的 `ExtensionInstaller` 完成（释放文件锁后删除），可选择「立即重启」生效。

---

## 8. 加载与校验行为

TuneLab 加载每个包时：**发现** → 读 `description.json` **判代际**（有 `id` = V1）→ **校验**（sdk-version 兼容？平台匹配？程序集存在？）→ 为包建一个 **per-folder ALC** → 按 `assemblies`（或全扫）**选择性加载** → 扫 attribute **实例化注册**。

- 任何一步失败都**优雅降级**：只跳过出问题的插件/包，**不会让主程序崩溃**，并在扩展侧边栏与日志里反映加载状态。
- `sdk-version` 高于宿主 → 该包被跳过并提示。
- `platforms` 不含当前平台 → 该插件被跳过。

---

## 附录：Legacy 插件

改版前发布的老插件（链接旧的 `TuneLab.Base` / `TuneLab.Extensions.Formats` / `TuneLab.Extensions.Voices`）属于 **Legacy**：它们的 `description.json` **没有 `id`**（或根本没有该文件）。TuneLab 据此识别为 Legacy 并交给兼容层处理。

- **新插件请勿沿用 Legacy 形态**——务必带 `id` 与新版字段，按本文编写。
- Legacy 兼容层会长期保留，老插件无强制迁移压力；但新功能（如 effect）只在 V1 提供。
