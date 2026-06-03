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

- TuneLab 的 SDK 契约程序集（`TuneLab.Primitives` + `TuneLab.SDK.*`）和 .NET 运行时由**主程序统一提供、所有插件共享**——你引用它们即可，**不要**把它们打进包里。
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
    <Reference Include="TuneLab.Primitives" />
    <Reference Include="TuneLab.SDK.Base" />
    <Reference Include="TuneLab.SDK.Format" /> <!-- format 插件 -->
    <Reference Include="TuneLab.SDK.Voice" /> <!-- voice 插件 -->
    <Reference Include="TuneLab.SDK.Effect" /> <!-- effect 插件 -->
  </ItemGroup>
</Project>
```

规则：

- **目标框架锁 `net8.0`**（SDK 的 ABI 地板）。宿主未来升级 .NET 时，按此地板编译的插件无需重编即可运行。
- **只引用 `TuneLab.Primitives` 和 `TuneLab.SDK.*`**。**不要**引用 `TuneLab.Foundation` 或主程序——它们不是插件契约。
- SDK 程序集设为「不复制到输出」（`Private=false` / Copy Local = No），打包时**别把它们放进包**，宿主会共享自己的那一份。
- 你的**私有第三方依赖**正常引用、随包分发即可。

---

## 4. 编写 Format 插件

实现 `IImportFormat`（导入）和/或 `IExportFormat`（导出），用 attribute 声明文件扩展名。需要**无参构造函数**。

```csharp
using System.IO;
using TuneLab.SDK.Format;
using TuneLab.SDK.Format.DataInfo;

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

工程模型（`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…）定义在 `TuneLab.SDK.Format.DataInfo`。

---

## 5. 编写 Voice 插件

实现 `IVoiceEngine`，用 `[VoiceEngine("type")]` 声明引擎类型标识。需要**无参构造函数**。

```csharp
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Voice;

[VoiceEngine("MyEngine")]            // 引擎类型标识（唯一）
public class MyVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

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

`Init` 的 `enginePath` 即包文件夹，捆绑的模型文件、原生运行时等放在包里、从这里定位。相关接口：`IVoiceSource` / `ISynthesisData` / `ISynthesisTask` / `SynthesisResult` 等（`TuneLab.SDK.Voice`）。

> ⚠️ **自动化参数名避开宿主保留名**：`IVoiceSource.AutomationConfigs` 的键会和宿主内置自动化合并展示，若与内置项**重名会被内置项占用、你的参数显示不出**。已知保留名：**`Volume`**、**`VibratoEnvelope`**。请用自己的独特名（如 `Breathiness` / `Growl` / 加前缀）。

---

## 6. 编写 Effect 插件

效果器（effect）对**已合成的整段音频**做变换。它面向**耗时较长的离线模型**（如 SVC 换声、神经音色转换），不是实时的 VST 式效果器。

实现 `IEffectEngine`，用 `[EffectEngine("type")]` 声明类型标识。需要**无参构造函数**。

```csharp
using TuneLab.Primitives.Audio;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Effect;

[EffectEngine("MyEffect")]            // 效果器类型标识（唯一）
public class MyEffectEngine : IEffectEngine
{
    // 参数面板：声明暴露给用户的可编辑参数（渲染为属性面板）。
    public ObjectConfig PropertyConfig => mPropertyConfig;
    // 可随时间变化的自动化参数（可空）。
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;

    // enginePath = 你的包文件夹路径，用来定位随包分发的模型/资源。
    public bool Init(string enginePath, out string? error)
    {
        error = null;
        // ... 加载模型 ...
        return true;
    }

    public void Destroy() { /* 释放资源 */ }

    public IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output)
        => new MyEffectTask(input, output);

    readonly ObjectConfig mPropertyConfig = new(new OrderedMap<string, IControllerConfig>());
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
}
```

合成任务一次性处理整段音频：`Start()` 后异步处理，把结果写入 `output.Audio`，完成时触发 `Complete`。

```csharp
class MyEffectTask(IEffectSynthesisInput input, IEffectSynthesisOutput output) : IEffectSynthesisTask
{
    public event Action? Complete;
    public event Action<double>? Progress;
    public event Action<string>? Error;

    public void Start()
    {
        try
        {
            var src = input.Audio;                       // 整段上游音频（voice 或上一个 effect 的输出）
            double amount = input.Properties.GetDouble("amount", 1.0);
            // 自动化（可选）：按采样时间点取值
            // if (input.TryGetAutomation("intensity", out var getter)) { var times = ...; var values = getter.GetValue(times); }
            var processed = Process(src.Samples, amount);
            output.Audio = new MonoAudio(src.StartTime, src.SampleRate, processed);
            Complete?.Invoke();
        }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }

    public void Stop() { /* 取消处理 */ }
}
```

要点：

- **输入/输出都是整段 `MonoAudio`**（`Primitives.Audio`）：`StartTime` / `SampleRate` / `Samples`（`float[]` 单声道）。原样整进整出，不要求逐缓冲流式处理。
- **参数读取**：`input.Properties`（`PropertyObject`）按 `PropertyConfig` 声明的键取值（`GetDouble`/`GetString`/`GetBool`/`GetEnum`…）。
- **效果链**：一条音轨的 MidiPart 上可挂多个 effect，按声明顺序**串行**——上一个的输出是下一个的输入。链顺序、启用（bypass）、增删由用户在属性面板里管理。
- **分段处理**：合成以「片段」为单位（由歌声引擎的分片决定），每个片段独立过你的链——片段之间的连续性由分片负责，你只需处理拿到的这一段。
- **失败优雅降级**：`Error` 或抛异常时，宿主把该级当作直通（passthrough），不中断整段播放。

相关接口都在 `TuneLab.SDK.Effect`：`IEffectEngine` / `IEffectSynthesisInput` / `IEffectSynthesisOutput` / `IEffectSynthesisTask`。

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
