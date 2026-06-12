# TuneLab 插件开发 · 面向 AI 的参考

> 本文为**喂给 AI 助手**的结构化事实清单，用于生成正确的 TuneLab V1 插件。
> 人类可读版见 [plugin-development.md](plugin-development.md)。

---

## 一句话提示语（复制给你的 AI 助手）

> 你要为 TuneLab 编写一个 V1 插件。插件是一个文件夹，根目录必须有 `description.json`（其 `id` 字段是新版判别标志，必须有，用反向域名）。代码插件类库目标框架锁 `net8.0`，**只引用** `TuneLab.Primitives` 与 `TuneLab.SDK.*`（绝不引用 `TuneLab.Foundation` 或主程序），且 SDK 程序集不随包分发（宿主共享）。format 插件用 `[ImportFormat("ext")]`/`[ExportFormat("ext")]` 实现 `IImportFormat`/`IExportFormat`；voice 插件用 `[VoiceEngine("type")]` 实现 `IVoiceEngine`；effect 插件用 `[EffectEngine("type")]` 实现 `IEffectEngine`（对整段音频做离线变换）；所有插件类需有**无参构造函数**。`description.json` 里 `type` 必填、`assemblies` 选填（写了只扫这几个程序集）、含代码时 `sdk-version` 必填。严格按下方《事实清单》的 schema 与接口签名生成，不要臆造 API。

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

插件级字段（单插件写在顶层；多插件放进 `extensions[]` 数组的每个元素）：
- `type` (string, **必填**) — `"format"` | `"voice"` | `"effect"` | 资源类（如 `"voicebank"`）。
- `assemblies` (string[], 选填) — 相对包根的程序集路径。**写了**=只加载+扫描这些；**没写**(代码插件)=扫全部 `*.dll`；资源包不写。
- `platforms` (string[], 选填) — 如 `["win","osx","linux"]` 或 `["win-x64"]`；空=全平台。

规则：
- 有 `extensions[]` → 以它为准，顶层 `type`/`assemblies` 忽略。
- 无 `extensions[]` → 顶层 `type`/`assemblies`/`platforms` 即那唯一插件。
- 无 `id` 的 manifest（或无文件）= **Legacy**，不要按此生成新插件。

### 工程配置
- `<TargetFramework>net8.0</TargetFramework>`（固定）。
- 引用：`TuneLab.Primitives`、`TuneLab.SDK`，format 插件另加 `TuneLab.SDK.Format`。
- **禁止**引用 `TuneLab.Foundation`、`TuneLab`（主程序）、或任何 `TuneLab.Extensions.*`（那是 Legacy）。
- SDK 引用 `Private=false`（不复制输出、不随包分发）。

### Format 接口（命名空间 `TuneLab.SDK.Format`）
```csharp
public interface IImportFormat { ProjectInfo Deserialize(Stream stream); }
public interface IExportFormat { Stream Serialize(ProjectInfo info); }
[AttributeUsage(AttributeTargets.Class)] public class ImportFormatAttribute : Attribute { public ImportFormatAttribute(string fileExtension); }
[AttributeUsage(AttributeTargets.Class)] public class ExportFormatAttribute : Attribute { public ExportFormatAttribute(string fileExtension); }
```
- `fileExtension` 不带点。工程模型在 `TuneLab.SDK.Format.DataInfo`（`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…）。
- 实现类需无参构造函数。

### Voice 接口（命名空间 `TuneLab.SDK`）
```csharp
public interface IVoiceEngine {
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; }   // TuneLab.Primitives.DataStructures
    bool Init(string enginePath, out string? error);                  // enginePath = 包文件夹
    void Destroy();
    IVoiceSource CreateVoiceSource(string id);
}
[AttributeUsage(AttributeTargets.Class)] public class VoiceEngineAttribute : Attribute { public VoiceEngineAttribute(string type); }
```
- `type` 唯一。实现类需无参构造函数。
- 相关类型：`IVoiceSource`、`ISynthesisData`、`ISynthesisTask`、`SynthesisResult`、`SynthesizedPhoneme`、`VoiceSourceInfo`、`AutomationConfig`、`IAutomationEvaluator`。

### Effect 接口（命名空间 `TuneLab.SDK`）
```csharp
public interface IEffectEngine {
    ObjectConfig PropertyConfig { get; }                                   // 参数面板
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    bool Init(string enginePath, out string? error);                       // enginePath = 包文件夹
    void Destroy();
    IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output);
}
public interface IEffectSynthesisInput {
    MonoAudio Audio { get; }                                               // TuneLab.Primitives.Audio，整段上游音频
    PropertyObject Properties { get; }                                     // TuneLab.Primitives.Property，参数快照
    bool TryGetAutomation(string automationId, out IAutomationEvaluator? automation);   // 查询轴 = 全局秒
}
public interface IEffectSynthesisOutput { MonoAudio Audio { get; set; } }  // 把处理结果写这里
public interface IEffectSynthesisTask {
    event Action? Complete; event Action<double>? Progress; event Action<string>? Error;
    void Start(); void Stop();
}
[AttributeUsage(AttributeTargets.Class)] public class EffectEngineAttribute : Attribute { public EffectEngineAttribute(string type); }
```
- effect = 对**整段已合成音频**的离线变换（如 SVC 换声），非实时 VST。`type` 唯一，实现类需无参构造函数。
- `MonoAudio`：`double StartTime`、`int SampleRate`、`float[] Samples`（单声道）。任务 `Start()` 异步处理后写 `output.Audio` 并触发 `Complete`；出错触发 `Error`（宿主按 passthrough 降级）。
- 一个 MidiPart 上多个 effect 按声明顺序串行，上一个输出是下一个输入；启用/顺序由用户管理。

### 常见错误（避免）
- ❌ 漏 `id` → 被当成 Legacy。
- ❌ 引用 `TuneLab.Foundation` 或主程序 → 不是插件契约，加载会出错。
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
  "assemblies": ["MyFormat.dll"]
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
    <Reference Include="TuneLab.Primitives" Private="false" />
    <Reference Include="TuneLab.SDK" Private="false" />
    <Reference Include="TuneLab.SDK.Format" Private="false" />
  </ItemGroup>
</Project>
```

`MyFormat.cs`：
```csharp
using System.IO;
using TuneLab.SDK.Format;
using TuneLab.SDK.Format.DataInfo;

[ImportFormat("myfmt")]
public class MyFormatImporter : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        // TODO: 解析 stream 填充 project
        return project;
    }
}

[ExportFormat("myfmt")]
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
