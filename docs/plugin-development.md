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

> ⚠️ **本节为旧模型、已过时，待重写**：现行 voice SDK 是会话托管模型（`IVoiceEngine.Init()` 无参、`CreateSession(voiceId, context) → ISynthesisSession`，由会话承担声明 + 逐步合成 + 产物，取消了 `IVoiceSource`/`ISynthesisTask`）。下方 `CreateVoiceSource`/`Init(enginePath)`/`ISynthesisTask` 等签名**已不存在**，请勿照抄。以现行接口为准（设计见 `docs/voice-sdk-design.md`），本节将在 voice 专属开发文档中重写。

实现 `IVoiceEngine`，用 `[VoiceEngine("type")]` 声明引擎类型标识。需要**无参构造函数**。

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

[VoiceEngine("MyEngine")]            // 引擎类型标识（唯一）
public class MyVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    // enginePath = 你的包文件夹路径，用来定位随包分发的模型/资源。
    public bool Init(string enginePath, out string? error)
    {
        error = null;
        // ... 扫描 enginePath 下的声库、加载模型 ...
        return true;
    }

    public IVoiceSource CreateVoiceSource(string id) => new MyVoiceSource(id);

    public void Destroy() { /* 释放资源 */ }

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}
```

`Init` 的 `enginePath` 即包文件夹，捆绑的模型文件、原生运行时等放在包里、从这里定位。相关接口：`IVoiceSource` / `ISynthesisData` / `ISynthesisTask` / `SynthesisResult` 等（`TuneLab.SDK`）。

> ⚠️ **自动化参数名避开宿主保留名**：`IVoiceSource.AutomationConfigs` 的键会和宿主内置自动化合并展示，若与内置项**重名会被内置项占用、你的参数显示不出**。已知保留名：**`Volume`**、**`VibratoEnvelope`**。请用自己的独特名（如 `Breathiness` / `Growl` / 加前缀）。

---

## 6. 编写 Effect 插件

效果器（effect）对**已合成的整段音频**做变换。它面向**耗时较长的离线模型**（如 SVC 换声、神经音色转换），不是实时的 VST 式效果器。

实现 `IEffectEngine`，用 `[EffectEngine("type")]` 声明类型标识。需要**无参构造函数**。引擎是每种效果器类型一个；宿主为工程里每条「effect 实例 × 音频段」创建一个**持久处理器** `IEffectProcessor` 驱动它。

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

[EffectEngine("MyEffect")]            // 效果器类型标识（唯一）
public class MyEffectEngine : IEffectEngine
{
    // 参数面板 / 自动化轨：均为当前参数值（context.Properties）的纯函数——宿主在参数 commit 时按当前值重算并 diff
    // 到 UI，故控件/轨可随参数显隐（条件声明）。静态的忽略 context 返回固定值即可（如下例）。
    public ObjectConfig GetPropertyConfig(IEffectPropertyContext context) => mPropertyConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context) => mAutomationConfigs;

    // 无参：包目录经 Assembly.Location 自定位（无需宿主递路径）。失败直接抛异常，宿主在调用边界 catch → passthrough 降级。
    public void Init() { /* ... 加载模型 ... */ }
    public void Destroy() { /* 释放资源 */ }

    // 每条「effect 实例 × 音频段」一个持久处理器。
    public IEffectProcessor CreateProcessor() => new MyEffectProcessor();

    readonly ObjectConfig mPropertyConfig = new()
    {
        Properties = new OrderedMap<string, IControllerConfig>
        {
            { "amount", new SliderConfig { DefaultValue = 1.0, MinValue = 0.0, MaxValue = 2.0 } },
        },
    };
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
}
```

处理器整段进整段出，把结果写入 `output.Audio`。`change` 描述自上次 `Process` 以来变了什么（首次 `IsInitial=true`），引擎据此决定内部哪些级需重算并跨调用复用中间结果；**没有内部增量可做的引擎，每次整段重处理即可**（忽略 `change` 即退化为整段进整段出）。

```csharp
class MyEffectProcessor : IEffectProcessor
{
    public Task Process(IEffectInput input, IEffectOutput output, IEffectChange change,
                        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var src = input.Audio;                          // 整段上游音频（voice 或上一个 effect 的输出）
        double amount = input.Properties.GetDouble("amount", 1.0);

        // 自动化（可选）：按采样时间点取值，查询轴 = 全局秒（与音频同一时间系）。
        double[]? env = null;
        if (input.TryGetAutomation("intensity", out var evaluator) && src.Samples is { } s)
        {
            var times = new double[s.Length];
            for (int i = 0; i < s.Length; i++) times[i] = src.StartTime + (double)i / src.SampleRate;
            env = evaluator.Evaluate(times);
        }

        var processed = DoProcess(src.Samples, amount, env);
        output.Audio = new MonoAudio(src.StartTime, src.SampleRate, processed);  // 可比输入长（含尾巴）
        progress?.Report(1);
        return Task.CompletedTask;                       // 错误抛异常（宿主 catch → passthrough），此处不吞
    }

    public void Dispose() { /* 释放该段的常驻状态 */ }
}
```

要点：

- **输入/输出都是整段 `MonoAudio`**（`TuneLab.Foundation`）：`StartTime` / `SampleRate` / `Samples`（`float[]` 单声道）。整进整出，不要求逐缓冲流式处理。
- **持久处理器**：`CreateProcessor` 为「该 effect × 该段」建一个长生命周期实例，宿主按变化重复调 `Process`、跨调用复用其内部缓存；段销毁 / 删 effect / 重分段 / 换采样率时 `Dispose`。**不要**把状态做成每次 `Process` 重建。
- **增量（可选优化）**：读 `change` 精确决定重算范围——`IsInitial`（首次全量）、`TryGetAudioChange`（上游音频变更区间）、`ChangedProperties`（变了哪些参数 key）、`ChangedAutomations` / `TryGetAutomationChange`（哪条轨、哪段秒区间变）。与本段无关的变化可原样返回上次输出（同数组引用），宿主据此跳过本段下游。
- **条件声明**：`GetPropertyConfig` / `GetAutomationConfigs` 是当前参数值的纯函数，参数 commit 时重算——可据此让控件/自动化轨随参数显隐（如某开关勾选才暴露某条轨）。轨从声明消失后宿主**保留其已画曲线**（隐藏不删），参数回退轨复现即原样恢复。须为纯函数（同输入同输出、无副作用、轻量）。
- **参数读取**：`input.Properties`（`PropertyObject`）按 config 声明的键取值（`GetDouble`/`GetString`/`GetBool`…）。
- **效果链**：一条 MidiPart 上可挂多个 effect，按声明顺序**串行**——上一个输出是下一个输入。链顺序、bypass、增删由用户在属性面板管理。
- **分段处理**：合成以「片段」为单位（由歌声引擎分片决定），每个片段独立过链；片段间连续性由分片负责，你只处理拿到的这一段。
- **失败优雅降级 / 取消**：抛异常时宿主把该级当直通（passthrough），不中断播放；取消经 `cancellation` 请求、正常返回（**不要**抛 `OperationCanceledException`）。
- **线程纪律**：`input` / `change` 仅可在 `Process` 同步前缀（数据线程）读取；若 offload 到后台线程，只读已物化的不可变值。

相关接口都在 `TuneLab.SDK`：`IEffectEngine` / `IEffectProcessor` / `IEffectInput` / `IEffectOutput` / `IEffectChange` / `IEffectPropertyContext`。

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
