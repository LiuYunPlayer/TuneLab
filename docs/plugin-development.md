# TuneLab 插件开发指南

> 适用于 TuneLab 新版扩展系统（V1）。本文介绍如何编写、打包、发布一个 TuneLab 插件。
> 老插件（Legacy）的兼容说明见文末「附录：Legacy 插件」。

---

## 1. 核心概念

- **插件包 = 一个文件夹**。它是部署、安装、卸载的原子单位，被放进 TuneLab 的扩展目录（见 §9）。
- **`description.json` 是包的身份标识**，必须放在包文件夹的**最外层**。新版（V1）插件**必须**带它；TuneLab 先读这个文件，再据其内容**有选择地**加载包内程序集——不会盲目扫描整个文件夹。
- **一个包可以含多个插件**。若你有一套共用的基建程序集，可以把基于它开发的多个插件打进同一个包，基建只分发一份、运行时也只加载一份（它们共享同一个加载上下文）。
- **插件类别（type）**：当前支持 `format`（工程文件导入/导出）、`voice`（歌声合成引擎）、`instrument`（多声部音源引擎，如合成器/采样器）与 `effect`（效果器，对合成音频做整段离线变换，如换声）。包还可以是纯**资源包**（无代码，如音色资源）。

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

插件级（描述「这个包提供什么」）。**身份内联进 manifest**：一个条目 = 一个具体可注册能力，自带身份（引擎 id / 文件扩展名）+ 实现类全名。宿主读完 manifest 即知插件提供什么、无需加载程序集反射。

| 字段 | 必填 | 说明 |
|---|---|---|
| `type` | ✅ | 类别：`format` / `voice` / `instrument` / `effect` / `agent-model` / 资源类 |
| `engine` | voice/instrument/effect/agent-model ✅ | 引擎类型 **id**（唯一身份，如 `"MyEngine"`）。**不可变**——它会写进工程文件，改了旧工程会失配。绝不本地化。 |
| `extension` | format ✅ | 文件扩展名 **id**（不带点，如 `"myfmt"`）。同属不可变身份。 |
| `name` | | **显示名**（UI 展示用），可与身份 id 不同、可翻译。省略则 UI 退回显示身份 id。 |
| `localizations` | | 按语言翻译 `name`，如 `{ "zh-CN": { "name": "增益" } }`。缺当前语言则回退基础 `name`。 |
| `classes` | 含代码时✅ | **入口候选类清单**（全名字符串数组，如 `["My.Ns.MyVoiceEngine"]`）。宿主把数组里的类**都扫一遍**，按本 `type` 所需接口逐个匹配、命中即注册（见下）。manifest 只是"方便宿主加载的描述"，**无需精确指明哪个类干哪件事**——把候选都列上、宿主按接口认领。 |
| `assembly` | 含代码时✅ | 含上述候选类的程序集（相对包文件夹的路径，单个）。所有候选类同居此程序集。资源包省略。 |
| `platforms` | | 平台过滤，如 `["win", "osx", "linux"]` 或带架构 `["win-x64"]`。留空 = 全平台。 |

**`classes` 的接口认领规则**（宿主按 `type` 决定要找哪些接口）：

| `type` | 宿主在 `classes` 里找的接口 |
|---|---|
| `voice` | `IVoiceSynthesisEngine`（首个命中者注册为引擎） |
| `instrument` | `IInstrumentSynthesisEngine`（首个命中者注册为引擎） |
| `effect` | `IEffectEngine` |
| `agent-model` | `IAgentModelEngine` |
| `format` | `IImportFormat`（→ 注册导入）+ `IExportFormat`（→ 注册导出），各扫一遍、**至少命中其一**；同一个类可同时实现两者 |

> 所以一种类型可以需要**多个入口类**（如 format 的导入类 + 导出类），数组天然承载；只导入/只导出的格式就只放对应那一个类。每个候选类需有**无参构造函数**。
>
> **身份 id 与显示名分离**：`engine`/`extension` 是不可变身份（注册键 + 工程序列化引用）；`name`/`localizations` 仅供 UI 展示、可随意改名翻译。
>
> 一个程序集里有多个引擎/格式时，在 `extensions[]` 里逐条列出（同 `assembly`、各自 `engine`/`extension` + `classes`）。

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
  "extension": "myfmt",
  "classes": ["My.Ns.MyFormatImporter", "My.Ns.MyFormatExporter"],
  "assembly": "MyFormat.dll"
}
```

> 简写形态下顶层只有一个 `name`：它**同时**是包名与这唯一引擎/格式的显示名（`localizations` 同理共用）。若要让包名与引擎显示名各不相同，改用下面的 `extensions[]` 形态、给条目单独写 `name`。

### 2.3 一包多插件

用 `extensions[]` 数组，每个元素是一个独立插件的元数据。包级字段（id/name/version/author/description/sdk-version）写在顶层、共用：

```json
{
  "id": "com.example.suite",
  "name": "Example Suite",
  "version": "2.0.0",
  "sdk-version": "1.0",
  "extensions": [
    { "type": "format", "extension": "exfmt", "classes": ["Example.Format.Importer", "Example.Format.Exporter"], "assembly": "Example.Format.dll" },
    { "type": "voice",  "engine": "ExEngine", "classes": ["Example.Voice.ExVoiceEngine"], "assembly": "Example.Voice.dll", "platforms": ["win"] }
  ]
}
```

> `Example.Format.dll`、`Example.Voice.dll` 可以共同引用同一个 `Example.Common.dll`（放进包里），它只需分发一份。
>
> 规则：有 `extensions[]` 时以它为准，顶层的身份字段（`type`/`engine`/`classes`/…）被忽略。

### 2.4 资源包（无代码）

省略 `assembly`/`classes` 等代码字段，只用 `type` 声明用途。TuneLab 只登记它、不加载代码，由对应引擎在运行时去发现包内资源：

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

实现 `IImportFormat`（导入）和/或 `IExportFormat`（导出）。需要**无参构造函数**。文件扩展名与实现类写在 `description.json`（`extension` + `classes` + `assembly`），代码里**不再用 attribute 声明**。导入类与导出类可以是两个类（都列进 `classes`），也可以是同一个类同时实现两个接口。

```csharp
using System.IO;
using TuneLab.SDK;

public class MyFormatImporter : IImportFormat   // 列进 classes，宿主按 IImportFormat 认领为导入
{
    public ProjectInfo Deserialize(Stream stream)
    {
        // 把 stream 解析成 TuneLab 工程模型
        var project = new ProjectInfo();
        // ... 填充 project ...
        return project;
    }
}

public class MyFormatExporter : IExportFormat   // 列进 classes，宿主按 IExportFormat 认领为导出
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

对应的 manifest 条目：

```json
{ "type": "format", "extension": "myfmt", "name": "My Format",
  "classes": ["My.Ns.MyFormatImporter", "My.Ns.MyFormatExporter"],
  "assembly": "MyFormat.dll" }
```

> `extension` 是不可变身份（路由 + 序列化）；`name` 是可选显示名（可加 `localizations` 翻译）。

工程模型（`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…）定义在 `TuneLab.SDK`。

---

## 5. 编写 Voice 插件

voice 是**歌声合成引擎**（如 SVS 模型）。本章按「先建立心智模型 → 逐个接口讲清职责与坑 → 五个易错专题（音素 I/O、音高采样、快照、属性约定、原生依赖打包）」组织。照本章写完，你应能交付一个**线程安全、增量重合成正确、产物归属无误**的 voice 插件。

### 5.0 心智模型（先读这一节）

- **会话托管厚模型**：你实现 `IVoiceSynthesisEngine`（每种引擎类型一个，需无参构造函数；引擎 id 写在 `description.json` 的 `engine`，实现类列进 `classes`，宿主按 `IVoiceSynthesisEngine` 接口认领）。宿主为工程里**每条 MidiPart** 调一次 `CreateSession` 建一个 `IVoiceSynthesisSession`。**合成的全部状态由会话自己托管**——分块、调度状态、音频缓冲、合成进度、失效（dirty）判定全在你这边。理由：失效依赖图（如「音素时长 → 音高 → 音频」的分级管线，改自动化只需重渲音频而不必重算音素）只有引擎自己懂，宿主无从复制。宿主只做三件事：把工程数据的变更流推给你、驱动调度、读你的产物来展示。
- **声明 vs 执行分层**：会话对外有两类职责——*声明*（这个声源暴露哪些自动化轨/回显轨/属性面板、默认歌词）与*执行*（合成）。声明全部是「当前 part/note 参数值的纯函数」，宿主在参数 commit 时重算并 diff 到 UI（详见 §5.2）。
- **插件侧时间量一律全局秒**：note 边界、曲线查询点、开窗区间、状态段范围、音频段对齐——**全部是秒**（`double`）。tick 只是宿主乐谱内部表示、**绝不外露**给插件。全局 0 秒 = 采样点 0。tempo 变化不需要你显式处理：它被分解成「note 边界秒值变」与「自动化区间移位」两类具体通知，你用既有订阅就收到了（§5.9）。
- **两视图 + 线程纪律（最重要的坑）**：
  - **活视图**（`IVoiceSynthesisContext` 及其 `IVoiceSynthesisNote` / `ISynthesisAutomation`）：可订阅、**只能在数据线程访问**。用于「收变更通知 → 标脏」「`GetNextSegment` 分片决策」「`SynthesizeNext` 同步前缀拉快照」。
  - **冻结快照**（`VoiceSynthesisSnapshot` 及 `*Snapshot` 家族、`IAutomationEvaluator`）：不可变、无事件、**可跨线程**。后台 worker **只读快照**，永不回碰任何活视图对象。
  - 命名即纪律：活视图（`IVoiceSynthesisContext` / `IVoiceSynthesisNote` / `ISynthesisAutomation`）仅数据线程；`*Snapshot` = 冻结物（可跨线程）。**违反这条是 voice 插件最常见、最难查的 bug**（worker 线程读活 note → 与编辑线程数据竞争）。开发期宿主会在活视图入口做数据线程断言，跨线程访问会直接抛异常帮你定位。

manifest 条目：`{ "type": "voice", "engine": "MyEngine", "name": "My Engine", "classes": ["My.Ns.MyVoiceEngine"], "assembly": "MyVoice.dll" }`（`engine` 是不可变身份、会写进工程文件，改了旧工程会失配；`name` 可选显示名、可加 `localizations` 翻译；宿主在 `classes` 里找实现 `IVoiceSynthesisEngine` 的类）。

### 5.1 `IVoiceSynthesisEngine`：引擎生命周期与声库目录

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

public class MyVoiceEngine : IVoiceSynthesisEngine    // engine id 在 manifest 的 "engine" 声明
{
    // 声库目录（菜单/选择器用，无需创建会话即可读）。
    // 契约：必须【立即返回、不得阻塞】——宿主与 UI 同步读取、无异步等待。
    // 正确做法：Init 期扫描声库并缓存，这里仅返回缓存引用。惰性加载（首次 get 才扫盘）会卡 UI。
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    // 无参、失败抛异常：宿主在调用边界 catch（责任归属靠捕获点判定，不靠异常类型）。
    // 不传安装路径——你的 DLL 经 typeof(MyVoiceEngine).Assembly.Location 即可自定位包目录（见 §5.10）。
    // Init 是懒调用（首次用到才调），宿主也可主动预热。仅「跨调用持有昂贵常驻状态」（如加载模型）才需要 Init/Destroy。
    public void Init() { /* 扫描声库填 mVoiceInfos；加载/预热模型 */ }
    public void Destroy() { /* 释放常驻资源（卸载模型、关 ONNX session 等） */ }

    // 每条 part 一个会话：voiceId 是 VoiceSourceInfos 的 key（选定哪个声库）；
    // context 是该 part 的输入活视图，随会话同生共死。会话是轻量句柄——重模型加载应是懒的。
    public IVoiceSynthesisSession CreateSession(string voiceId, IVoiceSynthesisContext context)
        => new MySession(voiceId, context);

    // —— 声明面（属性面板 / 自动化轨）：见 §5.2，全在引擎上、不依赖会话实例 ——
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mReadbackConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => mPartConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => mNoteConfig;

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}
```

**`VoiceSourceInfo` 字段**（声库目录元数据，会话不重复承载这些）：

```csharp
public struct VoiceSourceInfo
{
    public string Name;             // 声库显示名（可本地化，见 §5.2 末「本地化」）
    public string Description;      // 一句话简介
    public ImageResource? Portrait; // 可选立绘（显示在钢琴窗）；null = 无
}
```

`Portrait` 用 `FileImageResource`（`TuneLab.Foundation`），传**绝对路径**——你按自己的包目录拼出（见 §5.10）。它可指向单张图，也可指向序列帧目录，宿主按需解码：

```csharp
var portrait = new FileImageResource(System.IO.Path.Combine(packageDir, "voices", voiceId, "portrait.png"));
mVoiceInfos.Add(voiceId, new VoiceSourceInfo { Name = "Alice", Description = "...", Portrait = portrait });
```

> 换声源（换引擎）时宿主丢弃旧会话、用新 `voiceId` 重建会话（context 也随之重建）；引擎对象本身长存，`Init` 只在首次用到时调一次。

### 5.2 引擎声明面：属性面板与自动化轨

声明面的四个方法**在 `IVoiceSynthesisEngine` 上**（不是会话上），**全部是纯函数**（同输入同输出、无副作用、轻量），宿主在每次参数 commit 时调用并 diff 到 UI。静态声明的插件忽略 `context` 返回固定 map/config 即可；要做条件 UI（某开关打开才出现的控件/轨）就读 `context` 当前值来决定返回什么；多声库（一个引擎多个 `voiceId`）按 **`context.VoiceId`** 分流。

> **为何在引擎而非会话**：声明只依赖 `(voiceId, part 当前值)`、不碰任何合成运行时状态，本就是纯函数。放在引擎上让宿主能在**建会话之前**就求出声明（轨集合/面板），于是 `CreateSession` 返回的会话在**构造函数里就能订阅自己声明的自动化轨**（构造期 `context.Automations` 已含你声明的轨，`TryGetValue` 取得 / 可枚举——声明已就绪）。若声明留在会话上则成死环：会话要订阅自己声明的轨，却要等自己构造完、宿主才拿得到声明——构造期 `context.Automations` 尚未填好你声明的轨。`DefaultLyric` 是唯一留在会话上的取值（运行时才用，见 §5.3）。

```csharp
// 自动化轨集合（part 级）：连续轨与分段轨同在一张有序 map，声明序即呈现序。voiceId 选哪个声库经 context.VoiceId。
public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
// 只读回显轨声明（引擎产出、不可编辑的曲线，如 energy）。无回显返回空 map。
public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mReadbackConfigs;
// part 级属性面板（只依赖 part 自身稀疏值 + context.VoiceId）。
public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => mPartConfig;
// note 级属性面板（依赖 part 设置 + 选中 note 的合并值）。
public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => mNoteConfig;
```

> `IVoiceSynthesisPartPropertyContext`（part 面板/自动化）：`VoiceId` + **`IReadOnlyList<PropertyObject> PartProperties`**（各选中 part 的稀疏快照，可多选 part）。`IVoiceSynthesisNotePropertyContext`（note 面板，**独立接口、不继承**）：`VoiceId` + **`PropertyObject PartProperties`**（note 所属的**单个** part——note 必属一个 part）+ **`IReadOnlyList<PropertyObject> NoteProperties`**（各选中 note）。列表成员不在乎多选就 `.Merge()`（`PropertyObjectExtensions` 扩展方法，在 `TuneLab.Foundation`）还原成单个三态 `PropertyObject`（同 key 全等给值、不等/部分缺给 `Multiple`）按单选写；要逐成员真值（如把不等长数组的 seed 合成对）就直接遍历列表。voiceId 进 context 使 voice 的 context 与 effect 的 `IEffectPropertyContext`（无此对等物）永久分叉——这是有意的：effect 是单类型引擎，没有「选哪个库」的概念。

**note / part 属性约定（keyed `Properties`，这是 per-note/per-part 参数的唯一通道）**：

- `IVoiceSynthesisNote` 的固定字段只有最小通用乐理量（`StartTime`/`EndTime`/`Pitch`/`Lyric`/`Phonemes`）。**所有 voice 专属的 per-note 参数（如张力、气声、性别）都走 `note.Properties`（keyed）**——加新参数 = 在 `GetNotePropertyConfig` 的 `ObjectConfig.Properties` 里加一个 key，不动接口固定面。part 级专属参数同理走 `GetPartPropertyConfig`。
- 面板用控件配置词汇搭（都在 `TuneLab.SDK`）：`SliderConfig`（`DefaultValue`/`MinValue`/`MaxValue`/`IsInteger`，量程与默认值必填）、`ComboBoxConfig`（`Options` + `DefaultOption`，值/显示分离，可「界面中文/底层存枚举值」）、`CheckBoxConfig`、`TextBoxConfig`（`IsPassword` 可掩码）；容器是 `ObjectConfig { Properties = OrderedMap<string, IControllerConfig> }`。

```csharp
readonly ObjectConfig mNoteConfig = new()
{
    Properties = new OrderedMap<string, IControllerConfig>
    {
        { "tension",   new SliderConfig { DisplayText = "Tension", DefaultValue = 0, MinValue = -1, MaxValue = 1 } },
        { "breathiness", new SliderConfig { DisplayText = "Breathiness", DefaultValue = 0, MinValue = 0, MaxValue = 1 } },
    },
};
```

- **读值**：合成时从 `VoiceSynthesisNoteSnapshot.Properties`（`PropertyObject` 值拷）读，用 `GetDouble(key, default)` / `GetBool` / `GetInt` / `GetString` / `GetEnum<T>`。**稀疏存储**——只有用户改过的字段才在里面；读不到就用你声明的默认值（`PropertyObject` 的 `Get*` 第二参数就是 fallback，传与声明一致的默认值即可）。
- **`AutomationConfig`**：`DisplayText` / `DefaultValue` / `MinValue` / `MaxValue` / `Color`（如 `"#E5A573"`）。**`DefaultValue = double.NaN` ⇒ 分段轨**（无默认基线、段间断开，如 pitch 类、bend）；实数 ⇒ 连续轨（处处有值、有基线，如 growl）。回显轨恒为分段形（`DefaultValue = NaN`）。
- **条件声明 + 孤儿数据**：轨集合可随参数显隐（如某开关勾选才暴露 Growl 轨）。轨从声明里消失后，宿主**保留其已画曲线（隐藏不删、不参与合成）**，参数回退使该轨复现即原样恢复——你不必担心条件轨切换会丢用户数据。

> ⚠️ **自动化参数名避开宿主保留名**：`GetAutomationConfigs` 的键会和宿主内置自动化合并展示，与内置项**重名会被内置项占用、你的参数显示不出**。已知保留名：**`Volume`**、**`VibratoEnvelope`**。请用自己的独特名（如 `Breathiness` / `Growl` / 加前缀）。

> **本地化**：`DisplayText`、`ComboBox` 选项文本、轨名、声库名/简介都由你自译——读 `TuneLabContext.Global.Language`（如 `"zh-CN"`），用你自己的词典出文案，宿主不参与查表。未译时原样返回英文即可。manifest 的 `name`/`description` 走 `localizations` 字段本地化。

### 5.3 输入活视图 `IVoiceSynthesisContext` 与 `IVoiceSynthesisNote`

> 会话本身（`IVoiceSynthesisSession`）的声明只剩 **`DefaultLyric`**（`string`，新建 note 的默认歌词）——它是创建后才被取用的运行时值，不参与构造前声明，故留在会话实例上；其余声明（轨/面板）都在引擎上（§5.2）。

context 由宿主实现、会话级（随会话死）、**仅数据线程访问**。你订阅它来感知输入变化，并在 `SynthesizeNext` 同步前缀从它 `GetSnapshot` 物化快照。

```csharp
public interface IVoiceSynthesisContext
{
    IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> Notes { get; }   // 链表：枚举顺序消费、First/Last、note.Next/Last 邻居导航；WhenAny 自动接线成员增删
    IReadOnlyNotifiablePropertyObject PartProperties { get; }
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }   // 取你声明过的可编辑轨（TryGetValue / 枚举）
    ISynthesisAutomation Pitch { get; }            // 绝对音高约束（分段：有值=钉死、NaN=自由），见 §5.6
    ISynthesisAutomation PitchDeviation { get; }   // 加性偏差（连续、默认 0、永不 NaN），见 §5.6
    VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes, double startTime, double endTime);  // 见 §5.5
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);             // 见 §5.8
    event Action? Committed;                   // 逻辑编辑收口，见 §5.9
}
```

**`Notes` 排序与重叠**：全序确定性——`StartTime` 升序 → 同起点 `EndTime` 降序（长 note 在前）→ 再同则保持插入序。note **可以重叠**（和弦）：序列原味直传可重叠 note，「后盖前」等去重叠是**你的责任**（单声部插件按需截断，和弦插件原味消费重叠）。

**`IVoiceSynthesisNote` 字段**全是可订阅属性（`IReadOnlyNotifiableProperty<T>`，有 `Value` / `WillModify` / `Modified`）：`StartTime`/`EndTime`（全局秒）、`Pitch`（`int` 半音）、`Lyric`（`string`）、`Phonemes`（`IReadOnlyList<VoicePhoneme>`，见 §5.7）、`Properties`（keyed per-note 参数）。还有 `Next`/`Last` 邻居链——**仅供数据线程的分片决策用**（事件 handler 里只有 note 自身引用、无列表索引）；合成时必须在快照的有序列表上按索引导航邻居，不要回碰活 note。

### 5.4 调度：`GetNextSegment`（peek）与 `SynthesizeNext`（commit）

宿主掌握全局播放线，**驱动逐步合成**：先 peek 窗内「下一块待合成」的边界，再 commit 合成那一块。

```csharp
// peek：窗内下一待合成块的纯值秒边界，【无副作用】。null = 窗内无待合成。
// 数据线程上廉价执行（会被多会话 speculative 地问，多数不中选——别在这里做重活或捕获）。
public SynthesisRange? GetNextSegment(double startTime, double endTime)
{
    // 基于完整 part 做【确定性】分片决策，返回下一脏块 [start,end]。
    // 确定性是关键：commit 时会按同一窗口重算分块，须与本次 peek 得到同一块。
    return FindNextDirtyPiece(startTime, endTime) is { } p ? new SynthesisRange(p.StartTime, p.EndTime) : null;
}

// commit 入参 = 选中它的那次 peek 的【同一窗口】（不是回灌 GetNextSegment 自报的 SynthesisRange）。
public async Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default)
{
    // —— 同步前缀（仍在数据线程）：按同一窗口重算分块 + 圈定本块 note + GetSnapshot 物化快照 ——
    if (FindNextDirtyPiece(startTime, endTime) is not { } piece) return;
    var snapshot = mContext.GetSnapshot(piece.Notes, piece.Notes[0].StartTime.Value, piece.Notes[^1].EndTime.Value);
    piece.Dirty = false;            // 合成期间到达的新变更会重新标脏，完成后自然重排
    StatusChanged?.Invoke();        // 标记本段进入 Synthesizing

    // —— offload：worker 只读 snapshot 算 PCM/音素/曲线（绝不碰活视图）——
    var report = new Progress<double>(p => { piece.Progress = p; StatusChanged?.Invoke(); });
    var rendered = await Task.Run(() => Render(snapshot, piece.Notes, report, cancellation), CancellationToken.None);
    if (rendered == null) return;   // 取消：正常返回，产物保持上一版

    // —— marshal 回数据线程发布产物（换引用即不可变）——
    piece.Segment?.Dispose();       // 丢旧段建新段（见 §5.8）
    piece.Segment = mContext.CreateAudioSegment((long)(rendered.StartTime * rate), rendered.Audio.Length, rate);
    piece.Segment.Write(0, rendered.Audio);
    piece.Segment.Commit();
    piece.Phonemes = rendered.Phonemes;
    StatusChanged?.Invoke();
}
```

调度坑点：

- **peek→commit 原子衔接**：两者在同一调度 tick、同在数据线程，期间无编辑可插入；commit 收到的是选中它的那次 peek 的**同一窗口**。所以你的分片必须**确定性**（数据未变 + 同一窗口 ⇒ commit 重算得到 peek 报出的同一块）。peek 时若要为 commit 留信息（分块缓存），存进会话自己的字段，**不要**塞进 `SynthesisRange`（它只是 peek 返回的两个 `double`）。
- **一个会话同时只合成一块**；并行发生在不同 part 的不同会话之间，并发上限由宿主账本式管控。
- **取消是正常调度结局**：`SynthesizeNext` 返回纯 `Task`、无 outcome。取消时**正常返回，绝不抛 `OperationCanceledException`**（否则逼每个 await 套 try-catch）。错误才抛异常（宿主 catch、该段标 `Failed`）。**槽位在 `await` 真正返回时才释放**——不可中止的实现把这块跑完再返回即可，资源始终封顶在并发上限内。
- **进度**经状态带上报：`SynthesisStatusSegment.Progress`（[0,1]）+ `StatusChanged`，不经 `SynthesizeNext` 参数传。`Progress<T>` 在数据线程构造能捕获同步上下文，worker 的进度回报会 marshal 回数据线程。
- **offload 用 `Task.Run`**：`Render` 里只读 `snapshot`，传 `cancellation` 进去让其能尽早退出。注意 `Task.Run` 的第二参数传 `CancellationToken.None`（取消由 `Render` 内部检查 `cancellation.IsCancellationRequested` 处理，而非让调度抛 TaskCanceledException）。

### 5.5 合成快照 `VoiceSynthesisSnapshot`（隔离的核心）

worker 不能碰活视图，所以 `SynthesizeNext` 的同步前缀要把本次合成所需的一切**物化成不可变快照**再 offload。`GetSnapshot` 一次返回一份：

```csharp
VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes, double startTime, double endTime);
```

- **`notes`**：本次合成需要的 note——**段内 note + 协同发音邻居**，由你自由圈定（如想看前一个 note 的尾辅音，就把它也放进来）。返回的 `snapshot.Notes` 与你递入的 `notes` **索引对齐**——这是产物归属契约（见 §5.7：`SynthesizedPhonemes` map 以 `origins[i]` 为键回指）。
- **`[startTime, endTime]`**：自动化曲线的开窗区间（秒）。
- **一次合成可拉多份**：如先拉音素级小窗定时，再据音素结果拉音频级大窗。但**只能在同步前缀（offload 前、数据线程）调用**。

`VoiceSynthesisSnapshot` 带什么（全是不可变值，可跨线程；将来跨进程时它就是序列化消息体）：

```csharp
public sealed class VoiceSynthesisSnapshot
{
    IReadOnlyList<VoiceSynthesisNoteSnapshot> Notes { get; }   // 与递入 notes 索引对齐；邻居按索引导航（不带 Next/Last）
    SynthesisAutomationSnapshot Pitch { get; }            // 绝对音高约束（冻结求值器）
    SynthesisAutomationSnapshot PitchDeviation { get; }   // 加性偏差（冻结求值器）
    PropertyObject PartProperties { get; }                // part 参数值拷
    IReadOnlyMap<string, SynthesisAutomationSnapshot> Automations { get; }   // 取你声明的可编辑轨（同活视图函数式入口）
}

public sealed class VoiceSynthesisNoteSnapshot   // 触底到值类型、无任何活引用
{
    double StartTime { get; }  double EndTime { get; }    // 全局秒。EndTime = 有效末（宿主去重叠后盖前钳到下一 note 起点，单声部音频口径）；宿主独占音素布局，不暴露满末
    int Pitch { get; }         string Lyric { get; }
    IReadOnlyList<VoicePhoneme> Phonemes { get; }     // 物化副本（时长 / 权重 / IsLead，位置不存）
    PropertyObject Properties { get; }                    // per-note 参数值拷
}

public sealed class SynthesisAutomationSnapshot { IAutomationEvaluator Evaluator { get; } }
```

- **automation 是冻结求值器，不是裸点**：`SynthesisAutomationSnapshot.Evaluator.Evaluate(times)` 递一列秒时间点、拿回各点的值（`double[]`）。插值算法恒在宿主侧（杜绝两套插值漂移），你只管递点取值。这正解决了「查询点常是合成中间产物（音素定时后才知道在哪采）、快照时刻预知不了」的问题。
- **想在前缀就采好的**：在同步前缀直接调 `Evaluator.Evaluate(...)` 把值采成 `double[]` 自存，再 offload——这样后台完全不依赖求值器（推荐给查询点已知的场景）。
- **唯一纪律**：快照不可变、只写一次、此后只读。**宿主从不修改已发布的快照**——数据变了走活视图通知 → 你标脏 → 下次 peek 出新段 → 物化**一份全新快照**。替换而非同步，所以无需任何锁。

### 5.6 音高曲线采样：双通道 + 按帧取值

音高是**两个平行通道**，合成时按公式合成：

```
finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)
```

- **`Pitch`（绝对约束，分段型）**：用户钉死的绝对音高曲线（半音）。**有值 = 用户钉死、必须遵守**；**`NaN` = 自由区，你自己生成**（典型回退到 note 的 `Pitch`，并自加滑音/过渡）。
- **`PitchDeviation`（加性偏差，连续型）**：处处有值、默认 0、**永不 NaN**。宿主侧的 vibrato 等偏差源都汇于此。它**加在解析后的绝对面上**，所以偏差对「自由区」同样生效（旧式把 vibrato 叠在绘制曲线上，自由区无载体会丢偏差——这里结构性修复了）。

按帧（控制率）采样的参照写法（worker 内，只读快照）：

```csharp
// 以控制率（如 100Hz）在 note 时间范围内布点，批量求值，再逐采样线性插值。
int controlCount = Math.Max(2, (int)((noteEnd - noteStart) * kControlRate) + 1);
var times = new double[controlCount];
for (int c = 0; c < controlCount; c++)
    times[c] = noteStart + (noteEnd - noteStart) * c / (controlCount - 1);

double[] pitch     = snapshot.Pitch.Evaluator.Evaluate(times);          // 绝对约束（含 NaN）
double[] deviation = snapshot.PitchDeviation.Evaluator.Evaluate(times); // 加性偏差（无 NaN）
for (int c = 0; c < controlCount; c++)
    pitch[c] = (double.IsNaN(pitch[c]) ? note.Pitch : pitch[c]) + deviation[c];  // NaN 自由区回退 note 音高
// 之后 pitch[] 即「最终半音曲线」，频率 = 440 * 2^((pitch-69)/12)，逐采样线性插值。
```

- **`times` 是全局秒**，与音频/音素同一时间系。批量 `Evaluate` 比逐点调用高效得多——攒一批点一次调。
- **`Evaluate` 永不要你懂插值**：连续轨永不返回 NaN；分段轨段间返回 NaN（自己据此判断「自由/钉死」）。
- 你**产出的音高回显**走 `SynthesizedPitch`（具名富类型 `SynthesizedPitch { IReadOnlyList<IReadOnlyList<Point>> Segments }`，分段折线，`Point = (全局秒, 半音)`），供宿主在音高轨上画回显线。空 = `new() { Segments = [] }`。其他声学量（如 energy）走回显轨（§5.2 + §5.8）。

### 5.7 音素 I/O：`VoicePhoneme`（读入 / 输出同型）

音素描述符**方向无关**——读入（用户钉死约束）与输出（合成产物）都用同一个 `VoicePhoneme`：只报「标称时长 + 权重 + IsLead」，**不报绝对位置**。定位 / 跨 note 去重叠压缩 / melisma 铺设全由**宿主**按同一时长模型派生（引擎报已压缩的绝对位置会让宿主布局误判相接判据，故只报自然时长、宿主独占布局）。

```csharp
public struct VoicePhoneme { public string Symbol; public double Duration; public double StretchWeight; public bool IsLead; }
```

**输入（host → engine）：`note.Phonemes`（`IReadOnlyList<VoicePhoneme>`，per note）**

- 用「时长 + 权重 + IsLead」表示，而非解析后的绝对时间——相邻钉死 note 的去重叠（后盖前 / **跨 note 辅音簇压缩**）须由**全局布局算法**联合求解；且用时长便于「推挤式」编辑（改一个音素长度，相邻整体平移而非互相挤占）。
  - `Duration`：辅音(`StretchWeight=0`)的固定时长；核(`StretchWeight>0`)的时长是**填充派生量**（布局忽略其记录值，见下）。
  - `StretchWeight`：弹性伸缩权重，`>0` = 核可伸（全局压缩中**先让**）/ `0` = 辅音刚性（**按标称长度等比压**）。
  - `IsLead`：是否前置音素（音节核之前的引导辅音）。**显式标注前后归属**（不用 `StretchWeight` 推断），与弹性解耦。
  - **位置不存、由布局派生**：前置分界线（核起点）= 音符头；`IsLead` 音素从分界线往左累积固定时长（可任意加长、向 note 前越界）；核 + 后辅音往右——辅音用固定时长、核填充到 **note 满末**（含 melisma 铺过乘客）、多核按权重分摊。
- **钉死粒度为整 note**：列表**非空** = 用户钉死了**全部**音素（你必须遵守这组约束）；列表**为空** = 你从 `Lyric` 做 G2P + 全自由定时。**不支持单音素级部分钉死**。

**解析为真实时序：调 SDK 共享函数 `VoicePhonemeLayout.Resolve`**

`Resolve` 只接管「定位」这一半——把你给的「音素**标称时长** + note 几何」跨 note **去重叠压缩**成真实 `[StartTime, EndTime]`。**标称时长怎么来（G2P、按元音分词分组、`word_div`/`dur` 模型、head/tail padding）仍全在你这边**（引擎专属，不被消掉）；交出去的只是末端对齐 / 去重叠那一层，不是整条音素链。

**两种用途，别混为一谈**：

- **音频布局（用 `Resolve` 驱动帧时序——你基本必须用）**：若你按每音素时长顺序铺帧喂声学模型，就用 `Resolve` 输出的 `[Start,End]` 定帧长——重叠被压掉、帧总长不再溢出真实窗口。**此时 `FillEnd` 的取法直接塑造音频**：要音频 == 宿主显示（WYSIWYG），`FillEnd` 必须取**与宿主同一口径**——自己的有效末 + 仅铺过**延续乘客**（`IsContinuation`）的 melisma；**真发声 note 间的空隙就停在自己末（空隙是静音）、别把元音铺过空隙到下一发声 note**。一旦 `FillEnd` 偏离此口径（如填过空隙），音频与显示就分叉，而这是**听得见的**，不是"非致命"。
- **显示对齐（可选）**：若你**不**用 `Resolve` 驱动音频、只想让宿主画的音素线对上你的自由音频，调它即可一致；不调就自由放置——这种**纯显示**错位才是「顶多音素线与波形错位、非致命」。这句 escape hatch **只对显示成立、对音频不成立**。

调用：把每个 note 物化成 `VoicePhonemeLayoutNote`（`FillStart` = 音符头；`FillEnd` 见上；`Phonemes` = 该 note 音素，顺序前置辅音→核→后辅音），整段传 `Resolve`，返回同构交错数组 `VoicePhonemeTiming[][]`（`{ Start, End, Duration }`，可 `var (s,e)=` 解构）——`result[i][j]` = `notes[i].Phonemes[j]` 的真实落点。`Resolve` 对任意连续 note 区间成立，宿主显示传窗口、你传整段同一函数。冻结的只是 I/O 形状，压缩体宿主侧可演进、你运行时绑定那一份故不漂移。

**钉死覆盖**：`snapshot.Notes[i].Phonemes` 非空 = 该 note 用户钉死，物化 `VoicePhonemeLayoutNote.Phonemes` 时用其钉死音素而非你的 G2P 预测；空才用预测。

- **全局压缩语义（两阶分级，逐 note 边界相互独立）**：每个 note 边界的吸收跨度 = `[前 note 末元音起 … 后 note 首元音起]`——① 元音（w>0）**先让**，最多到 0；② 元音耗尽仍超 → 辅音簇（w=0，前 note 尾辅音 ∪ 后 note 前辅音）**按标称长度等比压**（当前无最小地板、可压到 0）。例 `kas`+`bus` 靠近：`a` 先让，再 `s`+`b` 一起压。
- **间隙（静音）= 音素互不影响**：仅**相接 / 重叠**的相邻 note 才跨 note 协同（上面的两阶推挤）。两 note **有空隙**（前 note 内容末 < 后 note 核起点）时，两 note 音素**各自保持自然几何、互不推挤压缩**——后 note 前置辅音可自然探入空隙、显示上与前 note 重叠，但谁都不动。这样固定音素不因邻居在空隙内移动而跳变；用户**想要可调（前置辅音推挤前 note 元音）就把音符拉到相接**。**合成侧间隙如何处理由你（引擎）自行决定**：合并连续推理（→推挤）还是分片独立产出在时间轴重叠混音（→重叠发声），是你的音频决策；宿主只按理想形态（相接推挤、空隙独立）显示。若你用 `Resolve` 驱动音频，想 WYSIWYG 就让 `FillEnd` 同口径（空隙停在自己末）；偏离则音频与显示在空隙处分叉，**驱动音频时这听得见、非"非致命"**（"非致命"只对纯显示错位成立，见上「两种用途」）。
- **延续与休止**：宿主把**你不为其产出音素的 note** 视为透明——前一个 note 的音素正向铺过它（melisma）。要让某个 note 发**静音 / 换气**，就为它输出一个静止音素（如 `sp` / `AP`），它即不再透明、构成边界。**哪些 note 是延续（连音 / melisma 乘客）由宿主作稳定标志暴露**：读 `IVoiceSynthesisNote.IsContinuation` / `VoiceSynthesisNoteSnapshot.IsContinuation`，**不要**自行匹配歌词记号、也**不必再自判相接**——该标志是「**生效延续**」：已含「延音符 ∧ 经不断裂的相接链回溯到发声 note」，**孤儿延音符（被空隙断链）为 `false`**，故直接信它就和宿主一致、不会把前元音误铺进静音。判据规则宿主独占、可演进。注：`IVoiceSynthesisNote.IsContinuation` 是**普通只读字段、无独立通知**（它是 Lyric+相接的派生量，要响应变化请订阅 `Lyric`/`StartTime`/`EndTime`）。

**输出（engine → host，合成时返回）：按归属 note 键的音素 map**

```csharp
IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<VoicePhoneme>> SynthesizedPhonemes { get; }
// 每个归属 note → 该 note 的一组 VoicePhoneme（只报 Symbol / Duration / StretchWeight / IsLead，无绝对位置）
```

- **按归属 note 键**（而非扁平时间线 + 出身字段）：音素描述符不报绝对位置，无主音素无锚不可定位、也落不进 note 失效链，故**没有「无主音素」契约**——`VoicePhoneme` 不带 `Note` 字段，归属全由 map 的键表达。辅音入侵上一 note 尾巴这类越界，由宿主按时长模型派生位置时自然产生，不需你声明位置。（breath 等将来用「归属 note 的前置 / 后置音素」或专属事件通道承载。）
- **map 键怎么填**：用你递给 `GetSnapshot` 的**活 note 列表**（`origins`）按**快照索引对齐**回取——`snapshot.Notes[i]` 的产物归属就是 `origins[i]`，把该 note 这组音素以 `origins[i]` 为键加入 map。键仅作身份 token（归属）用，**合成中不得读它的属性**（那是活视图、在 worker 线程是违例）。脏 / 合成中的块**不应**在 map 里报告其 note 的音素（宿主据此留白）。
- **`StretchWeight`（伸缩 / 压缩权重）**：用户锁定音素后，宿主把产物固定为钉死几何（`note.Phonemes` 的 `StretchWeight` 即由此而来），此后显示 / 合成的去重叠按**全局两阶**用它分级：**元音（w>0）先让位**（最多到 0），**辅音（w=0）刚性、仅在元音耗尽后按标称长度等比压**。典型一个音节 `[前辅音 w0, 元音 w1, 后辅音 w0]`。**没有音韵学知识就填 `w = Duration`**（让所有音素都可伸、退化为按时长比例缩，安全默认）；`Σw ≤ 0`（含没设、struct 默认全零）退化为均匀，无除零。
- **核时长由宿主填充派生**：核（`StretchWeight>0`）的 `Duration` 被布局忽略（恒按 note 可用空间填充），报多少无所谓；辅音（w=0）的 `Duration` 即其固定长。`IsLead` 决定前后归属（前置往左累积、核 + 后辅音往右）。因此输出**无需自己摆位**，只诚实报每个音素的时长 + 权重 + 前后标记即可，锁定时宿主按「核起点 = 音符头」统一派生位置——零跳变。
- **preview 纯显示、绝不反馈给你当约束**：权威时长由全量合成重新定时返回（带新权重），覆盖 preview。你只管每次合成诚实输出当前时长 + 权重。

### 5.8 音频产物与状态

**音频经段握柄 `IAudioSegment` 交付**（不是扁平 pull）——因为下游 effect 链按段增量重渲染，段是 effect 的失效/重渲染单元。

```csharp
public interface IAudioSegment : IDisposable   // Dispose() = 删除该段（重分片/改长度或位置时重建）
{
    void Write(int offset, ReadOnlySpan<float> samples);  // 段内 [offset, offset+len) 就地写；span 借用语义，返回后可复用缓冲
    void Commit();                                        // 标该段音频已固定——送 effect 的【唯一闸门】
}
```

- 段经 `context.CreateAudioSegment(sampleOffset, sampleCount, sampleRate)` 申请：`sampleOffset` = 全局起始采样位置（**插件 native 率**，全局 0 秒 = 采样点 0）；`sampleCount` = 段长（采样数）；`sampleRate` = **该段的 native 采样率**（你传入，宿主据此解释——等于工程率直读、不等套一层重采样，集中宿主一处）。**采样率随段走、可逐段不同**（如提供合成采样率下拉）。
- 段的**起始与长度创建时固定**（宿主一次性分配缓冲，你就地写、渐进合成不累积重拷）；位置/长度要变 → `Dispose()` 旧段、`CreateAudioSegment` 新段。每次重渲染一段都「丢旧建新」。
- **`Commit()` 是送 effect 的唯一闸门**：Commit 前的 `Write` 只供进度/波形展示；冻结数据（Commit）才进 effect。所以合成爆发期不会拖着昂贵 effect 频繁重跑。
- 写入/提交/释放**全在数据线程**（worker 渲染完，在 marshal 回数据线程的续延里写）。
- **静音段**：宿主缓冲零初始化，`CreateAudioSegment` 后直接 `Commit()`、无需 `Write`。

**状态带 `SynthesisStatusSegment`**（`GetStatus()` 返回，宿主据此着色/进度/报错）：

```csharp
public struct SynthesisStatusSegment
{
    public double StartTime; public double EndTime;       // 秒
    public SynthesisSegmentStatus Status;                 // Pending / Synthesizing / Synthesized / Failed
    public string? Message;                               // Failed=错误信息；Synthesizing=可选阶段文案（如「正在算音素时长」），宿主原样展示
    public double Progress;                               // Synthesizing 时 [0,1]，不报进度保持 0
}
```

- 状态段与音频段**解耦**：前者是 UI 状态带、后者是 effect 失效单元，两套分区可不同，宿主不假设对齐。
- **`StatusChanged` 是唯一刷新信号**：产物（音频/音高/回显/音素）或状态有任何更新，触发它，宿主收到即重读重绘。出方向事件允许任意线程触发、宿主负责 marshal——但你的产物字段须在数据线程换引用（换引用即不可变发布）。

**回显轨数据**走 `SynthesizedParameters`（`IReadOnlyMap<string, SynthesizedParameter>`，key 对齐 `GetSynthesizedParameterConfigs`）：

```csharp
public sealed class SynthesizedParameter { IReadOnlyList<IReadOnlyList<Point>> Segments { get; } }  // 分段折线，段内 Point=(秒,值)，段间断开
```

### 5.9 失效与增量重合成

正确的增量重合成 = 「廉价标脏 + 收口重活」。在会话构造时订阅 context，handler 里**只做廉价标脏**，把重活（如重分块）推迟到 `Committed`：

```csharp
// 构造时接线（数据线程）
mNotesSub = NotifiableExtensions.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);  // 自动覆盖成员增删
context.Notes.ItemAdded   += _ => mNeedResegment = true;
context.Notes.ItemRemoved += _ => mNeedResegment = true;
context.PartProperties.Modified += MarkAllDirty;
context.Pitch.RangeModified         += OnRangeModified;   // (startTime, endTime) 秒：只标脏相交的块
context.PitchDeviation.RangeModified += OnRangeModified;
if (context.Automations.TryGetValue("Growl", out var growl)) growl.RangeModified += OnRangeModified;   // ← 构造期即可订阅自己声明的轨
context.Committed += () => { if (mNeedResegment) Resegment(); };   // 逻辑编辑收口：一次性做重活
```

> **构造期订阅自己声明的自动化轨是可靠的**：声明（`GetAutomationConfigs`）在引擎上、宿主在建会话之前已据此填好轨集合，故会话构造函数里 `context.Automations` 已含你声明的轨（`TryGetValue` 取得 / 可枚举）。绘制该轨后的区间失效经此回调送达 → 标脏 → 下个调度 tick 重渲。若漏订阅，绘制完参数将不触发重渲（轨数据变了但没人标脏）。

- **三种最小变更事实**：①字段变了（订阅 `note.StartTime/EndTime/Pitch/Lyric/Phonemes/Properties` 的 `Modified`，必要时用 `WillModify` 抓旧值作废旧区间）；②区间变了（`ISynthesisAutomation.RangeModified` 带秒范围）；③集合变了（`Notes` 增删，`WhenAny` 自动接线新成员）。这些事实映射到哪些段、重合成到管线哪一级（失效依赖图）**归你**——机制粒度支撑最精细策略，也允许「任何通知 → 全部标脏」的懒实现。
- **`Committed` 是收口点**：每个逻辑编辑（一个 command，含单条编辑）的全部通知发完后触发一次（单条编辑也补发，所以你无需区分「在不在批量中」）。批量编辑（移调几百个 note）因此**只重分块一次**。
- **tempo 变化无独立信号**：它被分解为 note 边界秒值变（`StartTime/EndTime.Modified`）+ 自动化秒映射移位（受影响轨的全区间 `RangeModified`），你用既有订阅就收到了。
- **务必在 `Dispose` 退订**——虽然 context 短命（随会话死，泄漏结构性不可能），但退订是好习惯，也便于释放你持有的模型/段句柄。`Dispose` 里还要 `Dispose` 所有音频段。

> 重叠 note（和弦）分块陷阱：按 note 间隙分块时，判间隙要用「组内**最大**结束」而非「上一 note 的结束」——同起点和弦里上一 note 可能结束更早，用它会把仍在响的长音错误切出去。块尾同理取 `notes.Max(n => n.EndTime)`。

### 5.10 原生依赖与模型打包

voice 引擎常依赖原生运行时（ONNX Runtime 等）、模型权重、发音词典（dict）。打包规则：

- **私有依赖随包分发、与其他插件隔离**：你的第三方托管库、原生 `.dll`/`.so`/`.dylib` 放进**包文件夹**，会被加载进你这个包专属的 ALC。不同插件捆绑不同版本的同一个库**不会冲突**。SDK 程序集（`TuneLab.Foundation` / `TuneLab.SDK`）和 .NET 运行时由宿主共享，**不要**打进包（见 §3）。
- **定位包内资源（模型/dict/原生库）**：用你自己程序集的位置拼绝对路径，**不要**用工作目录或 `AppContext.BaseDirectory`（那是宿主目录）：

  ```csharp
  static readonly string PackageDir =
      System.IO.Path.GetDirectoryName(typeof(MyVoiceEngine).Assembly.Location)!;
  // 之后：Path.Combine(PackageDir, "models", voiceId, "acoustic.onnx") 等
  ```
- **原生库的加载**：把原生 `.dll` 与你的托管 `.dll` 放在**同一目录**（包根），默认探测通常能直接 P/Invoke 到。若用 ONNX Runtime 这类带原生后端的 NuGet 包，让其原生库随包输出到包根即可；跨平台时按目标平台分别提供对应原生库，并在 manifest 用 `platforms` 过滤（如某声库只发 Windows）。
- **大模型权重不要塞进 `.tlx`**：`.tlx` 是即装即载的安装包，几百 MB 的模型塞进去会让安装/加载很重。推荐两种形态：
  - **资源包分离**：模型作为独立的资源包（无代码，`type` 声明用途），引擎运行时去发现；或
  - **走扩展设置让用户配模型路径**：引擎实现 `IExtensionSettings`，用 `TextBoxConfig` 暴露「模型目录」设置项，用户在「设置 → 扩展」填好路径，你在 `ApplySettings` 收下、`Init`/`CreateSession` 时从该路径加载（见 §8）。API key 等密钥用 `TextBoxConfig { IsPassword = true }`，宿主掩码显示 + 安全落盘。
- **`Init` 里加载、失败抛异常**：模型/词典加载放 `Init`（或更懒，首次 `CreateSession` 时）。加载失败直接抛异常，宿主在调用边界 catch、把该插件标为加载失败并在侧边栏反映原因，不会崩溃主程序。

### 5.11 接口职责速查

| 成员 | 线程 | 职责 |
|---|---|---|
| `IVoiceSynthesisEngine.VoiceSourceInfos` | 任意（同步读） | 声库目录；**必须立即返回不阻塞**（Init 期缓存） |
| `IVoiceSynthesisEngine.Init/Destroy` | — | 加载/释放常驻状态（模型）；失败抛异常 |
| `IVoiceSynthesisEngine.CreateSession` | 数据线程 | 每 part 建一会话 |
| `IVoiceSynthesisEngine.GetPartPropertyConfig`/`GetNotePropertyConfig` | 数据线程 | 属性面板（纯函数 of voiceId+当前值，可条件显隐） |
| `IVoiceSynthesisEngine.GetAutomationConfigs` | 数据线程 | 可编辑自动化轨集合（NaN⇒分段；避开保留名） |
| `IVoiceSynthesisEngine.GetSynthesizedParameterConfigs` | 数据线程 | 只读回显轨声明（恒分段形） |
| `IVoiceSynthesisSession.DefaultLyric` | 数据线程 | 新建 note 默认歌词（会话级运行时取值） |
| `GetNextSegment` | 数据线程 | peek 下一脏块边界（无副作用、确定性） |
| `SynthesizeNext` | 同步前缀=数据线程；之后 worker | 拉快照 → offload 渲染 → 回数据线程发布 |
| `GetSnapshot` | **仅同步前缀** | 物化不可变快照（圈定 notes + 开窗） |
| `CreateAudioSegment` / `IAudioSegment.Write/Commit` | 数据线程 | 申请并写音频段；Commit 是送 effect 的闸门 |
| `SynthesizedPitch/Parameters/Phonemes`、`GetStatus` | 数据线程发布、可跨线程读 | 产物；发布即不可变 |
| `StatusChanged` | 任意触发、宿主 marshal | 唯一刷新信号 |
| `Dispose` | 数据线程 | 退订、释放模型与段句柄 |

---

## 6. 编写 Effect 插件

效果器（effect）对**已合成的整段音频**做变换。它面向**耗时较长的离线模型**（如 SVC 换声、神经音色转换），不是实时的 VST 式效果器。

实现 `IEffectEngine`。需要**无参构造函数**。效果器 id 写在 `description.json` 的 `engine`，实现类列进 `classes`（宿主按 `IEffectEngine` 接口认领，不再用 attribute）。引擎是每种效果器类型一个；宿主为工程里每条「effect 实例 × 上游音频段」创建一个**持久厚处理器** `IEffectProcessor` 驱动它。处理器持有自己那一段的上下文 `IEffectContext`、**自订阅、自管失效与重处理**——引擎私有的失效图（哪条参数/哪段自动化标脏触发哪些内部重算）落在处理器内部，宿主无从复制，故为厚模型。

manifest 条目：`{ "type": "effect", "engine": "MyEffect", "name": "My Effect", "classes": ["My.Ns.MyEffectEngine"], "assembly": "MyEffect.dll" }`（`engine` 是不可变身份；`name` 可选显示名、可加 `localizations` 翻译；宿主在 `classes` 里找实现 `IEffectEngine` 的类）。

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

public class MyEffectEngine : IEffectEngine   // engine id 在 manifest 的 "engine" 声明
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
        if (mContext.Automations.TryGetValue("intensity", out var automation) && count > 0)
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

相关接口都在 `TuneLab.SDK`：`IEffectEngine` / `IEffectProcessor` / `IEffectContext` / `IUpstreamAudioSegment` / `IAudioSegment` / `IEffectPropertyContext` / `ISynthesisAutomation`。

---

## 7. 编写 Instrument 插件

Instrument 是**多声部音源**（合成器 / 采样器 / 和弦音源）。它与 voice **机制同构**（引擎生命周期、调度 peek/commit、隔离快照、音频段交付、effect 链、扩展设置全部一样），接口族用 `IInstrument*` 前缀平行成套，与 voice 族无继承。**只在三处与 voice 实质不同**：

- **note 满末、不去重叠**：`IInstrumentSynthesisNote.EndTime` / `InstrumentSynthesisNoteSnapshot.EndTime` 是 note 满末（`Pos+Dur`），宿主**不**钳到下一 note 起点。`Notes` 直传原始可重叠 note（和弦 / 多声部），引擎自行叠加发声（参考实现按每个 note 的 pitch 各加一段波形、混音求和）。
- **无歌词 / 无音素**：`IInstrumentSynthesisNote` 没有 `Lyric` / `Phonemes`；会话没有 `DefaultLyric`、不产 `SynthesizedPhonemes`。
- **无 pitch 曲线、产物仅音频**：`IInstrumentSynthesisContext` 没有 `Pitch` / `PitchDeviation`（v1 纯按 note 整数 `Pitch` 发声）；会话不产 `SynthesizedPitch`。仍可声明 automation 轨与 `SynthesizedParameters` 回显（引擎不声明即无）。

manifest 条目：`{ "type": "instrument", "engine": "MyInstrument", "name": "My Instrument", "classes": ["My.Ns.MyInstrumentEngine"], "assembly": "MyInstrument.dll" }`。

音源目录与 voice 同形：`IInstrumentSynthesisEngine.InstrumentSourceInfos`（按 id 键）。**一插件一乐器** = 单条目；**容器式**（如 Kontakt：一个引擎挂多个外置资源包乐器）在 `Init()` 扫描已装资源包、填多条目，`InstrumentId` 选具体乐器——宿主统一扁平选择器照常呈现。

> 完整接口契约与设计依据见 [instrument-sdk-design.md](instrument-sdk-design.md)；最小参考实现（一个引擎挂 sine/square 两音色、多声部叠加合成）见 `tests/plugins/V1.Instrument`。

相关接口都在 `TuneLab.SDK`：`IInstrumentSynthesisEngine` / `IInstrumentSynthesisSession` / `IInstrumentSynthesisContext` / `IInstrumentSynthesisNote` / `InstrumentSynthesisSnapshot` / `InstrumentSynthesisNoteSnapshot` / `InstrumentSourceInfo` / `IInstrumentSynthesisPartPropertyContext` / `IInstrumentSynthesisNotePropertyContext`（音频 / automation / 状态 / 回显等叶子类型与 voice 共用）。

---

## 8. 扩展设置（IExtensionSettings）

让你的扩展（extension，即一个 voice/effect 等能力实现）声明一组**随宿主持久化、跨工程共享**的设置——典型如 **API key、模型路径、设备选择**。宿主在「设置」窗口渲染面板、按 extension 落盘、运行时回喂。

> **与属性面板的区别**：voice 的 `GetPartPropertyConfig`/`GetNotePropertyConfig`、effect 的 `GetPropertyConfig` 声明的是**随工程序列化的实例/段级**属性（每个 part/note/effect 实例各一份，存进 `.tlp`）。本节的设置则是**扩展自身**的配置，与具体工程无关、跨工程共用、单独落盘。两者用同一套控件配置词汇（`ObjectConfig`），但生命周期与存储位置完全不同。
>
> 粒度是 **per extension**（一个 voice/effect 能力一份），不是 per 安装包（ExtensionPackage 可含多个 extension，各自独立设置）。

### 7.1 接入方式

设置是 **opt-in** 的：让你的能力实现类**额外实现** `IExtensionSettings` 即可，无设置的扩展不必理会。宿主对每个已注册能力做 `x is IExtensionSettings` 探测，实现了才显示其设置面板。

```csharp
public sealed class MyVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings
{
    // —— 声明 schema（复用属性面板同款控件配置）——
    // 是 context 当前值的纯函数（宿主在值变更后重算并 diff 到控件）；且【必须在 Init 之前可调】
    //（"先填模型路径，Init 才加载得了模型"——schema 不能依赖 Init 后的状态）。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<string, IControllerConfig>();
        props.Add("model_path", new TextBoxConfig { DisplayText = "模型路径", DefaultValue = "" });
        props.Add("api_key", new TextBoxConfig { DisplayText = "API Key", IsPassword = true }); // 密钥：掩码显示 + 加密落盘
        props.Add("use_gpu", new CheckBoxConfig { DisplayText = "使用 GPU", DefaultValue = false });
        // 动态/条件项：据已填值决定显隐（如勾了 GPU 才暴露设备字段）。
        if (context.Settings.GetBool("use_gpu", false))
            props.Add("gpu_device", new TextBoxConfig { DisplayText = "GPU 设备", DefaultValue = "" });
        return new ObjectConfig { Properties = props };
    }

    // —— 接收持久化的值 ——
    // 宿主在【加载完成后】灌一次（早于任何 Init / 会话），用户在设置窗口【保存后】再灌一次。自存自用。
    public void ApplySettings(PropertyObject settings)
    {
        mModelPath = settings.GetString("model_path", "");
        mApiKey    = settings.GetString("api_key", "");
        mUseGpu    = settings.GetBool("use_gpu", false);
        // 之后在 Init / CreateSession / CreateProcessor 里用这些值。
    }

    // IVoiceSynthesisEngine 的其余成员……
}
```

### 7.2 要点

- **密钥字段**：用 `TextBoxConfig { IsPassword = true }` 标出。宿主据此掩码显示，并按平台安全落盘：Windows 用 DPAPI 把密文就地存进配置文件（仅原用户原机可解）；macOS 存进钥匙串（Keychain）、配置文件只留空串。**无安全存储可用时不保存该密钥字段（绝不明文）并告警**。官方支持 Windows / macOS。
- **schema 须 Init 前可达**：`GetSettingsConfig` 不得依赖 `Init` 后才有的状态——用户得先在设置面板填好（如模型路径）你才 `Init`。把它当纯函数写（同输入同输出、无副作用、轻量）。
- **动态/条件项**：`GetSettingsConfig(context)` 是 `context.Settings`（当前已填值）的纯函数；用户改值后宿主按当前值重算并 diff 到控件树，故可据已填值显隐字段（如某开关打开才出现的字段）。
- **回喂时机**：宿主在启动加载完所有扩展后回喂一次（此时尚未 `Init`），用户保存设置后再回喂一次。设置变更对**已在运行**的会话/处理器的影响（是否需要重建）由你自己决定与处理。
- **本地化**：设置项 `DisplayText` 由你自译（与属性面板同范式，按 `TuneLabContext.Global.Language` 出文案），宿主不参与查表。
- **manifest 无需声明**：设置 schema 纯走代码（`GetSettingsConfig`），`description.json` 不掺和。

### 7.3 用户在哪里改

「设置」窗口（顶部菜单进入）→「扩展」分页：每个声明了设置的扩展一段「显示名 + 设置面板」。编辑在**关闭窗口 / 切走分页**时统一落盘并回喂。

> agent 模型引擎有自己的侧边栏设置入口，不在「扩展」分页里。

相关接口在 `TuneLab.SDK`：`IExtensionSettings` / `IExtensionSettingsContext`（+ 控件配置 `ObjectConfig` / `TextBoxConfig` / `CheckBoxConfig` / `ComboBoxConfig` / `SliderConfig`）。

---

## 9. 打包、安装、卸载

- **包格式**：把包文件夹打成 zip，扩展名改为 **`.tlx`**，要求 `description.json` 在 zip 的**根目录**。
- **安装**：在 TuneLab 里把 `.tlx` 拖进窗口，或用扩展侧边栏的「Install Extension」。安装即解压到扩展目录并**立即加载**（无需重启）。
- **扩展目录**：`%AppData%/TuneLab/Extensions/<包名>/`（Windows）。侧边栏「Open Extensions Folder」可直接打开。
- **卸载**：扩展侧边栏每个条目的「Uninstall」。卸载在**编辑器关闭后**由独立的 `ExtensionInstaller` 完成（释放文件锁后删除），可选择「立即重启」生效。

---

## 10. 加载与校验行为

TuneLab 加载每个包时：**发现** → 读 `description.json` **判代际**（有 `id` = V1）→ **校验**（sdk-version 兼容？平台匹配？）→ 为包建一个 **per-folder ALC** → 逐条按 `assembly` 加载、**扫 `classes` 候选类按本 `type` 所需接口认领并实例化注册**（不再反射扫 attribute）。

- 任何一步失败都**优雅降级**：只跳过出问题的插件/条目，**不会让主程序崩溃**，并在扩展侧边栏与日志里反映加载状态。
- `sdk-version` 高于宿主 → 该包被跳过并提示。
- `platforms` 不含当前平台 → 该插件被跳过。
- 条目级校验失败（`assembly` 找不到、`classes` 里没有任何类实现该 `type` 所需接口、命中类缺无参构造）→ **只这一条目失败**，原因写进侧边栏 tooltip；同包其余条目照常加载（部分加载）。

---

## 附录：Legacy 插件

改版前发布的老插件（链接旧的 `TuneLab.Base` / `TuneLab.Extensions.Formats` / `TuneLab.Extensions.Voices`）属于 **Legacy**：它们的 `description.json` **没有 `id`**（或根本没有该文件）。TuneLab 据此识别为 Legacy 并交给兼容层处理。

- **新插件请勿沿用 Legacy 形态**——务必带 `id` 与新版字段，按本文编写。
- Legacy 兼容层会长期保留，老插件无强制迁移压力；但新功能（如 effect）只在 V1 提供。
