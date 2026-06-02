# TuneLab 插件开发指南

> 适用于 TuneLab 新版扩展系统（V1）。本文介绍如何编写、打包、发布一个 TuneLab 插件。
> 老插件（Legacy）的兼容说明见文末「附录：Legacy 插件」。

---

## 1. 核心概念

- **插件包 = 一个文件夹**。它是部署、安装、卸载的原子单位，被放进 TuneLab 的扩展目录（见 §6）。
- **`description.json` 是包的身份标识**，必须放在包文件夹的**最外层**。新版（V1）插件**必须**带它；TuneLab 先读这个文件，再据其内容**有选择地**加载包内程序集——不会盲目扫描整个文件夹。
- **一个包可以含多个插件**。若你有一套共用的基建程序集，可以把基于它开发的多个插件打进同一个包，基建只分发一份、运行时也只加载一份（它们共享同一个加载上下文）。
- **插件类别（type）**：当前支持 `format`（工程文件导入/导出）与 `voice`（歌声合成引擎）。`effect`（效果器）即将到来。包还可以是纯**资源包**（无代码，如音色资源）。

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
| `author` | | 作者 |
| `description` | | 一句话简介 |
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

---

## 6. 打包、安装、卸载

- **包格式**：把包文件夹打成 zip，扩展名改为 **`.tlx`**，要求 `description.json` 在 zip 的**根目录**。
- **安装**：在 TuneLab 里把 `.tlx` 拖进窗口，或用扩展侧边栏的「Install Extension」。安装即解压到扩展目录并**立即加载**（无需重启）。
- **扩展目录**：`%AppData%/TuneLab/Extensions/<包名>/`（Windows）。侧边栏「Open Extensions Folder」可直接打开。
- **卸载**：扩展侧边栏每个条目的「Uninstall」。卸载在**编辑器关闭后**由独立的 `ExtensionInstaller` 完成（释放文件锁后删除），可选择「立即重启」生效。

---

## 7. 加载与校验行为

TuneLab 加载每个包时：**发现** → 读 `description.json` **判代际**（有 `id` = V1）→ **校验**（sdk-version 兼容？平台匹配？程序集存在？）→ 为包建一个 **per-folder ALC** → 按 `assemblies`（或全扫）**选择性加载** → 扫 attribute **实例化注册**。

- 任何一步失败都**优雅降级**：只跳过出问题的插件/包，**不会让主程序崩溃**，并在扩展侧边栏与日志里反映加载状态。
- `sdk-version` 高于宿主 → 该包被跳过并提示。
- `platforms` 不含当前平台 → 该插件被跳过。

---

## 附录：Legacy 插件

改版前发布的老插件（链接旧的 `TuneLab.Base` / `TuneLab.Extensions.Formats` / `TuneLab.Extensions.Voices`）属于 **Legacy**：它们的 `description.json` **没有 `id`**（或根本没有该文件）。TuneLab 据此识别为 Legacy 并交给兼容层处理。

- **新插件请勿沿用 Legacy 形态**——务必带 `id` 与新版字段，按本文编写。
- Legacy 兼容层会长期保留，老插件无强制迁移压力；但新功能（如 effect）只在 V1 提供。
