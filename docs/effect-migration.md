# Effect 分支迁移 · 工作上下文

> 本文档用于在多个独立 session 之间共享设计共识。
> 每个新 session 在开始具体话题之前，请先完整阅读本文。

---

## 一、背景

`effect` 分支是早年开始的一个大型分支，包含：

1. **新功能**：Effect（效果器）插件类型的支持
2. **重构**：项目结构调整、基础类与接口规范化
3. **扩展系统升级**：SDK 程序集分层、adapter 兼容机制

分支当前状态：
- 与 master 的分叉点：commit `54134e9` (`feat: 增加打开log选项`)
- effect 领先 master：62 commits，583 文件，+12018 / -3132
- master 领先 effect：57 commits，74 文件，+5540 / -294
- 双方都改了"扩展系统"和"DataInfo / EditorInfo"等区域，直接 merge 会产生大量冲突

---

## 二、工作方式（已达成共识）

### Effect 分支访问方式：使用 git worktree

effect 分支已通过 `git worktree` 检出到本仓库**同级目录**的 `../TuneLab-effect`（相对于本仓库根目录）。

```
<parent>/
  TuneLab/             ← 当前仓库（master 或 feat/effect-migration）
  TuneLab-effect/      ← effect 分支的独立 checkout（共享 .git）
```

在本仓库内引用时一律使用相对路径 `../TuneLab-effect/...`。

新 session 进来后**直接用 Read / Glob / Grep 访问 `../TuneLab-effect/` 下的文件**即可，不需要 `git show origin/effect:<path>`。

如果本地没有这个 worktree（例如初次 clone），执行：

```powershell
git worktree add ../TuneLab-effect origin/effect
```

迁移全部完成、不再需要参考 effect 分支时：

```powershell
git worktree remove ../TuneLab-effect
```

### 不在 effect 分支上继续改

原因：
- 落后 master 太多，merge 必然冲突地狱
- 当时的设计（如 `_V1` 后缀的 SDK 类型）已被推翻，绝大多数代码需要重写
- git history 会很乱，code review 困难

### 从 master 新开分支，逐话题推进

```
master
  └─ refactor/foundation (或类似名)
       Phase 1: 话题 #1（项目结构 & .NET 9）
       Phase 2: 话题 #2（Foundation 抽离）
       ...
```

每个话题：
1. 切到迁移分支
2. 阅读 effect 分支对应文件作为**设计参考**，不直接 cherry-pick commit
3. 在 session 中讨论清楚设计，达成共识
4. 用新设计实现，验证，commit
5. 推到一个独立 PR 或合并 commit

### effect 分支的定位

是**设计笔记**，不是要继承的代码。可以 `git show origin/effect:<path>` 查看任意文件作为参考，但实现按新设计来。

---

## 三、已确定的设计共识

### 1. SDK 命名：公开类型不带版本后缀

❌ 旧方案：`IEffectEngine_V1`、`MonoAudio_V1`、`Point_V1` 这种带版本后缀的公开类型名

✅ 新方案：
- 公开 SDK 类型名永远干净：`IEffectEngine`、`MonoAudio`、`Point`
- 版本通过**程序集 version** 区分（`TuneLab.SDK.Effect.dll` v1.0.0 vs v2.0.0）
- 插件作者升级 SDK 时不需要全文替换版本后缀

### 2. Adapter 隔离 + 用 `extern alias` 在适配层"画回"版本

适配代码在**独立 .csproj**（命名 `Compat.<被桥接的那一代>`：当前桥接 Legacy 插件的叫 `TuneLab.Hosting.Compat.Legacy`，未来桥接 V1 插件的叫 `Compat.V1`），不与主程序混合。

> **#8 修订**：`extern alias` 只消歧**同名不同版**程序集，首次真正需要它是 **V1→V2**（`SDK.*.dll` v1/v2 撞名，`Compat.V1` 用 `extern alias SdkV1` 桥接 V1 插件）。当前的 **Legacy→V1 这一代无需 extern alias**——Legacy（`Base`/`Extensions.Formats`/`Extensions.Voices`）与 V1（`Primitives`/`SDK.*`）程序集**名字不同**，按 assembly identity 直接共存。详见 §三.15。下例演示的是未来 V1→V2 的同名跨代形态：

适配项目的 `GlobalUsings.cs`：

```csharp
extern alias SdkV1;

global using IEffectEngine_V1 = SdkV1::TuneLab.SDK.Effect.IEffectEngine;
global using MonoAudio_V1     = SdkV1::TuneLab.SDK.Base.Synthesizer.MonoAudio;

global using IEffectEngine = TuneLab.SDK.Effect.IEffectEngine;
global using MonoAudio     = TuneLab.SDK.Base.Synthesizer.MonoAudio;
```

适配层代码：

```csharp
class EffectEngineAdapter : IEffectEngine       // 当前版
{
    IEffectEngine_V1 _legacy;                   // 老版（一眼可见）
    public void Process(MonoAudio input) { ... }
}
```

好处：
- 插件作者代码干净
- 适配作者仍能在类型名上看到版本
- 编译器强制类型转换（`IEffectEngine` 和 `IEffectEngine_V1` 是不同类型）
- 主程序**不引用老 SDK 程序集**，避免类型混用

**Compat 程序集划分粒度**：按**代**分（`Hosting.Compat.Legacy`、未来 `Compat.V1`/`Compat.V2`），**不**按域（Format / Voice / Effect）分。理由：`MonoAudio` 等基础桥接类型在多域适配间共享，按域分必然挤出 `Compat.*.Common` 第三方项目或重复代码；compat 层的真实生命周期是"整代下线"，按域独立 deprecation 没有实际需要。

### 3. 老 SDK 留**源码** legacy 程序集，隔离冻结（#8 修订）

> 原方案"只留 dll 不留源码"已被 #8 推翻。本质分歧是"冻结靠物理 vs 冻结靠纪律"：纯二进制等同性确定但不可读/不可调试，源码可维护但等同性靠守。取源码 + 护栏的折中（effect 分支已用独立 sln 隔离）。理由详见 §三.15。

老 SDK（Legacy：`TuneLab.Base` / `Extensions.Formats` / `Extensions.Voices`）保留**源码**于 `legacy/sdk/src/`（可读、可调试、diff 友好），靠三条护栏保证 ABI 等同性：

1. **独立 sln 隔离**——老 SDK 源码 + `Hosting.Compat.Legacy` 自成一个 sln，**不进 `TuneLab.sln`**（避免全仓 rename / analyzer fix-all 物理误伤）。
2. **csproj 钉死 ABI 标识**——`AssemblyVersion`/`Version` 显式写死成**当年发布版本**（绑定等同性的命门），`TargetFramework` 锁 net8，关闭跟随主仓的 analyzer/props。
3. **目录与文件头标注"冻结·禁改"**。

（可选第 4 条：CI 用 `Microsoft.DotNet.ApiCompat` 对比黄金 dll，源码可读 + 物理校验双保险；V1 阶段可不上。）`AssemblyVersion` 具体值待 #9 清点野外插件后钉。

### 4. 主程序的引用边界

`TuneLab.csproj` 只引用：
- 当前 SDK 程序集
- 适配层程序集（通过其暴露的工厂接口，返回的是**当前版**类型）

主程序代码的"语言世界"里，老 SDK 类型根本不存在。

### 5. ALC 隔离与 Capability Pattern（#8 已决，详见 §三.15）

- **ALC（AssemblyLoadContext）隔离**：#8 定为**永远 per-plugin ALC + 共享契约 + 非 collectible 起步**；collectible 热卸载留 #10。**纠正原表述**：ALC 并**不**减少 adapter 代码（其量由契约形状差驱动），也**不**提供真正的崩溃隔离（非进程边界）——它解决的是**依赖版本冲突**、**多代同名 SDK 并存**、（collectible 下）**热卸载/文件锁释放**。跨 .NET 版本兼容是 TFM ABI 地板 + roll-forward 的事，与 ALC 无关。
- **Capability Pattern（参考 CLAP）**：核心接口极小 `IPlugin { object? GetExtension(string id); }`，能力为独立小接口加字符串 ID（如 `"effect.processing/1.0"`）。加新能力不需要出 SDK 大版本。#8 结论：它与 compat **正交**，compat 为 Legacy 插件**合成**能力面；具体字符串方案 + API 留 #10/#11。

### 6. 性能考量

Adapter 对**冷路径**（Format I/O、property panel）开销可忽略。

~~需在 #8 中基准测试的两个点~~ —— **#8 已实测（见 §三.15 性能表）**：`Properties[key]`、双向集合 wrapper、enumerator 装箱的分配**全落在冷设置路径**（每 note/part 边界转换一次），对合成耗时摊薄到可忽略；`Properties[key]` 在 eager 转换下**零分配**。决定性规则：**边界 eager 转换，绝不给插件 lazy 逐访问 wrapper**。

接口设计上已避免最致命的 per-sample 虚调用模式（automation 是批量 API）。

### 7. 最终程序集蓝图

迁移完成后的目标布局。**各程序集由对应话题引入并填内容**，不在 #1 提前建空壳。

| 程序集 | TFM | 引入话题 | 角色 |
|---|---|---|---|
| `TuneLab` | host TFM | 已存在 | 主程序 WinExe |
| `TuneLab.Foundation` | host TFM | #2 | 纯通用富基础（引用 `Primitives`；替代 `TuneLab.Base`） |
| `TuneLab.Primitives` | **ABI 地板** | #7 | 中性冻结内核：跨边界值类型（`Point`/`MonoAudio`/Property 值模型）+ DataStructures map 接口族（见 §三.11）；`Foundation` 与 `SDK.*` 都引用（见 §三.10） |
| `TuneLab.SDK.Base` | **ABI 地板** | #7 | 插件服务接口（`ILog`/`ITuneLabContext`…）+ 通用控件 Config 家族（`IControllerConfig` + Slider/CheckBox/ComboBox/TextBox/Object，见 §三.12）+ 引用 `Primitives` |
| `TuneLab.SDK.Format` | **ABI 地板** | #7 | format 插件 SDK（含工程序列化模型 `DataInfo`） |
| `TuneLab.SDK.Voice` | **ABI 地板** | #7 | voice 插件 SDK |
| `TuneLab.SDK.Effect` | **ABI 地板** | #7 | effect 插件 SDK |
| `TuneLab.Extensions.Format` | host TFM | #7 | 内建 format 实现 |
| `TuneLab.Extensions.Voice` | host TFM | #7 | 内建 voice 实现 |
| `TuneLab.Extensions.Effect` | host TFM | #7 | 内建 effect 实现 |
| `TuneLab.Hosting.Compat.Legacy` | host TFM | #8 | Legacy 老插件适配（单程序集，见 §三.2/§三.15） |
| `ExtensionInstaller` | net8 | 已存在 | 独立小工具 |

> 原蓝图曾列 `TuneLab.Core`（host-TFM 业务核心契约）；#3 决定**暂不建**（理由见 §三.10），表中已移除。host 业务契约待 #4 活文档模型 / command-undo 出现时再评估。

**TFM 策略**：

- **host TFM** = 主程序运行时；目前 net8，未来独立 PR 升级。结构重构与 TFM 升级**永远分开**。
- **ABI 地板** = `Primitives` + SDK.\* 系列锁在 host TFM 之下的一个 LTS（首次引入时锁 net8）。插件按此地板编译，host 升级到新 TFM 不破坏老插件加载。

**命名约定**：

- 公开类型**不带** `_V1` 版本后缀（见 §三.1）
- `Extensions.*` 和 `SDK.*` 都用**单数**（`Format` / `Voice` / `Effect`），与 `Microsoft.Extensions.*` 主流惯例一致
- 老 `Extensions.Formats` / `Extensions.Voices` 复数程序集（连同 `Base`）作为 **Legacy 源码隔离冻结**保留在 `legacy/sdk/src/`（#8 修订 §三.3），供 `Compat.Legacy` 直接引用（名字不同，**无需 extern alias**，见 §三.15）
- 新老插件区分由 `SDK.` 前缀 + 程序集版本 + `extern alias` 三层承担，**不**依赖单复数命名

### 8. 项目配置约定

- **csproj 的所有有意义设置就地内联**（`Nullable` / `ImplicitUsings` / `LangVersion` / `TargetFramework` 等），不依赖 `Directory.Build.props` 继承。原因：
  - SDK.\* / Foundation / Extensions.\* 系列 csproj 是**公共契约的一部分**——会被第三方插件开发者 reference、阅读、甚至作为新插件 csproj 的模板。设置内联保证它们在脱离仓库目录树后仍然自描述。
  - 这也是 VS / `dotnet new` 模板的默认选择：模板必须假设 "csproj 可能在任何地方被独立打开"，所以选择 **locality > DRY**。这个约束在我们仓库同样成立。
  - "DRY 节省的那几行" 远不抵 "外部读者搞不清这个 csproj 用了什么编译规则" 的代价。
- `Directory.Build.props` 只保留**兜底默认**：当前是 `<Nullable>enable</Nullable>`，作用是"哪天新建 csproj 忘了写也不会沉默地关掉 nullable"。**不**承担集中配置职责。
- TFM 永远不进 props：host TFM 与 SDK ABI 地板分两档，必须各 csproj 显式声明。

### 9. Foundation 边界与命名（#2 确立）

**准入判据**：不 reference 任何 TuneLab 业务模型类型（Note / Track / Tempo-as-data / Project…），不依赖 UI/Avalonia，不依赖插件宿主。对原始类型运算的"音乐数学"（`MusicTheory`、pitch↔freq、tempo↔time 换算）算通用工具，留 Foundation；承载业务语义的数据类归业务层（`DataInfo`→`SDK.Format`、活文档模型→host；详见 §三.10——原计划的 `Core` 不建）。`Science` 整体留 Foundation，**不**拆独立程序集。

**Base→Foundation 兼容形态**：`TuneLab.Foundation` 是**新建程序集**（fork 自 Base 内容），`TuneLab.Base` 源码**冻结**。主程序与内建扩展全部改引 Foundation；旧第三方插件继续引用冻结的 `TuneLab.Base.dll`，compat 层（#8）另起 csproj 引用 Base。Base / Foundation 是不同程序集名，靠 assembly identity 天然共存，**这一对不需要 extern alias**（extern alias 只用于同名不同版的 `SDK.*`）。Base 最终处置（live csproj vs 归档 dll）留 #8。

**命名约定补充**：
- 文件夹/命名空间跟随：`Data`→`Document`、`Structures`→`DataStructures`、`Properties`→`Property`（单数）；namespace 与目录一致。
- **数据层 / UI 层信号用不同动词，刻意区分**：数据层用 `Modify`（`WillModify`/`Modified`），UI 层用 `Change`（`ValueWillChange`/`ValueChanged`/`ValueCommitted`）——看到动词即知所在层。`WillModify`/`Modified` 是刻意的"将来/过去"时态对（`Will` 为对齐 `Modified` 的过去时；英语无屈折将来时，时态轴上无法做到单词级对称——已论证，**勿再"优化"**）。
- `Science` / `Document` / `Head` 命名保留（`DataDocument` 仿 git 设计，`Head` 沿用 git 隐喻）。

**边界归位**：值编辑的 UI 绑定 `IValueController`/`IDataValueController` 属 UI，移至 `TuneLab/GUI/Controllers`，不在 Foundation。Config 类型（`NumberConfig` 等）暂留 `Property/`（它是插件 SDK 编译契约）；值模型 / Config / Controller 的**终态拆分**归 #5。

### 10. Core 取舍、Primitives 冻结内核、插件服务注入（#3 确立）

**不建 `TuneLab.Core`**：effect 分支的 `TuneLab.Core` 是命名空间都不统一的 grab-bag（DataInfo / Synthesizer / ControllerConfigs / Environment），按性质各归各位（见下）后，host-TFM 的"业务核心契约层"此刻无实质内容，按 §三.7"不提前建空壳"**暂不建立**；待 #4 活文档模型 / command-undo 等真正的 host 业务契约出现再评估。

**数据 / 服务分家**（SDK 设计总纲）：
- **数据**（值/DTO：`Point`、`MonoAudio`、Property 值模型、ControllerConfigs、`DataInfo`）→ **具体类型**，插件直接 `new`，不接口化、不走反射 Factory。
- **服务**（`IEffectEngine`/`IFormat`/`ILog`/`ITuneLabContext`）→ **接口**，host 注入实现；演进靠默认接口方法（DIM）+ 版本治理。
- 关键认知：接口式只给**服务**带来演进自由；对**数据**是幻觉——改数据类型在任何方案下都得走版本化，故数据用共享具体类型即可，别为幻觉牺牲 `new` 的 DX。

**`TuneLab.Primitives`：中性冻结内核**：
- 跨边界值类型只定义**一份**，放中性程序集 `TuneLab.Primitives`（ABI 地板/net8 LTS、零依赖、冻结）。`Foundation` 与 `SDK.Base` **都引用它**；插件只引用 `SDK.* + Primitives`，**永不引用 Foundation/Core**。
- **为何独立成程序集而非 Foundation 内 `public`**：Primitives 与 Foundation **生命周期不同**（冻结 ABI / net8 地板 vs 自由演进 / host TFM），且需可独立版本化供 Compat、需作小而稳的 ALC 共享契约。一个程序集只能有一个 TFM、一个版本号，`internal` + `[InternalsVisibleTo]` **解决不了 TFM / 版本 / 冻结纪律**这几根轴——它只用于**程序集内部**收敛 host 私货（host-only 辅助标 internal + IVT 给主程序），不替代拆分。物理边界本身就是"要暴露就得显式搬进来"的纪律执行机制。
- **边界类型归属**：`Point` / `MonoAudio` / Property 值模型 / DataStructures map 接口族（见 §三.11）→ `Primitives`；`DataInfo` → `SDK.Format`；服务接口 + 通用控件 Config 家族 → `SDK.Base`（Config 归属理由见 §三.12——Foundation 不引用它，不过 Primitives 双方准入）；域专属 Config（`AutomationConfig`）→ 对应 `SDK.*`。

**命名与人体工学**：
- Primitives 类型用**诚实命名空间** `TuneLab.Primitives.*`（**不做命名空间伪装**——伪装会把冻结边界藏回去）；host 侧用 `global using Point = TuneLab.Primitives...Point;` 消除打字摩擦，go-to-definition 仍暴露边界。
- 把 Foundation 类型挪进 Primitives 是**纯 host 内部、编译期**改动（插件不引用 Foundation → 零插件影响）；use-site churn 由 global using 别名吸收；真实成本在**依赖卫生**——冻结类型必须先切断对 Foundation 的依赖（Primitives 不能反向引用 Foundation）。

**插件读宿主状态 & Log**：
- `ILog` 进 `SDK.Base`；`ITuneLabContext` + 静态点进 `SDK.Base.Environment`。**采静态全局访问点 `TuneLabContext.Global`**：host 启动时（插件加载前）注入唯一实现 `TuneLabContextGlobal`，插件经它读 `Language`、取 `GetLogger()`——日志器前缀由 host 按**调用者所属 ALC 名（= 插件包目录，`PluginLoadContext` 设定、插件改不了）**自动判定、转发进现有 sink，无需插件自报。
- **反转此前"弃用 static 全局"的决定**：当时理由之一"ALC 隔离下静态每-ALC 一份"对**共享契约程序集不成立**——`PluginLoadContext` 对 `TuneLab.Primitives` / `TuneLab.SDK.*` 返回 null 落 Default ALC（全程一份、跨边界类型标识相等），故 SDK.Base 里的静态对 host 与全部插件就是同一实例。剩下的只是 service-locator 取舍，为"对 effect/voice/format 三类统一、免每插件注入工程"而有意识采用。host 仍自留 Foundation 静态 `Log.*`，与插件 `ILog` 共用同一 sink。

**内核增长纪律**：每纳入一个类型 = 永久 ABI 承诺。内核只在具体插件 API 真需要时**克制、刻意**地增长；优先在 SDK.Base 暴露**冻结接口**、富实现留 Foundation，而非把富类型整个下沉。

**冻结类型的演进（安全路径，落地留 #7/#8）**：
- 非破坏改动（class 加成员、struct 加方法、服务接口用 DIM 加方法）**就地免费**，不升版本。
- 破坏性改动走**整代版本化**：ABI 地板（`Primitives` + `SDK.*`）按"代"统一升版本，旧代经 `Compat.<代>` 单程序集桥接（§三.2 按代分，命名 `Compat.Legacy`/`Compat.V1`…）。当前 Legacy 代留**源码**隔离冻结（§三.3 #8 修订），未来同名跨代（V1→V2）才用 `extern alias`（§三.2）。同版本内零转换；跨版本仅在边界转换，热载荷（`Point[]`、audio `float[]`）可 `MemoryMarshal.Cast` 零拷贝或共享数组引用。主程序绝不引用旧代（§三.4）。

### 11. DataStructures 接口族：冻结内核 vs Foundation 富实现（#4 确立）

**集合接口按 ABI 边界二分**（判据：是否出现在 SDK.\* / ControllerConfigs / PropertyValue / `ISynthesisOutput` 等**插件契约签名**里）：

- **进 `Primitives`（冻结 ABI）**：**map 家族整体**——`IReadOnlyKeyValuePair`、`IReadOnlyMap`、`IMap`、`IReadOnlyOrderedMap`、`IOrderedMap` + 具体 `Map`/`OrderedMap`。证据：`IReadOnlyMap`/`IReadOnlyOrderedMap` 出现在 `Properties`/`ObjectConfig`/`AutomationConfig`/`VoiceInfos`/Format 注册表；`IMap`（可变）出现在 `ISynthesisOutput.SynthesizedAutomations`（插件**填**它）。数据=具体类型插件直接 `new`（§三.10），故具体 `Map`/`OrderedMap` 也进 Primitives，并把它实现的接口一并拽入。**最小内核纪律在此省不下**——契约 + 构造需求把整族拽进来。
- **留 `Foundation`（host 富实现，自由演进，永不跨边界）**：`ILinkedList`/`IReadOnlyLinkedList`/`ILinkedNode`/`LinkedList`（唯一用途=`INote`/`IPart` 侵入式相邻链，钢琴卷帘 O(1) 插删 + 前后导航）、`IMutableList`（Document 内部 `IList`+`IReadOnlyList` 的 DIM 调和糖）、`CacheList`/`SafeReadOnlyList`、各 `Wrapper`/`Convert` 扩展。

**对原 §四.4 三问的结论**：
- **三层（IReadOnly/可变/具体）只在边界值得**：读写方向是契约语义（服务回**协变只读** map、插件**填可变** map）。非边界的 `IMutableList` 式 DIM 调和不进冻结内核。
- **`OrderedMap` 存在理由**：声明顺序注册表（property/automation/voice/format 按作者声明序在面板展示）是真实需求 → `IReadOnlyOrderedMap` 是合格的冻结边界类型。
- **`DataList` vs `DataObjectList` 分裂必要且保留**：`DataList<T>`=值快照列表（整列即 info，`ItemReplaced`）；`DataObjectList<T> where T:IDataObject`=子文档树（元素 Attach/Detach、独立参与 undo、`ListModified`）。但整套是 Foundation 内部 live-doc，终态形状与 `DataPropertyObject` 一起在 #5 定。

**规范化形状**（effect 已验证、待 #7 物理落地 Primitives 时采用）：`IReadOnlyKeyWithValue`→`IReadOnlyKeyValuePair`（对齐 BCL）；`Keys`/`Values` 收紧为 `IReadOnlyCollection<>`/有序版 `IReadOnlyList<>`；删 `IReadOnlyOrderedMapExtension.At` 自递归死代码 bug；补 `[CollectionBuilder]` + Empty 单例支持 `= []` 集合表达式。**否决** effect 在 `SDK.Base` 重拷一份 `IReadOnlyMap_V1`/`IMap_V1`/…（违 §三.1/§三.10——map 家族只在 Primitives 定义一份，插件经 `SDK.* + Primitives` 引用）。

**Document/ 泛型框架归 `Foundation`，终态留 #5**：`IDataObject`/`IDataList`/`IDataObjectList`/`DataMap`/`Command`/`Head`/`DataDocument` 不 reference 任何业务类型 → 过 §三.9 准入判据 → Foundation（host-only 基础设施，从不触碰冻结 ABI，可自由演进）。具体活文档模型（`NoteData`…）composite 其上 → host（§三.9 "活文档模型→host" 指此具体层，非泛型机制）。

**物理落地（搬入 Primitives、改名、规范化）留 #7**，本话题不改代码。

### 12. Property 体系：值模型 / Config / live-doc 的三段归位（#5 确立）

承 §三.9 留的尾（值模型 / Config / Controller 终态拆分）与 §三.11 留的尾（live-doc 终态形状）。纯决策，不改代码；物理落地随 #7。

**三段按 ABI 边界归位**：

| 段 | 内容 | 去处 | 判据 |
|---|---|---|---|
| 值模型 | `PropertyValue` 树（Null/Bool/Number/String/Array/Object）+ `IPrimitiveValue` 标记接口 + `PropertyType` | **Primitives**（冻结） | Foundation(live-doc 存值) + SDK(`ISynthesisNote.Properties`) + 序列化(`DataInfo`) 都用 → 过 §三.10 "双方共用" 准入 |
| 通用控件 Config | `IControllerConfig` + `Slider`/`CheckBox`/`ComboBox`/`TextBox`/`Object`Config | **SDK.Base**（冻结） | 仅 SDK 声明 + host/UI 消费，Foundation **不引用** → 不过 Primitives 准入；且为表现层增长面，隔离在 SDK.Base 受 DIM 治理 |
| 域专属 Config | `AutomationConfig`（name/min/max/color） | **SDK.Base**（#11 修订） | 原定 SDK.Voice（"voice 概念"）；#11 发现 effect 也声明 AutomationConfig → 实为"自动化轨通用配置"，连同 `IAutomationValueGetter` 一并搬 SDK.Base 单份共用（见 §三.19） |
| live-doc | `DataPropertyObject`/`DataPropertyValue`/`DataPropertyArray` 等 | **Foundation** | host-only undo/attach 基础设施；插件只见 `PropertyObject` 快照，永不拿 live 树 |
| controllers | `IValueController`/`IDataValueController` | **UI**（#2 已搬） | 值编辑 UI 绑定 |

**Config 命名按 UI 控件，不按值类型**：同一值类型可对应多种控件（int 可用 TextBox 也可用 Slider），不同控件的可配参数不同（TextBox 有 `IsSingleLine`、ComboBox 有 display-text、Slider 有 min/max）。控件命名诚实承载"用哪个控件 + 配什么"的作者意图，是 TuneLab 一贯暴露给作者的模型。采纳 effect 的 `SliderConfig`/`CheckBoxConfig`/`ComboBoxConfig`/`TextBoxConfig`，**否决** Base 的值类型命名（`NumberConfig`…）。对现存第三方插件是破坏性改名，由整代版本化 + compat 层（§三.2）吸收。

**值模型用单一 box，不做标量/树双 box**：effect 曾分裂 `PrimitiveValue`（标量）vs `PropertyValue`（树）各带 readonly 变体——分裂的收益面窄（仅 combo 选项 / config 默认值的编译期"只收标量"保证），成本面宽（冻结表面翻倍 + 标量在两 box 间永久 up-cast 噪音，违 §三.10 内核增长纪律）。**折中**：只保留单一 `PropertyValue` box + 轻量 `IPrimitiveValue` 标记接口（标记 `PropertyBoolean/Number/String`）；combo/config 入口用**具体类型重载**（`ComboBoxOption(bool/double/string)`）拿编译期保证，无需第二个 box。读写方向仍按 §三.11 边界二分保留 `IPropertyValue`/`IReadOnlyPropertyValue`（服务回只读、插件填可变）。

**值模型形状**（采纳 effect 结构，但 effect 代码是半成品，#7 落地时补齐）：JSON 树 + `PropertyType` 枚举 + `PropertyNull.Shared` 哨兵（替代旧 `Invalid`）。**必须补齐** effect 漏掉/注释掉的：① 值相等性（`property增加相等性判定` 意图——undo 去重 `IDataObject<T>.Set` 的 `Equals(before,info)` 依赖它，类型 + 深比较）；② `ToString`；③ 数组走 §三.11 冻结集合接口而非裸 `IList<>`；④ 命名清理（统一 `UnBox`，删 `PrimitiveValue`/`UnBoxing` 不一致与残留 `PropertyValue_V1Extensions`）。

**live-doc 内部形状按未来需求定，本话题只锁分区不锁内部**：`DataPropertyObject` 族归 Foundation 已锁；但 effect 里 `IDataPropertyObjectField`（类型化可绑定字段，UI 双向绑定的桥）、`MultipleDataPropertyObject`（多选编辑）、`PropertyPath`（嵌套寻址 vs `GetField` 导航）三项 effect **全是 `NotImplementedException`**，其去留与终态随 #11(effect 参数)/#12(属性面板) 的具体需求落地时定。**对 §三.11 #4 尾的呼应**：property 数组用 `DataList`（值快照、整列替换）还是 `DataObjectList`（逐元素 attach/undo）同属此 live-doc 内部形状，一并随需求定。

**物理落地（值模型搬 Primitives、Config 搬 SDK.Base、改名、补齐、规范化）随 #7**。

### 13. 条件表达式系统：功能保留，丢弃 effect 实现，落地随 #12（#6 确立）

**功能保留（明确在案）**："用户在某控件选/输入不同值 → 属性面板展示不同控件及对应字段"是**确认要做的功能**，不是被砍。#6 否决的只是 **effect 那份半成品响应式 DSL 现在就锁进冻结 ABI**，不是否决特性本身。

**它是什么**：`IExpression<out T>` = 响应式计算单元（`T Result` + `event ResultChanged`）+ 组合子（`And`/`Or`/`Not`/`Excute`/常量 `ToExpression`）+ 流式 `Expression.If(c).Return(x).ElseIf(c2).Return(y).Else(z)` 构建器。**意图用例**（引入 commit `b4c9b79` 同时加的 `ConditionConfig` + `IControllerConfig.When(...)` 揭示）：属性面板控件槽的子 config 随其他控件实时值反应式切换；作者声明式写条件，host 在 value-changed 时重算换控件。设计目标是"更易读易写的声明式 API"。

**三问结论**：
- **跨进程/跨语言序列化？** 否。持有活 `Func<>` 委托 + 事件订阅，纯进程内、不可序列化，无字符串/JSON DSL、无解释器，与跨语言无关。
- **能否 C# lambda 替代？** 能。host 本就监听控件值变化，一个普通 `Func<…,bool>` 可见性谓词按 value-changed 重算即达成同样 UX，ABI 面更小。
- **effect 实现状态？** **零消费者、半成品**：effect 全树 `When`/`Expression.If`/`ConditionConfig` 调用点为 0；较新的 `Core/IControllerConfig` 已退化成空接口（条件能力只剩在更早 `_V1` 版）；host 侧消费（面板读 `ConditionConfig.Config` + `InvokeValueChanged`）未接通；引擎本身有 bug（`ElseIf` 状态机、`+= ResultChanged` 把事件字段当 handler）。作者用例未提交进 effect。

**决策：丢弃 effect 的这份实现，当前不进任何层；功能随 #12（属性面板）连同 Config 家族一起设计落地**。理由：条件 config 叠在 Config 家族之上（§三.12 归 SDK.Base，随 #7/#12 落地），作者书写形状取决于面板/config 模型，必须和 #12 一起定；现在锁进冻结 ABI 违 §三.7 不提前建空壳 + §三.10 内核增长纪律；非数据值→不进 Primitives；effect 那份 `IExpression_V1` 正是 §三.1/§三.11 已否决的"重拷 `_V1`"。

**#12 落地时的取舍（倾向 B 内核 + A 糖）**：
- **A. 声明式响应式**（effect 路线）：config 树含 `ConditionConfig`/`IExpression` 节点，作者写 `If().ElseIf().Else()` 最顺，但要把整套组合子搬进 SDK.Base 冻结、且插件持事件订阅图易踩泄漏/重算 bug。
- **B. 声明式数据 + host 求值**（**倾向**）：字段带 `visibleWhen: Func<…,bool>` 或 `{选项→config}` 表，ABI 只多一个 `Func`/map，host 拥有实时值、求值时机可控，无响应式引擎。
- **折中**：走 B 的内核（小 ABI、host 求值），在 SDK.Base 上包一层薄 `If().ElseIf().Else()` 糖拿回 A 的可读性。**最小单份**、无 `_V1`、与 Config 家族同处 SDK.Base，**永不进 Primitives**。

**（#12 后方案演进 → §三.25）**：上述"B 内核 + 逐字段可见性谓词 + 薄糖"的设想，已被更统一的 **`ObjectConfig = f(context)` 整树重算**模型取代——动态性不是埋在 config 树里的节点，而是整棵 config 成为数据的纯函数。详见 §三.25。

### 14. SDK 分层 + 命名物理落地（#7 确立，已改代码）

承 §三.7 蓝图 + §三.10/§三.11/§三.12 归属，本话题首次**落地代码**：建 5 个程序集、按归属搬类型、改全仓引用，`dotnet build TuneLab.sln` Debug/Release 均 **0 错误**。范围裁剪（用户定）：**仅契约层**（内建实现留主程序，不建 singular `Extensions.*`）；**SDK.Effect 最小骨架**（接口形状留 #11）；plural `Extensions.Formats`/`Voices` **从 .sln 移除**（契约源码已搬入 SDK.\*，目录留盘供 #8 归档）；churn 用**逐文件 `using` 改写**吸收（split 命名空间处**加性**追加，不删旧 using）。

**已建程序集**（均 net8 ABI 地板、设置内联）：`TuneLab.Primitives`（零项目依赖，卫生卡口已验证）、`SDK.Base`→Primitives、`SDK.{Format,Voice,Effect}`→SDK.Base+Primitives；`Foundation` **新增 →Primitives 边**（§三.10 批准），主程序引 Primitives+4×SDK。

**已落地的归属/改名**：
- **Primitives.DataStructures**（冻结 map 家族，已规范到对称完整、避免二次改）：`Map`/`OrderedMap` + 只读/可变接口 + `Point`。改名 `IReadOnlyKeyWithValue`→`IReadOnlyKeyValuePair`、`KeyWithValue`→`ReadOnlyKeyValuePair`（并改为**不可变**以匹配 ReadOnly 语义）；**删** `IReadOnlyOrderedMapExtension.At` 自递归 bug；**补**可变 `IMap`/`IOrderedMap` 补全读/写对称，`IOrderedMap` 补 `RemoveAt(int)` 与 `Insert(int,…)` 对称；**Keys/Values 收紧**——`IReadOnlyMap`→`IReadOnlyCollection<>`、`IReadOnlyOrderedMap`→有序 `IReadOnlyList<>`（6 处实现同批改：`Map`/`OrderedMap`/`DataMap`/`DataObjectMap`/`DataPropertyObject`/`ReadOnlyMapWrapper`，Foundation 补对称 `ReadOnlyCollectionWrapper` 供惰性投影保 Count）；`Map` 枚举器改私有 `yield` 摆脱 Foundation `EnumeratorWrapper`（保 Primitives 零依赖）；`Map.Empty` 单例保留。**纪律**：凡进冻结 ABI 的同类 datastructure 一次规范到对称完整，杜绝冻结后二次修改（升代）。
- **Primitives.Property**：`PropertyValue`+`PropertyObject` 搬入并加性补齐——`PropertyType` 枚举、`PropertyNull.Shared` 哨兵、深相等性 + `GetHashCode` + `==`/`!=`（喂 undo 去重）、`ToString` 容 null；`.Round()`/`.ToEnum()` 内联以断 Foundation 依赖。
- **Primitives.Audio**：`MonoAudio`（最小冻结 struct）。
- **SDK.Base**：Config 家族按 UI 控件改名（`NumberConfig`→`SliderConfig`、`BooleanConfig`→`CheckBoxConfig`、`StringConfig`→`TextBoxConfig`、`EnumConfig`→`ComboBoxConfig`、`ObjectConfig` 留）+ 族根 `IPropertyConfig`→`IControllerConfig`（`IValueConfig`/`<T>` 留，删未用的 `IntegerConfig`/`ListConfig`）；服务接口 `ILog`（提炼自 Foundation `ILogger`，静态 `Log` 留 Foundation）+ `ITuneLabContext`（注入式，仅 `Log`）。
- **SDK.Format** = `DataInfo/*` + `IImportFormat`/`IExportFormat` + attrs；**SDK.Voice** = voice 契约 + `AutomationConfig`（基类改 `SliderConfig`）；**SDK.Effect** = 占位注释文件。
- Foundation 留 `DataPropertyObject`/`DataPropertyValue`/`PropertyPath` + Document/LinkedList/wrapper/`RangeF`（永不跨边界）。

**本话题刻意推迟项**（记录在案，均安全后补）：
- **PropertyValue 全树重构**（`PropertyBoolean/Number/String` 包装类型 + `IPrimitiveValue` 标记 + `PropertyArray`，§三.12 形状）**推迟到 #12**：当前**零消费者**（无数组、无代码读 `IPrimitiveValue`），按 §三.7 不提前建空壳；本话题已落地搬迁 + `PropertyType` + 空哨兵 + 深相等性 + `ToString`。
- `[CollectionBuilder]` 未给键值 map 加（无 `= []` 站点、无元素字面量语义），保留 `Empty` 单例。
- `PropertyValue.Invalid`/`IsInvalid()` 保留为指向 `PropertyNull.Shared` 的转发 shim。**（#12 后查漏补缺反转此项）**：`Invalid`（无值/无选中）正式化为长期哨兵、不再清理，并增补并列的 `PropertyValue.Multiple` 哨兵（多选冲突）——属性面板三态呈现与未来条件谓词共用，见 §三.23。
- `MonoAudio`/`ILog`/`ITuneLabContext` 为 directive 显式要求引入但**当前无消费者**（奠基 ABI 词汇，真实接通在 #8/#11）。

蓝图 §三.7 表中各程序集 TFM/角色不变，本节只记物理落地与裁剪/推迟决策。

### 15. 老插件兼容机制：命名分代 + ALC 加载模型 + Legacy 源码冻结（#8 确立）

纯决策，不改代码（`Compat.Legacy` 实际代码留 #9 范围 / #10 加载 / #11 effect 接口定下目标面后写）。effect 分支的 `ExtensionCompatibilityLayer` 作参考：**单程序集、无 ALC、无 extern alias**，靠新老程序集**名字不同**直接引用 + 深拷贝适配（Format 路径完整，Voice 路径多为 `NotImplementedException`——正因接口未定）。

**命名分代（Legacy / V1 / V2）**：
- **Legacy** = 现 master 那套（`TuneLab.Base` + `Extensions.Formats` + `Extensions.Voices`），野外已发布插件链接；改版前、无版本号、单一一代。
- **V1** = 改版后新插件系统的**第一**版（`Primitives` + `SDK.*`，#7 落地）；可维护性/升级规范从此科学，故 V1 从此起算。
- **V2** = 未来下一代。
- 兼容层命名 `Compat.<被桥接的那一代>`：Legacy 插件 → `Hosting.Compat.Legacy`；未来 V1 插件 → `Compat.V1`。修订 §三.2/§三.7（原 `Compat.V1` → `Compat.Legacy`）。

**extern alias 的真实触发点**：它只消歧**同名不同版**程序集。Legacy→V1 名字不同 → **无需 extern alias**，`Compat.Legacy` 直接引用 Legacy 与 V1 两套（同 effect 分支，亦同 §三.9 的 Base/Foundation 共存）。首次真正用 extern alias 是 **V1→V2**（`SDK.*.dll` v1/v2 撞名），届时 `Compat.V1` 用 `extern alias SdkV1`。§三.2 机制正确，但其语境是未来同名跨代，非当下。

**跨 .NET 版本兼容靠 TFM ABI 地板，不靠 ALC**：决定运行时版本的是 host，插件随 host 跑。host 升 net10 时**不重编 SDK**（`Primitives`/`SDK.*` 保持 net8 二进制原样），net8 插件经 roll-forward 在 net10 运行时直接跑、**无需重编**——这正是 §三.7"ABI 地板锁 net8、host TFM 独立升级"的兑现。唯一会破：改 SDK public surface（走整代版本化）或插件踩中被移除的 BCL API（罕见 → 升 .NET 必须独立 PR + 一轮老插件加载回归验证；呼应 §三.7"结构重构与 TFM 升级永远分开"）。

**ALC 加载模型：永远 per-plugin ALC + 共享契约 + 非 collectible 起步**：
- **永远 per-plugin ALC**（统一模型，消掉"判断走哪条路"维度）。论证：条件触发 vs 永远触发在最差情况等价，好情况下永远触发多付的税极低（见下表对应 1–4 项），故取统一。
- **共享契约硬约束**：`Primitives` + `SDK.*` + BCL 由 **Default ALC 加载一份、所有插件 ALC 共享**（`PluginLoadContext.Load()` 对契约程序集返回 null 落 Default、插件私有依赖走 `AssemblyDependencyResolver`），插件**私有依赖**才进各自 ALC。否则同名 Type 跨 ALC 不相等，连同版本插件都要 marshaling（footgun）。
- **起步非 collectible**：吃下隔离的全部好处——依赖版本冲突天然根除、多代同名 SDK 并存提前铺路——而**无泄漏风险、无 JIT 性能税**（这两条只在 collectible 才有）。代价仅：①加载器实现（官方插件范式 ~50 行）②契约清单（按 `TuneLab.SDK.`/`TuneLab.Primitives` 前缀自动判定）③每插件一 ALC 的微小开销 ④诊断（共享契约做对后跨边界类型都是 Default 同一 Type，问题集中在出问题的插件自身）——均低/可控。
- **collectible / 热卸载留 #10**，触发条件 = 卸载语义定为**运行时立即生效 / 释放 dll 文件锁**（配合 master 已有的安装/卸载 UI，§五）。届时同一个 `PluginLoadContext` 加 `isCollectible:true` 即可——**加载/契约结构零改动**。
- **升级不变量（使卸载成为加性补全而非返工）**：从 Legacy 兼容层落地起，跨边界引用就按 collectible 要求收敛管理——①事件桥接适配器 `IDisposable`、用完退订（见下"双向穿越"）；②插件实例只由加载器/registry **一处**持有，不散落进 host 长生命周期缓存。非 collectible 阶段这些纪律无害也无成本；切 collectible 时审查面缩到极小（仅补一道弱引用 + `GC.Collect` 卸载验证）。
- ALC 真正解决：**依赖版本冲突**（最实际）、**多代同名 SDK 并存**、（collectible 下）**热卸载/文件锁释放**。**不**减 adapter 代码（其量由契约形状差驱动）、**不**提供真正崩溃隔离（非进程边界）——纠正 §三.5。

**Capability Pattern 与 compat 正交**：Legacy 插件靠 attribute 发现（`[ImportFormat]`/`[VoiceEngine]`），无能力模型；compat 层**合成**能力面——把 Legacy 插件包成 `IPlugin`，`GetExtension("format.import/1.0")` 返回新类型适配器 over 老实例。capability 字符串方案 + `IPlugin` API 留 #10/#11，#8 只锁集成形态。

**双向数据穿越的所有权/生命周期**：
- **DTO/值快照**（`ProjectInfo`、`PropertyObject`）：**eager 深拷贝**、转移所有权、无别名。冷路径。
- **热缓冲**（`float[]` 音频、`Point[]` 曲线）：**按引用共享 / `MemoryMarshal.Cast` 零拷贝**（Legacy/V1 `Point` 布局相同），handoff 后视为不可变。
- **`SynthesizedPhonemes` 以 `ISynthesisNote` 为键**：note 包装必须**身份保持 + 缓存**（一 host note 一包装、双向查找），使插件返回的键映射回原 host note。
- **live document 永不跨界**：插件只见快照（合 §三.12），无 undo/attach 生命周期纠缠。
- **事件桥接适配器**（`ISynthesisTask` Complete/Progress/Error）`IDisposable`、dispose 退订——effect 分支漏了此点（泄漏，且正是 collectible 卸载的钉子）。

**性能基准（net8 Release，实测当前 `Primitives`/`Base` 类型，关闭 §三.6 开放点）**：

| 路径 | ns/op | B/op |
|---|---|---|
| `Properties[key]` 读已转换快照 | 37.4 | **0** |
| old→new `PropertyObject` 深拷贝（6 混合键） | 667.6 | 1024 |
| 经 `IReadOnlyMap` 接口枚举 `Map(8)`（class `ReadOnlyKeyValuePair` + 迭代器） | 389.7 | 352 |
| `PropertyValue` 装/拆箱往返 | 16.8 | 24 |

结论：所有 wrapper/装箱分配落在**冷设置路径**（每 note/part 边界转换一次，如 500 note 的 part 约 0.33 ms / 0.5 MB，对数百 ms 起的合成可忽略）；`Properties[key]` 在 eager 转换下**零分配（37 ns）**。决定性规则：**边界 eager 转换、绝不给插件 lazy 逐访问实时转换 wrapper**；批量数组 API（`float[]`/`Point[]`）把 per-sample 工作挡在 wrapper 世界外。

**本话题范围**：纯决策，写 §三.15；实际代码留后续写代码话题（#9/#10/#11）。一并修订 §三.2（命名 + extern alias 触发点）、§三.3（留源码冻结）、§三.5（ALC 已决 + 纠正）、§三.6（基准已测）、§三.7（表 `Compat.Legacy`）、§三.10（命名 + 归档形态）。

---

### 16. 老插件兼容范围：全 1.x 尽力而为 + 钉 1.0.0.0 + format/voice 并行 + 长期维护（#9 确立）

纯决策，不改代码。承 §三.15 留给 #9 的三个接口（兼容范围 / `AssemblyVersion` 钉值 / collectible 触发条件），逐一收口。

**野外插件清单与版本范围**：
- Legacy 三程序集（`TuneLab.Base` + `Extensions.Formats` + `Extensions.Voices`）**从未设过 `<Version>`/`AssemblyVersion`**（全仓仅一个 `Directory.Build.props` 且只含 `Nullable`）→ 野外插件链接的绑定身份一律 `Version=1.0.0.0, Culture=neutral, PublicKeyToken=null`（**未签名**）。发布跨度 **v1.0.0 → v1.6.0**（18 个 tag），AssemblyVersion 全程 1.0.0.0。
- 跨版本公共面变动是**纯加性 DTO 增长**（`ProjectInfo`+`EditorInfo`/`ExportConfig`、`TrackInfo`+`AsRefer`/`Color`/`ExportEnabled`/`ExportChannels`、`NoteInfo`+`Pronunciation`、`SynthesisSegment<T>`+`PartProperties`；唯一反向是 `NoteInfo.Lyric` 由 `required` 放宽为可选，二进制层无破坏）→ **冻结 master/v1.6.0 这一个最新超集**即满足全部 1.x 期间编译的插件（老插件只读写自己认识的字段，新字段取默认）。
- 内置格式（ACEP/Midi/TLP/UFData/VPR）是**主程序内建、走新 SDK、不经 compat**；compat 只服务外部 `.tlx` 包——**voice 引擎**（DiffSinger 类，捆绑 ONNX 等原生依赖）+ 社区 **format** 插件。
- **兼容目标 = 全 1.x 社区插件尽力而为**（用户定）：不锁权威目标集，加载器对任何链接 1.0.0.0 的插件都尝试，失败**优雅降级（不崩主程序）**。权威清单留 #10 加载流程落地时按真机实测补全。

**`AssemblyVersion` 钉值（钉进 `Compat.Legacy` 引用的冻结源）**：
- 三程序集**全钉 `AssemblyVersion=1.0.0.0`**（= 野外绑定身份，绑定键不可动；`FileVersion`/`InformationalVersion` 可另标 `frozen-legacy` 便于诊断）。
- **冻结源取自 `master`**（`using TuneLab.Base.Structures`），**不是** `feat/effect-migration` 磁盘上的同名副本——后者已被 #2 Foundation 改名污染（`using TuneLab.Foundation.*`），**非纯净 Legacy**。落地：把 master 的 `Base`/`Extensions.Formats`/`Extensions.Voices` 源码搬入 `legacy/sdk/src/`（§三.3 隔离冻结），csproj 钉死 `AssemblyVersion 1.0.0.0` + `net8.0` + 禁改标注。

**format / voice / effect 优先级：format 与 voice 并行（effect 无老插件）**：
- effect 是新概念、**无老插件**，compat 不含 effect 路径。
- 用户定 **format 与 voice 并行**推进。**依赖张力（如实记录，不掩盖）**：Format 路径可**立即完整落地**（effect 参考 `FormatConverter` 224 行已全实现，目标接口 `SDK.Format` 在 #7 已冻结）；**Voice 适配器的目标面依赖 #11** 定下的新 `IVoiceEngine`/`ISynthesis*` 形状（effect 参考 voice 路径多为 `NotImplementedException` 正因此）。故"并行"的可落地含义 = Legacy 侧源码冻结 + 适配器骨架与 Format **同步起步**，但 Voice 适配器**填实**被 #11 接口冻结**门控**；#11 一旦定面即补齐，按 §三.15 加性不变量不返工。

> **#10 修订（松绑 #11 门控）**：上述"Voice 适配器被 #11 门控"的前提**不成立**——它误用了 effect 分支那套半成品 voice SDK（满是 `NotImplementedException`），而 **#7 取的是 master 的完整 voice 接口**并作为 ABI 地板冻结。经核验，现有 `SDK.Voice`（`IVoiceEngine`/`IVoiceSource`/`ISynthesisData`/`ISynthesisNote`/`ISynthesisTask`/`SynthesisResult`）**完整无占位**，且 voice 与 effect 是独立域（#11 做 effect 链/dirty/渲染管线接入，不改 voice 插件契约签名）。故**敲定：Voice 适配器以现有 `SDK.Voice` 为稳定适配目标，与 Format 真正并行落地，不再门控于 #11**；即便将来 voice 接口加成员，§三.15 加性不变量保证适配器不返工。

**collectible / 热卸载触发条件（呼应 §三.15 留给 #9 的判定）**：
- 野外 voice 引擎**确实捆绑冲突第三方依赖**（各自 ONNX/原生运行时版本）→ 坐实 **per-plugin ALC 是必要而非可选优化**。但依赖冲突由**非 collectible 的 per-plugin ALC 已根除**，**不构成 collectible 触发**。
- collectible 唯一新增能力 = 热卸载（免重启卸载/更新）+ 释放 dll 文件锁。**触发条件 = 产品要"免重启卸载/更新"的 UX**，非依赖冲突。当前 master 卸载走 `ExtensionManager.LaunchPendingUninstalls` → 独立进程 `ExtensionInstaller.exe`（**重启式**），是可接受 fallback → collectible **非紧急，维持留 #10**。§三.15 升级不变量（事件适配器 `IDisposable`、插件实例单点持有）从 compat 落地起即遵守，使 #10 切 collectible 为**加性补全**。

**长期维护成本 vs deprecation：不设时间表，长期维护（用户定）**：
- 成本结构低：Legacy 源码**隔离冻结**（独立 sln、钉版本/TFM、禁改）→ 不随主程序演进、零持续改动；compat 适配器仅在边界深拷贝，代码量由契约形状差驱动（§三.15 性能表证明落冷路径可忽略）。
- **不设 deprecation 时间表**，Legacy compat 长期保留，野外插件作者**无强制迁移压力**。未来 V1→V2 跨代（首次需 `extern alias`，§三.2）时若需重评 Legacy sunset 再议，当前不锁。

**本话题范围**：纯决策，写 §三.16；实际代码留实现话题——Legacy 源码冻结搬迁（取自 master）+ Format 适配器可随即起，Voice 适配器随 #11。收口 §三.15 留给 #9 的三个接口（范围 / 版本 / collectible 触发）。

---

### 17. description.json & 扩展加载机制（#10 确立，已改代码）

承 §三.15（ALC 模型）+ §三.16（兼容范围）。本话题首次落地**新版加载器**，Debug/Release **0 错误**。

**代际判定 = 看 `id` 字段有无**（讨论中从 `sdk-version` 改定）：含 `id` ⇒ V1；无 `id`（老 schema 或无 description.json）⇒ Legacy。理由：`id` 通用于一切 V1 包（含**无代码的资源包**），而 `sdk-version` 只代码包才有，覆盖不全；资源包正是推翻 `sdk-version` 判别符的连锁反应。`id` 是**逻辑标识**（dedup/update/registry），**物理主键仍是文件夹**（安装/卸载/`PendingUninstalls` 不变），故 Legacy 无 id 也**无需造假 id**；运行时注册键（format 扩展名 / voice 引擎 type）来自 attribute、从来不是 package id。

**插件单位 = 文件夹（包）= ALC = 安装/卸载原子单位**；一包可含多插件（`extensions[]`，保留每插件独立元数据），因 folder=ALC 边界，多入口程序集装进同一 ALC、共享基建只分发/加载一份——「打一包免重复分发基建」诉求几乎零成本兑现。单插件可省 `extensions[]`，顶层字段经 `EffectiveExtensions` 归一化（复杂度收进解析一处，管线后续只见 `IReadOnlyList<ExtensionInfo>`）。

**字段职责**：`type` 必填（WHAT：派哪个 manager / 找哪种 attribute）；`assemblies` 选填（WHERE：性能提示，写了只扫这几个、没写代码插件扫全部 dll、资源包不写）；`sdk-version` 含代码包必填（ABI 地板兼容门，宿主 `SdkVersion=1.0`，要求 > 宿主则跳过）；`platforms` 插件级；包级公共 `id`/`name`/`version`/`author`/`description`/`sdk-version`。

**新增/改动代码**（均 `TuneLab/Extensions/`）：`ExtensionInfo`（插件级）+ `ExtensionDescription : ExtensionInfo`（包级，加 `id`/`sdk-version`/`extensions`/`IsV1`/`EffectiveExtensions`）；`PluginLoadContext : AssemblyLoadContext`（per-folder ALC，契约 `TuneLab.Primitives`/`TuneLab.SDK.*` 返 null 落 Default 共享、私有依赖走 `AssemblyDependencyResolver`+目录探测+`LoadUnmanagedDll`，`isCollectible` 默认 false 预留）；`ExtensionLoadResult`（结构化结果 + 代际/状态枚举）；`ExtensionManager` 重写为统一管线（发现→读 manifest 判代际→校验→V1 per-folder ALC 选择性加载→扫 attribute 注册 / Legacy 走 `LegacyLoadHook` 委托或保留盲扫 fallback），失败优雅降级不崩主程序。`FormatsManager`/`VoicesManager` 去掉各自重复的 description 解析，改暴露 `RegisterFromTypes`（接收已加载类型）。sidebar 改读 `ExtensionManager.LoadResults`，**删除** `DetectExtensionType` 字符串猜测。

**裁剪/推迟**（在案）：**collectible 热卸载维持留后续**（#8/#9 决定非紧急，加载器已 collectible-ready，切换只需 `isCollectible:true`，卸载继续走 `ExtensionInstaller.exe` 重启式）；**effect 类型识别但暂不支持**（接口形状待 #11，优雅降级记日志）；**Compat.Legacy 实装留 #9 尾**（`LegacyLoadHook` 委托是接入点，当前未设 → 走保留的盲扫 fallback，真实 Legacy 插件优雅失败）；`ITuneLabContext`/`ILog` 注入待接口支持（managers 仍无参构造，#11 接通）。

**配套产出**：开发者指南 [plugin-development.md](plugin-development.md) + 面向 AI 的参考 [plugin-development-llm.md](plugin-development-llm.md)（提示语 + 事实清单 + 最小示例）。

---

### 18. Compat.Legacy 实装（#9 尾落地，已改代码）

承 §三.15（ALC 模型 / 双向穿越）+ §三.16（兼容范围 / 版本钉值）。本话题把 Compat.Legacy 从决策落成代码：Format + Voice **真正并行全实装**（采纳 §三.16 的 #10 修订——Voice 以现有完整 `SDK.Voice` 为稳定目标，不门控 #11）。`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Debug/Release）均 **0 错误**，编译期隔离经探针验证。

**Legacy 源码冻结落位**：`TuneLab.Base` / `Extensions.Formats` / `Extensions.Voices` 由根目录 `git mv` 进 `legacy/sdk/src/`（保留 history），三 csproj **内联钉** `AssemblyVersion=1.0.0.0`（绑定身份命门）+ `FileVersion`/`InformationalVersion` 标 `frozen-legacy` + 冻结禁改文件头；`legacy/Directory.Build.props`（空）截断向上继承主仓 props（护栏#2）；`TuneLab.Base` 从 `TuneLab.sln` 移除。自成 `legacy/Legacy.slnx`（含三冻结 + Compat，护栏#1）。

**引用策略：反射加载、零编译依赖（用户定，强于文档原设想）**。主程序对 Compat.Legacy 与 legacy SDK **无任何 ProjectReference**——编译期连 Compat 的公开 API 都看不到（探针验证：主程序 `using TuneLab.Base` 报 CS0234）。接入三段解耦：
- ExtensionManager 经 `LegacyLoadHook`（`Func`，运行时设）委托，对 Compat 零编译认知。
- 启动时 `LegacyCompatLoader.Wire()` 反射 `Assembly.LoadFrom` 加载 `TuneLab.Hosting.Compat.Legacy.dll`，取 `LegacyCompatEntry.TryLoad`，注入**注册委托**装上 hook。委托参数全是共享契约类型（`IImportFormat`/`IExportFormat`/`IVoiceEngine`，跨 Default ALC 同一 Type），反射 Invoke 实参精确匹配。
- 注册反转：Compat 够不到 `internal` managers，故 host 实现注册委托（转发 `FormatsManager.RegisterImporter`/`RegisterExporter`、`VoicesManager.RegisterEngine`）传入；Compat 往里"推"已包成 V1 适配器的老插件。
- 部署：`TuneLab.csproj` 加 MSBuild target（非 ProjectReference）构建 Compat + 拷其产物（Compat.dll + 三冻结 dll）进输出；V1 SDK 引用 `Private=false` 不重复拷。

**适配落地（§三.15 双向穿越纪律）**：
- **per-plugin ALC**（`LegacyPluginLoadContext`）：共享契约 = 三冻结 + Primitives + SDK.\*（返 null 落 Default，跨边界 Type 同一标识），插件私有依赖（ONNX 等托管/原生）进各自 ALC + 目录探测；非 collectible 起步（`isCollectible` 预留）。
- **Format**：`FormatConverter` 全字段双向深拷贝（含 effect 参考遗漏的 `EditorInfo`/`ExportConfig`；V1 无 Effects 更简单），`ImportFormatAdapter`/`ExportFormatAdapter` 包老 `IImportFormat`/`IExportFormat`。
- **Voice**：`VoiceEngineAdapter`/`VoiceSourceAdapter`/`SynthesisDataAdapter`/`SynthesisTaskAdapter` —— 两代接口同形状（均源自 master），转换=命名空间桥接 + Property/Config/Point 边界转换。**note 身份保持缓存**（`NoteWrapperCache`，一 host note 一包装）使 `SynthesizedPhonemes` 键映射回宿主 note；**`SynthesisTaskAdapter : IDisposable`** dispose 退订事件桥（补 effect 分支泄漏点），宿主 `SynthesisPiece.Dispose` 经 `(mTask as IDisposable)?.Dispose()` 触发（落实 §三.15 升级不变量）；audio `float[]` 同类型共享引用（零拷贝）。
- **`Segment<T>` 泛型保留、适配器零强制转换**：包装时记输入下标，引擎分组后用下标从输入列表取回真实 `T`（泛型实参即 `LegacyNoteAdapter`，无需 cast）。SDK 的 `Segment` 形状重设计另案讨论，本话题不动冻结契约。
- **Property/Config 转换**：`PropertyConvert`（Base 多 box ↔ Primitives 单 box+`PropertyType`）、`ControllerConfigConvert`（`NumberConfig→SliderConfig` 等 1:1，`AutomationConfig` 先于 `NumberConfig` 匹配）、`PointConvert`（布局同，曲线点冷路径拷贝）。全程优雅降级：单 dll/类型失败不影响其余、不崩主程序。

**多版本共存的前瞻收口（修订 §三.2"按代分"的细节，落地留 V2）**：兼容层**直桥当前代**（`Compat.Legacy: Legacy→当前`、未来 `Compat.V1: V1→当前`），**不链式**——Voice 适配器是活 wrapper、链式会引入 per-access 多层间接（违 §三.15 eager 转换铁律），而接口跨代非全变、直桥重写量小（复用未变部分转换器 + 一轮老插件回归）。加载器统一参数化（仅"共享契约判定"按代变），**仅适配器按代写一份**；宿主保持单条发现管线 + 代→Compat 注册表（今天的单 `LegacyLoadHook` 是退化特例）。

**裁剪/推迟**：collectible 热卸载维持留后续（ALC 已 ready，`isCollectible:true` 即切）；权威野外插件清单留真机实测补（§三.16）；native dll 子目录探测（如 `runtimes/<rid>/native`）仅做顶层探测，复杂布局后补。

#### 测试与硬化收尾（本话题闭环）

产出**测试插件套件** `tests/`（13 包 + 用例文档 + .tlx 打包脚本 + 可导入样例文件）：每类型 × 新老接口、一包多插件、ALC 私有依赖版本隔离、manifest 各变体（sdk-version/platform/资源包/effect 跳过/坏 manifest/省略 assemblies）、legacy 一包多 attribute。详见 [tests/PLUGIN-TEST-CASES.md](../tests/PLUGIN-TEST-CASES.md)。**全部用例通过**，并经真实野外插件（ChoristaUtau、InstrumentsForTuneLab、LibreSVIPConverter 等）端到端验证。

真机测试暴露并修复 7 处（host + compat）：
1. **加载顺序竞态（关键）**——主程序对冻结契约无 ProjectReference（不在 Default ALC 的 TPA），契约仅在 Compat 首次触碰其类型时载入共享上下文；故**第一个**被处理的真实 legacy 插件 `GetTypes` 解析契约失败 → 整包 Skipped。修复：`LegacyCompatEntry.TryLoad` 开头 `WarmUpContract()` 预热三个冻结契约程序集，先于任何插件 GetTypes 载入。
2. **批量安装坏 manifest 崩溃**——`InstallExtensions` 解析 description 在 try 外、`async void` 未捕获异常崩进程；修复为容错解析 + dispose zip + 一次性汇总。
3. **Compat 诊断日志**——此前失败全静默吞；增 `Action<string> log` 注入，逐环节记录（程序集加载失败 / `ReflectionTypeLoadException` LoaderExceptions / 缺无参构造 / 类型身份不匹配 / 注册成功），写入宿主日志（`[Compat.Legacy]` 前缀）。
4. **侧边栏状态可见**——Failed/Skipped/Partial 彩色徽标 + Error 作 tooltip；安装汇总据真实加载状态区分成功/失败。
5. **Skipped 原因补全**——平台不匹配 / effect 跳过此前未写 `Error`，补齐供 tooltip。
6. **待卸载可撤销**——"稍后"标记后点击弹「取消卸载」防误点。
7. **重装对话框列名**——批量安装"检测到已安装"由单数文案改为列出数量 + 包名。

**Voice 保留名教训**：插件自动化参数名须避开宿主内置保留名（`Volume`/`VibratoEnvelope`），否则被内置项占用而不显示——已写入 voice 开发文档。

#### 扩展侧边栏信息增强（接 §三.18 收尾）

承上文"侧边栏状态可见"，给卡片补图标/作者/类别等信息。**卡片布局**（与左侧 64px 图标等高，不被简介行撑高）：第 1 行名称（左，过长省略号）+ 版本徽标（右）；第 2 行作者（中部，带人像图标）；第 3 行类别 +（非 Loaded 时）状态徽标（左）+ 卸载按钮（右）。**简介不上卡片**——鼠标悬在整张卡片上由 tooltip 给出完整信息（全名 + 版本 + 作者 + 简介），既保证信息完整又控制卡片高度。
- **图标**：`description.json` 新增 `icon`（包内相对路径），位图（`.png`/`.jpg`…）与矢量（`.svg`）都支持——位图走 `Bitmap` 解码、`.svg` 走 `Avalonia.Svg.Skia` 的 `SvgImage`，按扩展名分流，`Stretch=Uniform` 完整显示。**带图标的包不画任何打底背景/圆角、原样摆放图标**（VSCode 同款——圆角/透明交给作者，宿主不叠加遮罩，否则与容器圆角双重叠加不协调）；**无图标**才退回深色圆角方块 + 名称首字母占位。解码失败也退回占位。
- **作者 / 简介**：`author` 在卡片中部展示（过长省略号）；`description` 不占卡片行、进整卡 hover tooltip。**否决**卡片内简介行（四行撑高、难看）与点开式详情面板（tooltip 已够、不值新组件）。
- **Legacy 真实类型**：Legacy 包此前笼统显示 "Legacy"，现由兼容层回填真实类别——`LegacyLoadHook` 加第三参 `ICollection<string>` 类型 sink，注册委托按包重建并在 `addImporter`/`addExporter`/`addVoiceEngine` 成功时写入 `format`/`voice`，sidebar 据 `ExtensionLoadResult.Types` 展示精确类型。
- **状态优先**：底行左侧徽标——`Loaded` 显示类别（**每种 type 各一枚徽标**，不拼成一枚逗号串）；`Skipped`/`Failed` 不渲染类别（加载失败的包无"生效类别"可言）、只留彩色状态徽标 + 原因 tooltip；`PartiallyLoaded` 则类别 + Partial 并排。
- **作者贴近底行**：作者行与底行徽标编为一组锚在卡片底部，留白落在大字名称与小字之间，让作者（小字）视觉上靠近第三行徽标（小字）而非名称（大字）。

数据经 `ExtensionLoadResult`（新增 `Author`/`Description`/`IconPath`）从加载管线一路流到 sidebar，sidebar 不二次解析 manifest。`icon` 字段已写入插件开发文档。验证用例独立成 [tests/SIDEBAR-INFO-TEST-CASES.md](../tests/SIDEBAR-INFO-TEST-CASES.md)（只测本次改动影响的展示范围，不重跑既有 format/voice 功能用例）。

> 注：迁移期临时术语（话题#N、§章节号、内部代号）不得出现在永久代码/文档（注释、`plugin-development*.md`、测试用例文档），已全仓清理；本迁移文档为临时档案、不受此限。

---

### 19. Effect 新功能：离线整段音频变换 + per-piece 串行链（#11 确立，已改代码）

承 §三.7 蓝图（SDK.Effect 占位）+ §三.10/§三.12（SDK 分层）+ §三.15（双向穿越）。本话题把 effect 从占位落成完整功能，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**；零 effect 的既有工程行为不变（FinalizeChain 直接取 voice 输出）。

**定位（先讨论达成共识）**：effect 面向**耗时长的离线模型**（如 SVC 换声），**不是实时 VST**（reverb/EQ 等留后续）。决定了任务式异步 + 整段 `MonoAudio` 进出的形状。

**SDK.Effect 形状（最小冻结面）**：`IEffectEngine`（`PropertyConfig: ObjectConfig` + `AutomationConfigs` + `Init(enginePath, out error)` + `Destroy` + `CreateSynthesisTask(input, output)`）；`IEffectSynthesisInput`（`MonoAudio Audio` + `PropertyObject Properties` + `TryGetAutomation`）；`IEffectSynthesisOutput`（`MonoAudio Audio` sink，**塌缩**——不再 `: ISynthesisOutput`）；`IEffectSynthesisTask`（`Complete`/`Progress`/`Error` + `Start`/`Stop`，一次性处理器）；`[EffectEngine("type")]`。**dirty/缓存全归宿主编排器，SDK 不含 DirtyEvent**（比 effect 分支更小）；输出仅音频（pitch/automation 回写推迟）。

**共享词汇抽到 SDK.Base（修订 §三.12）**：`IAutomationValueGetter` + `AutomationConfig` 从 SDK.Voice 搬到 **SDK.Base**（voice/effect 共用，合"定义一份"纪律；V1 无野外插件、ABI 零破坏，纯内部 churn——含 host UI + Compat.Legacy 命名空间改写）。`ISynthesisNote`/`SynthesizedPhoneme` 仍留 SDK.Voice。

**数据模型（纯加性）**：`EffectInfo`（Type/IsEnabled/Automations/Properties）→ SDK.Format.DataInfo；`MidiPartInfo.Effects: List<EffectInfo>`（master 本无此字段）；host `IEffect`/`Effect`（挂 MidiPart，`Type` 不可变、`IsEnabled`=bypass、`Properties`=DataPropertyObject、`Automations` 复用现有 `Automation` 类）；`mEffects: DataObjectList<IEffect>` 有序链 + Insert/Remove/IndexOf。config 取自 `EffectManager.GetInitedEngine(Type)`，引擎缺失→空配置 + passthrough。

**EffectManager**（镜像 VoicesManager）：`[EffectEngine]` attribute 发现、惰性 Init、`RegisterFromTypes`；接入 `ExtensionManager`（删除"effect 暂不支持"跳过，加 LoadBuiltIn/Destroy/RegisterByKind 的 effect 分支）。

**合成接入（SynthesisPiece 内编排，不动 voice/AudioGraph）**：voice `Complete` 后把 `float[]` 包成 `MonoAudio`，**按 piece（voice 分片）独立**串行过链——每级 `CreateSynthesisTask` 异步处理，链尾输出 = piece 最终音频。**per-piece 是有意的 context 缩减**（片间连续性由 voice 分片函数负责，非 effect 跨段拼接）。**每级输出缓存 + 仅下游失效**：effect[i] 参数/启用/自动化变化→从 i 级重跑（上游缓存复用，避免重跑昂贵 SVC）；链结构变化→从 0；voice 脏→整链。bypass / 引擎缺失 / 出错 = 该级 passthrough，优雅降级。generation 计数 + 同步上下文 post 防异步竞态。

**UI（最小）**：属性侧栏新增 Effects 面板（`EffectsController`）——链列表 + 增/删/上下移/bypass + 「Add Effect」菜单列引擎类型；参数面板**复用现有 `ObjectController`**（按 effect 的 `PropertyConfig` 渲染、`Properties` 双向）。**per-effect 时间轴自动化编辑 UI 推迟**（数据模型/接口已支持，曲线编辑后补）；preset 与属性面板重设计随后续。

配套：[plugin-development.md](plugin-development.md) 加 §6 Effect 章节 + [plugin-development-llm.md](plugin-development-llm.md) 加 Effect 接口清单。

---

### 20. 新 Property 面板 UI + effect 自动化接入（#12 确立，已改代码）

承 §三.12（Property 三段归位 / live-doc 终态留 #12）+ §三.13（条件表达式落地随 #12）+ §三.14（PropertyValue 全树重构推迟 #12）+ §三.19（#11 推迟的 per-effect 自动化曲线 / effect 参数面板整合 / preset）。本话题分两提交落地，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**。先做设计讨论达成共识再动代码。

**提交① 面板架构 = 数据绑定驱动（live-bind），退役 path-routing**：
- **痛点（旧面板）**：面板↔数据靠 `PropertyPath` 下发值 + 事件上抛 + 外部手工写回/多选哨兵，胶水重且每调用点重写；控件类型 if-else 硬编码不可扩展；多选只服务 Note 且逻辑内联；面板内是 path-routing、面板外是 `.Bind()`，两套并存。
- **新形态**：`IDataPropertyObject`（`DataRoot` + 按 `PropertyPath.Key` 取/写值）+ 字段适配器（`NumberField/StringField/BoolField`），`DataPropertyObject` 实现之；控件逐字段 `BindDataProperty`，复用单属性的撤销/刷新/提交机器（与 Settings / 固定属性同底）。`PropertyObjectController` 用控件注册表，嵌套对象用 `PropertyPath` 寻址打到同一数据源（**不需合成嵌套对象**，绕开 effect 半成品里那些 `NotImplementedException`）。voice 的 note/part 属性、effect 参数面板**自动并入同一机制**，旧 `ObjectController` 整删。**（#12 后查漏补缺 §三.24 反转此项：落地 effect 原始意图——干掉 `PropertyPath`、`Object(key)` 逐层导航 + 多选复合递归，`DataRoot` 折进节点本身。）**
- **多选**：`MultipleDataPropertyObject` 合一（读全等→该值、否则 Invalid 显"多值"；写扇出）。**撤销根 `DataRoot` 的 Head/Commit/DiscardTo 委托首对象**（同文档共享一个 Head，一次提交归一个撤销单元——委托给谁等价，#12 确认）；但 **Modified 必须合并所有对象**——只听单对象会在它先被写、其余未写时算出 Invalid 卡住，合并后"最后一次刷新"看到全部写完的最终值（与单 note 表现一致）。
- **自动化默认值编辑保留专用控件**（`AutomationDefaultsController`）：merge-dirty 避免拖动重合成 + 按需 `AddAutomation` 这两条语义不适配通用桥，**功能完整性优先于 bind 语法便利**（用户定）。

**提交② effect 自动化接入参数面板 / 侧栏（与 voice 统一）**：
- **`AutomationKey`（来源 + plain id）做 UI→数据路由**，**否决伪造字符串前缀**（voice 轨真名可能撞前缀；用户定用类型）；数据仍按 plain id 存各自容器、key 不持久化。`IMidiPart` 加按 `AutomationKey` 路由的扩展（分派 voice / 对应 effect），`MidiPart` 不动、string 版留给内部 voice 调用。
- **统一交互**（用户定）：voice 与各 effect 自动化平等汇入底部参数选择栏与右侧栏默认值——**点亮小眼睛→叠加绘制，toggle 开→编辑**；voice 一组、每个 effect 一组、组间分隔符（右侧栏 effect 组带名字表头）。
- **effect 无颤音**：active 为 effect 轨时跳过颤音叠加层与关联操作（否则 voice-only 的 string 取值路径会拿 effect id 查 voice 配置而抛异常）。颤音影响 effect 参数（`Vibrato.AffectedAutomations` 可扩展）太复杂，先不做。
- 渲染器补订阅各 `effect.Automations` 改动（否则拖动 effect 自动化不重绘——voice 只听 `part.Automations.Modified`）。

**推迟项（均已论证安全后补）**：
- **条件表达式系统（§三.13）继续推迟**：已论证**未来纯增量可补**——配置类加可选 `Func<PropertyObject,bool>` 谓词（具体类加字段始终加性，或族根 `IControllerConfig` 走 DIM），host 侧求值，糖是扩展方法，**不需新 Primitives 类型**；live-bind 把每字段做成可观察 `IDataProperty`，反为它铺路。
- **PropertyValue 全树重构（§三.14）继续推迟**：ComboBox 维持 string-only、桥只用单一 `PropertyValue` box（`ToNumber/ToBoolean/ToString` 足够），`IPrimitiveValue`/`PropertyBoolean/Number/String`/`PropertyArray` 仍零消费者。
- **dataobject 集合接口 / `IDataObject` DIM 重构**（effect 那套 `IReadOnlyDataCollection` 统一）**不纳入**：与属性面板正交、是 Foundation 内部自由演进层、推迟零 ABI 风险；桥建在 master 现有 `DataPropertyObject`/`IDataProperty`/`.Any` 上。
- **effect preset** 未做（Part preset 面板保留不动）。

---

### 21. DataObject / 事件模型重构：纯契约接口 + 哑聚合复合 + 双重载事件（#12 后查漏补缺）

承 §三.20 推迟项"dataobject 集合接口 / `IDataObject` DIM 重构不纳入"。编号话题 #1–#12 已完成，本节是完成后的**查漏补缺**（非新编号话题），对已落地的 `IDataObject` 设计做一次结构修订。先讨论达成共识、讨论即落地，不留坑。

**两个痛点（动机）**：
- `IDataObject` 用"DIM 有状态 mixin"：protected 接口属性占位状态 + 私有 DIM 写算法 + 嵌套 `Implementation` 提供唯一存储。后果是 `Implementation` 里满是 `((IDataObject)this).Parent` 的回转 cast、`Wrapper` 要显式转发整排 protected 状态成员，且这套灵活性（任意 `IDataObject` 实现）实际没人用（单一状态载体、单继承）。
- 复合节点向子扇出值靠 `protected static` 泛型 `IDataObject<T>.SetInfo`（跨封闭泛型访问墙的唯一桥），调用点写成 `IDataObject<NoteInfo>.SetInfo(Pos, info.Pos)`——那个无关的封闭泛型前缀纯为给静态方法点名，**全仓 124 处 / 34 文件**。

**设计参考（另一仓库的 C++ `DataObject` 新实现）**：那套的核心是 ①撤销根 = `DataDocument`，`push` 向上找 document，无 document 即"应用但不记录"；②复合节点是**哑聚合器**——直接继承非泛型 `DataObject`，`info()/setInfo()` 是普通方法、读写子；命令只住在叶子（`DataProperty<T>`）与容器（`DataList`/`DataHash`），复合自己无命令、无 info 类型；③复合编辑原子性靠 merge-notify 批处理而非"复合级命令"。**引进其理念，不引进 Qt 机制**（signal/slot 用 C# event 替代，per-object `QMutex`/线程亲和不要——数据类跑主线程，有问题再手处理）。

**关键认识**：C# 侧 `DataDocument : DataObject` 已 override `Push`（记录未提交）/`Commit`/`Undo`/`Head`，**早已是 document-rooted**；`Parent?.Push` 上爬到它，无 document 祖先即爬到 null 丢弃命令。故本次是**利用现有撤销根**，不新建。

#### 共识 A：事件模型

- **`IModifiedEvent`（单事件、双订阅形状）**：无参 `Subscribe(Action)` = 只在结果态触发（`canIgnore==false`，与现有 `Modified.Subscribe(()=>…)` 逐字节兼容、零迁移）；带参 `Subscribe(Action<bool>)` = 每次都触发、arg 即 `canIgnore`（订一次拿全信息）。**接口只继承 `IActionEvent`（单一 `IEvent<Action>`），bool 形状以直接方法声明而非继承 `IActionEvent<bool>`**——否则实现两个 `IEvent<>` 会令 `When`/`Any` 的 `IEvent<TEvent>` 推断歧义，且 `When`/`Any` 消费者订阅的本就是无参 `Action`。实现 `ModifiedEvent : ActionEvent` **复用 settled 通道**（无重复多播逻辑、无额外对象分配），只增量加 bool 通道；`Invoke(canIgnore)`：`mAll?.Invoke(canIgnore)` + `if(!canIgnore) base.Invoke()`。
- **不做 `Modifying`（只 true）属性**：三个投影只 materialize 有用的两个——全量（带 bool）与 false（无参）；只-true 几乎无真实用例、且重引第二个概念名。要"只中间态"用 `Modified.Subscribe(b=>{ if(b)… })`。
- **`canIgnore` 语义**：`true`=可忽略的调整中间态（merge 期间每次发），`false`=结果态（merge 结束发一次 / 单次编辑发）。
- **`WillModify` 对称同型**（`IModifiedEvent`）；眼下无"将要改中间态"消费者，可先只接无参、加性补带 bool，不返工。
- **数据对象的 `Modified` 退出 `IMergableEvent`**：合并是**数据对象修改生命周期的属性**（治理 Modified + canIgnore + 撤销命令、树级 NotifyFlag），不是单个事件的属性，故数据对象的合并入口移到 `MergeNotify()`。但 `IMergableEvent`/`MergableEvent`（事件自持合并逻辑、`MergeHandler` 驱动）**保留**——它服务"与数据对象无关、需自合并触发的独立事件"，真实消费者是选择变更 `ISelectableCollection.SelectionChanged`（`MidiPart` 用 `MergableEvent` 合并批量选择通知）。这正是事件自合并模型的正当用例，与数据对象事件模型正交。
- **合并作用域归数据对象、升级为 `using` 作用域**：`BeginMergeNotify`/`EndMergeNotify` 留数据对象单一来源，并提供 `IDisposable MergeNotify()` 让进=Begin、出=End（异常也平衡，消除满仓手动配对的泄漏面）。调用点 `part.Notes.ListModified.BeginMerge()` → `part.Notes.MergeNotify()`。
- **`ListModified`**：改成 `IModifiedEvent`、退 `IMergableEvent`；删死字段 `mListModified`（从没被读，属性返回的是 `mDataLinkedList.Modified`）；**命名摆明"结构变更 vs 深层变更"**（它=仅增删成员，与继承来的 `Modified`=任何深层冒泡 不同）。修 `AnyEvent.OnRemove` 的 bug（移除时误调 `Subscribe` 应为 `Unsubscribe`，泄漏）。
- **删投机 Wrapper**：`IDataObject<T>.Wrapper` 的 `FromGet`/`ToSet` 值变换钩子（全仓零 override）+ 未实例化的泛型 `Wrapper`。三字段适配器 `NumberProperty`/`StringProperty`/`BoolProperty` 同形，合成一个 `PropertyField<T>(read,write)`。

#### 共识 B：DataObject 核心

- **`IDataObject` = 纯契约**（`Modified`/`WillModify`/`Head`(或 `Document`)/`Attach`/`Detach`/merge/`Commit`/`Undo`…），**移除 protected 状态成员 + 私有算法 DIM**；状态与算法（parent/children/NotifyFlag + `ChangeParent`/`ChangeNotifyFlag`/notify）移入 **`abstract class DataObject` 基类**（普通私有字段/方法，零 `((IDataObject)this)` cast），**退役嵌套 `Implementation`**（基类即实现）。
- **`GetInfo()` / `SetInfo(T)` 对称契约 + `Set` 交互式分层**（与 C++ `info()`/`setInfo()` 对齐）：
  - `IDataObject<T>.SetInfo(T)` = **纯契约设值**（去重 + 记命令，**无交互副作用**），与 `GetInfo()` 对称。复合扇出 / 装载走它。
  - `IDataProperty<T>.Set(T)` = **交互式设值**（带额外逻辑/副作用，可覆写点）。用户编辑走它。类型上卡死：`Set` 只在 `IDataProperty<T>`（属性/字段类），复合/容器（`IDataObject<T>`）只有 `SetInfo`——`note.SetInfo(info)`、`pos.Set(5)` 各得其所，复合**没有** `.Set`（编译器强制）。
  - 叶子的读/写原语是**方法**:`GetInfo()`(读,虚/抽象)+ `SetValue(T)`(裸写,虚/抽象,仅落值不记命令/无副作用,只被命令与装载用);`Value` 是**非虚只读 getter** `=> GetInfo()`(`IReadOnlyDataProperty<T>.Value{ get }` 只 getter,无 setter——读=属性、写=方法)。契约/交互写在基类:`IDataObject<T>.SetInfo`(去重+命令,命令经 `SetValue` 落值)/ `Set`(默认等同 SetInfo,`DataLyric` 等覆写叠副作用)。多态落在**方法**(`BPM`/`DataPropertyValue` 覆写 `SetValue`、`DataLyric` 覆写 `Set`),不虚化 `Value` 属性。`IDataObjectExtension.SetInfo` 扩展供 concrete 类型调用显式实现(替代退役的 `.Set` 扩展)。
  - **业务/UI 编辑统一走 `Set`（透明拿副作用，调用方把 DataLyric 当普通 DataString，无需感知）；纯 `SetInfo` 只被数据模型自身的复合扇出（如 `Note.SetInfo` 装载 NoteInfo）使用——区分关在框架内部,业务调用方永不需要选**。
- **无自身 info 的抽象节点（如 `Part`）只实现非泛型 `IDataObject`**——`DataObject` 与 info 无关，只管父子/命令传递/merge，**不泛型化**；有单一 info 的（叶子、`Note`）才 opt-in `IDataObject<T>`。**原子复合**（如 `Voice`，普通字段无子）`SetInfo` 自持复合 `ModifyCommand` + 私有裸写 `WriteInfo`；**可分解复合**（Note/Track/…）`SetInfo` = `using MergeNotify()` + 逐子 `child.SetInfo(...)` 扇出、无复合命令。
- **删 `protected static SetInfo` 扇出 + 裸写通路**：124 处 `IDataObject<X>.SetInfo(child,…)` → `child.SetInfo(…)`。构造期复合在 attach 自己到 document 前先 `child.SetInfo` 子，因无 document 祖先 → 应用但不记录（沿用现有 `Parent?.Push` 上爬到 null 的行为）。
- **Push 沿用现有 document-rooted、不缓存**：每次 push 爬树到 `DataDocument`（深度≈5、仅用户编辑触发的冷路径）；**否决** Attach 时缓存 document（任意中间 parent reparent 即失效，得写子树失效逻辑，为不存在的性能问题买复杂度）。
- **Wrapper 瘦身**：接口去 protected 状态后，`Wrapper` 只转发约 10 个公开方法（那 6 行 protected 状态转发消失），保留其"借壳数据对象"用途（属性面板字段适配器共享文档身份）。

**不引进**：Qt 的 QObject signal/slot（C# event 替代）、per-object `QMutex`、`moveToThreadRecursively` 线程亲和（数据类主线程）。

**已解（落地中发现并修正）**：上一轮标的"复合扇出走 `child.Set` 会在装载时触发 `DataLyric.Set` 副作用（清音素）"——由 `SetInfo`（纯）/`Set`（交互）二分**根除**：扇出/装载一律走 `child.SetInfo`（无副作用，载入歌词不会清掉刚载入的音素），用户编辑才走 `Set`。这正是 #4 对称契约的实质收益，非仅命名。

**风险 / 落地须核**：
- **复合编辑原子性从"单复合 `ModifyCommand`"改为"per-leaf 命令 + `Commit` 打包成 `CompositeCommand`"**（原子复合 `Voice` 例外，仍自持复合命令）。多数等价（逻辑动作以 commit 为边界、`DataDocument.Commit` 已打包），但须**逐处核对**依赖"未提交期存在单条复合命令粒度"的代码（partial `DiscardTo`、读未提交数等）。
- **构造/装载期 `child.SetInfo` 会创建并随即丢弃 `ModifyCommand`**（冷路径分配；万 note 工程量级若实测有压力，保留一条 document-presence 快路径跳过命令创建）。
- `ListModified` 改名波及订阅点；`IMergableEvent` 数据对象侧退役波及 `BeginMerge`/`EndMerge` 调用点（迁到数据对象的 `BeginMergeNotify`/`MergeNotify()`）。

**范围**：A+B 一次落地（用户定，讨论即落地不留坑）。

**伏笔（已解 → §三.22）**：IEvent 框架"事件作一等值 + `When`/`Any` 组合子"的方向与形状抉择，已在 §三.22 落定（保留委托形状、收敛单一原语、Holder 命名）。

### 22. IEvent 事件框架：保留委托形状 + WhenAny 单一原语 + Holder 命名（#12 后查漏补缺）

承 §三.21 伏笔。先讨论达成共识再落地，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**；纯结构重构、行为保持（真机验证待用户）。

**方向（确认）**：C# 原生 `event` 非一等公民（不可赋值/存储/传递、不能写组合子），`IEvent<TEvent>`（仅 Subscribe/Unsubscribe）把它包成可订阅的一等对象——方向正确且无可回避，同 Rx/FRP。真正抉择在形状。

**核心决策：保留委托形状（`IEvent<Action<…>>`），不上 Rx 值形状（`IObservable<T>`）。** 立"**push 通知 / pull 值**"为贯穿原则——与 §三.13 否决 `IExpression<T>` 反应式值图是同一条原则的两次应用：事件（push）只触发重算，值/谓词（pull）保持普通 `Func`，不建 push 式 computed-value 图。理由：① 真实只用 `When`/`WhenAny`/`Where` 三个组合子，且都是绑在数据对象图上的领域重接器，非 Rx 开箱算子（动态集合 merge 裸 Rx 还要拖 DynamicData）；② 委托形状天然多元（`Action<T1,T2>` 等在用）、热路径零分配（multicast delegate），Rx 强制 1 元 + 元组税 + 观察者链分配；③ 上 Rx 会推翻 §三.21 刚锁的 `IModifiedEvent` 零迁移兼容。业界同源参考：FRP（Fran 1997 的 Event/Behavior）、F# 一等事件（`Event.map/filter/merge`，委托形状非流）、Rx（`Select+Switch`=`When`）、ReactiveUI `WhenAnyValue`（属性链自动重接，与 `When` 同形）——本套是"ReactiveUI `WhenAny` 人体工学、剥掉 Rx 底座"的独立收敛。

**WhenAny 单一原语（消三份拷贝）**：原 `Any` 在 List/Map/LinkedList 各嵌一份"活订阅矩阵"（下游 handler 集 × 当前成员集，成员增删/下游订退保持叉乘）。收敛为单一基接口 `IReadOnlyDataCollection<out T>`（`ItemAdded`/`ItemRemoved`/`Items` 三成员）+ 唯一 `AnyEvent` 扩展；`IReadOnlyDataList`/`IReadOnlyDataLinkedList` 直接继承该基（自身不再声明增删事件，单一来源、无钻石），Map 因结构事件是二元（键+值）另持一元投影（`mValueAdded`/`mValueRemoved`，OnAdd/OnRemove 同步触发，经显式接口实现喂给 `IReadOnlyDataCollection<TValue>`）。`Any`→`WhenAny`（消与 LINQ `Enumerable.Any(predicate)` 的重载歧义、与 `When` 同族）。**按正确语义重写**：修 `AnyEvent.OnRemove` 误用 `Subscribe`（应 `Unsubscribe`）的退订泄漏。

**Where（响应式过滤）一并落地**：`collection.Where(谓词)` 返回随谓词翻转合成 `ItemAdded`/`ItemRemoved` 的过滤视图（`INotifiableProperty<bool>` 重载），与 `WhenAny` 同族、可串接（`Where(...).WhenAny(...)`）。它属事件层组合子，与 §三.13 的 SDK Config 条件系统**不同层、无关**。**修泄漏**：谓词订阅留存 handler、成员移除时真正退订（原实现漏存 disposable）。

**Holder 命名（替 Provider/Owner）**：`IProvider<out T>`→`IHolder<out T>`、`Owner<T>`→`Holder<T>`；事件 `ObjectWillChange`/`ObjectChanged`→`WillModify`/`Modified`（与数据对象、`INotifiableProperty` 同词汇），访问器 `Object`→`Value`。它是"伪装成常驻实例的稳定句柄、背后可换、注册一次自动重接"，`When` 挂其上（引用句柄专属，值无内部事件不参与）。`When`（`IHolder`）与 `WhenAny`（`IReadOnlyDataCollection`）基座不同、各单份实现，**不强并**（When 无重复，强并须让 Holder 假扮 0/1 元集合，负收益）。

**`INotifiableProperty` 保留**（瞬态、不可撤销的可观察值单元，是 `DataProperty` 的非撤销孪生），仅与 Holder 统一事件词汇；接口/类型层面的大一统**推迟**（见下）。

**推迟（记录在案，均"无消费者 + 加性可后补"）**：
- **`ISource`/`IChangeNotify` 统一根**（`{ Modified; WillModify; }`，让 Holder/`INotifiableProperty`/`IDataObject` 共一"会变即通知"根）：无"任意变化源"消费者（响应变化的代码直接订 `.Modified` 那个 IEvent，不需源共享类型）；且值访问器因可空 / 协变 / `class`-vs-`notnull` 三墙无法进根，根只能是无值空壳。
- **`IDataObject` 补 `WillModify`**（C++ `DataObject` 有对称 `aboutToModify(canIgnore)`）：现 C# 侧全程无"改之前"通路，补它须铺前置通知 + 镜像 merge 计数，且无消费者。
- **dataobject 并入统一根**：除上述，还撞 C# 属性类型不变性（`IDataObject.Modified` 是 `IModifiedEvent`，与根的 `IActionEvent` 不能隐式实现，须 `new` + 显式实现之疣）。
- 三项的真实使能消费者预计是属性面板 live-bind 出现"瞬态字段（不进撤销）与 `DataProperty` 字段共用一套绑定机器"——届时纯加性补。

### 23. 属性面板 multiple/invalid 三态呈现（#12 后查漏补缺）

承 §三.20 live-bind 痛点尾、§三.14 的 `Invalid` shim。先讨论达成共识再落地，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**；真机已验证。

**问题**：live-bind 改造后多选"多值"呈现断了——字段适配器 `PropertyField<T>` 把读到的空哨兵 coerce 成默认值，控件 `DisplayMultiple()` 沦为死代码；且 `Null` 与 `Multiple` 共用一个哨兵无法区分。

**三态模型（定在叶子值层、沿 JSON 树递归组合）**：`Concrete`（单选/多选全等）/ `Multiple`（多选 ≥2 不全等）/ `Invalid`（无选中）。前端**与插件两轴都区分**——插件诉求来自条件谓词需知宿主真实态（`IsMultiple()`/`IsInvalid()`，naïve `To*` 取值一律 false 安全降级）。**不开放 JSON null 作插件可见合法值**（会与 Invalid 视觉撞车，且类型内约定 NaN/""/显式 None 选项可模拟）。

**机制**：Primitives 增 `PropertyMultiple` 哨兵 + `PropertyValue.Multiple`/`IsMultiple()` + `PropertyType.Multiple`（**反转 §三.14"清理 Invalid"——`Invalid` 正式化为无值哨兵、`Multiple` 与之并列**；非 PropertyValue 全树重构，后者仍推迟）。哨兵**瞬态、永不序列化**（`GetInfo`/CBOR/JSON 走 `To*` 链跳过）。`MultipleDataPropertyObject.GetValue` 三态返回；`IDataPropertyObject` 不加"多选"方法（避免漏进单/多共用契约），改由字段经 `IRawValueProperty` 暴露未 coerce 原始值、绑定层 `Refresh()` 据此分派 `Display`/`DisplayMultiple`/`DisplayNull`。嵌套对象靠 #12 的 `PropertyPath` 扁平寻址自然递归，无需合成嵌套数据源。**（#12 后查漏补缺 §三.24 改导航式后，嵌套改由 `Object(key)` 逐层导航递归；三态机器本身——哨兵 + `IRawValueProperty` + `Refresh()` 分派——模型无关，一行未改。）**

**各控件呈现**：CheckBox 高亮底+dash（多值）/ 空框（无值）；TextBox watermark `(Multiple)`（占位非真实文本）/ 空；ComboBox placeholder `(Multiple)` / 空；Slider 空轨 + 标签 `-`（多值）/ 空（无值）。无选中改为绑空 `MultipleDataPropertyObject` 让控件在遮罩下呈 Invalid。

**真机暴露并修复（非三态本身、属相邻既有缺陷）**：① 扇出逐对象触发刷新致中间态闪烁/文本框光标跳 → `MultipleDataPropertyObject.SetValue` 包进 merge（中间 canIgnore、结果态订阅者不被打断）+ `TextInput` 编辑中（聚焦）不被刷新覆盖；② CheckBox 图标用共享 `mCheckItem.Icon`，`DisplayNull/Multiple` 改之而正常 `Display` 不还原 + 池复用残留 → 只在进入勾选确定态设 √、取消勾选不动图标（避开 150ms 颜色淡出动画把 √ 画出来）；③ Slider thumb 初次选中跳动 = 首帧 Bounds 滞后的三个侧面：bind 先于 add（值）、`ThumbPivotPosition` 用 `finalSize` 算端点（轨道尺寸）、`AbstractThumb.Piovt` 用 `DesiredSize` 算居中偏移（Bounds 首帧为 0）。

**推迟不变**：条件表达式系统（§三.13，纯增量可后补，本话题只铺哨兵地基未实现谓词）、PropertyValue 全树重构（§三.14，仅加 Multiple 哨兵非包装类型）。

**测试**：测试 voice `V1.Suite.Voice` 的 NoteProperties 增四类控件 + 嵌套 `vibrato` ObjectConfig；独立测试文档 `tests/PROPERTY-TRISTATE-TEST-CASES.md`（只测三态范围，不污染基线）。

### 24. 干掉 PropertyPath，改导航式数据模型（#12 后查漏补缺）

承 §三.20 提交①（live-bind 用 `PropertyPath` 扁平寻址）。#12 选扁平寻址是为绕开 effect 分支那套半成品（`IDataPropertyObjectField`/`MultipleDataPropertyObject`/`PropertyPath` 全 `NotImplementedException`）。本节落地 effect 的原始意图——**把 `IDataPropertyObject` 路径上每个节点抽象成 `IDataPropertyObject`（对象）或 `IDataProperty<T>`（叶子），拿到就当普通数据对象用**（绑定/观察/撤销一视同仁）；先讨论达成共识、讨论即落地，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**，真机已验证。

**接口（节点即撤销根）**：`IDataPropertyObject : IDataObject`（**折进 `DataRoot`**——节点本身就是可绑定/可撤销的数据对象，方案 A），加 `Object(string)` 导航 + 单层 `string` 版 `GetValue/SetValue`；`PropertyPath` 类型整删。叶子访问器保留 typed 三件套 `NumberField/StringField/BoolField`（type 固定几个，泛型 `Field<T>` 会把"传错类型"从编译期降级成运行期，否决）；`PropertyField<T>` 借壳节点本身。

**单选近乎免费 + 懒导航**：`DataPropertyObject` 的 `GetValue/SetValue` 单层化（不再下钻）；`Object(key)` 返回懒视图 `ObjectView`——**读经内部 `FindObject` 返回 default、不创建；写经 `GetOrCreateObject` 按需建路径**（保住"浏览不污染序列化、bind 不记假撤销"）。internal `ILazyObjectNode` 让视图与 `DataPropertyObject` 互链，不进公开接口。

**多选复合递归（effect 没写完的就是这块）**：`MultipleDataPropertyObject` 改持 `IReadOnlyList<IDataPropertyObject>`，`Object(key)` = 复合各成员的 `Object(key)` 递归而成；**关键正确性点**——某成员缺该嵌套时经其懒视图读出 default，**仍正确参与三态比较**（与有该嵌套的成员不等即 Multiple，而非被跳过误判全等）。`MultiDataRoot` 折进本体（Head/Commit 委托首成员、Modified 合并各成员，各成员撤销根根锚最外层对象、冒泡覆盖全部嵌套写）。

**连带简化**：删 `DataPropertyObject.PropertyModified`（`IActionEvent<PropertyPath>`）及其 `OnAdd/OnRemove` 路径拼接簿记——其唯一消费者只用它触发 note dirty 且忽略 path，改订 `Properties.Modified`（`DataObject.Modified` 本就冒泡全部嵌套修改）。

**三态机器零改动**：`IRawValueProperty` + 绑定层 `Refresh()` 三态分派模型无关，复合叶子照样经 `node.GetValue` surface 出 Multiple/Invalid。

**array 字段押后（倾向 DataObjectList）**：object 不是叶子而是 `Object(key)` 导航节点，无需 ObjectField；array 当前**零消费者 + 形态未定**（值快照叶子 vs 索引集合节点，纠结点即 §三.11 #4 尾的 `DataList` vs `DataObjectList`），不在本刀捎带。倾向 `DataObjectList`（元素天然是 `IDataObject`），但取决于将来数组 UI 控件的交互语义；接口对两条路都前向兼容（补 `ArrayField` 或 `Array(key)` 均加性）。

**测试**：`V1.Suite.Voice` 的 `vibrato` 加深到 3 层对象 `vibrato → lfo → range`，独立测试文档 `tests/PROPERTY-NAVIGATION-TEST-CASES.md`（深层懒建 / 多选深层三态 / 部分成员缺嵌套 / 深层撤销 / preset 往返，不污染三态基线）。

### 25. 条件属性面板：`ObjectConfig = f(context)` 整树重算 + commit 触发（#12 后查漏补缺，已改代码）

承 §三.13（功能保留、落地随 #12）。本节把该功能**落地**——`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）+ 测试插件 sln 均 **0 错误**；真机验证待用户。讨论中推翻了 §三.13 早期"逐字段可见性谓词（B 内核）"的设想，收敛到更统一的模型。

**模型：config 是数据的纯函数。** 插件不再交一棵静态 `ObjectConfig`，而是交一个纯函数 `f(context) → ObjectConfig`；host 在相关值变化时重算整棵 config、diff 到控件树。"显隐 / 换控件 / 选项内容变 / 控件数量变"全是 `f` 在不同输入下返回不同结果的**涌现**，不再是一等概念（弃掉讨论中途发明的 `ConditionalConfig`/`DynamicKeyed`/`VisibleWhen` 等节点类型）。

**不违背 push/pull 原则。** `f` 是纯函数，值变化（push 通知）→ 重跑一遍（pull）→ diff；无持续 computed-value 订阅图。这是 §三.22 立的"push 通知 / pull 值"原则的应用，与 §三.13 否决"响应式 DSL 锁进冻结 ABI"不冲突。

**context = 注入式只读对象**（范本 = effect 分支 `IVoiceSynthesisContext { VoiceID; Properties }`）。挂当前求值所需的值，冻结接口**纯加性扩展**（以后加只读属性不破坏旧插件）。

**依赖链 = 两层单向。**
- `part config = f_part(part 自身稀疏值)` —— 输入是 part 自己，即**面板内部联动**（某控件影响同面板另一控件）。
- `note config = f_note(part 稀疏值 + note 合并稀疏值)`。

上游 commit 沿链触发下游重算（part 提交 → 重算 part + note），下游不影响上游 → 无环、方向确定，host 自上而下跑一遍。**project/track 不参与**：`IProject`/`ITrack` 只有 host 内置字段（导出/混音/tempo/轨道），**无插件属性 schema**，不是同构的第三层 `f`；将来若要让 tempo/采样率等影响下游，以"context 里的只读 host 量"注入（形状像 `ITuneLabContext { SampleRate }`），纯加性。

**触发 = commit（主防线）。** 只在值提交（最终态）时重算，拖动/输入的中间值不触发。一招同时：① 把 `f` 执行频率压到"人手提交"级（非每帧）；② 消除交互中途结构突变（拖动中控件被 dispose/重建致手势断、焦点丢）；③ 充当**自依赖边界的安全网**——接口允许作者写"`x` 的 config 读 `x` 自己的值"这种本不该写的式子，commit 触发让它拖动中稳定、松手才变，手势已结束。离散控件（checkbox/combobox 点即 commit）的"选了就更新依赖项"与连续控件（slider/textbox 结束才 commit）在此规则下都正确。

**性能：不做 host 记忆化。** commit 语义决定"每次触发必然伴随至少一个值变化"→ 记忆化命中前提（触发但输入没变）几乎不成立；唯一可能命中的"沿链传播但下游不依赖该字段"需**依赖追踪**才能精确判断，而那个已被否决（保持 `f` 黑盒纯函数），粗粒度整 context 深比较救不了。故记忆化是净负担（每次白做一次 `PropertyObject` 深比较），砍掉。性能防线就是 commit 触发的低频 + `f` 契约。

**多选 = 合并喂一次（方案 A）。** note 多选合并成**一个**三态 `PropertyObject` 喂一次 `f`（非逐 note 算——后者 O(选中数)，大选区平方级）。`f` 依赖的字段若多选不一致，读到 `Multiple` 哨兵（§三.23），作者按默认 fallback，复用三态、不需新机制。

**默认值 = "字段不存在"。** `f` 的输入是**稀疏实际值**（`GetInfo` 只含写过的字段，§三.24 懒建不存默认），所以 `f` 不需"先有 config 才有输入"→ 破"算 config 要先有值、要默认值又要先有 config"的环。推论：① **恢复默认 = 清空数据节点字段**（回到"不存在"），现靠静态遍历 `config.Properties` 写 `DefaultValue` 的重置（`ResetPartPropertiesToDefaults`）简化为"清空"，不再依赖 config 可遍历；preset 保存仍 `GetInfo` 出稀疏字段。② **显示 fallback 不阻塞**：渲染叶子时数据有值用实际值、无则用 `f` 当前输出 config 的 `DefaultValue`（此时已拿到 config）。③ `f` 内读 ctx 缺失 key = `Invalid` 哨兵，作者自行 fallback。

**重建 = keyed-diff reconcile。** `f` 每次返回全新 `ObjectConfig`，host 不可拆了重建（编辑中光标/焦点丢、控件闪），须按 key diff：同 key 同类型 → 复用控件仅更参数；key 消失 → dispose；新 key → 建；同 key 换了 config 类型 → 换控件。复用编辑中控件，接 §三.22 落地的 `IReadOnlyDataCollection`/集合增删事件。

**边界：array 正交。** `ObjectConfig` 是 map（key 唯一），所以"动态数量控件"只要 **key 唯一**（如文本框 "abcd" → 按字母 key 出 4 个滑条，数据走现有键值模型 + §三.24 导航懒建）就落在本方案内、**不需 array**。真正"有序 + 可重复"（重复音素 "i i an"、可中插删的列表）是**独立话题**（`DataList`/`DataObjectList` 落地，§三.24 押后项），与"config 是不是值的函数"正交，不在本方案。

**实现落点**：
- **SDK.Voice**：新增 `IPropertyContext { PartProperties; NoteProperties }`（注入式只读，承载稀疏值快照）；`IVoiceSource` 加 **DIM** `GetPartConfig`/`GetNoteConfig(IPropertyContext)`，默认回退到静态 `PartProperties`/`NoteProperties`——旧插件 / Legacy 适配器不覆写即得"静态 config"行为，零改动。
- **host**：`IVoice`/`Voice` 把静态 `PartProperties`/`NoteProperties` 改为 `GetPartConfig`/`GetNoteConfig(context)` 转发 `IVoiceSource`。
- **`PropertyObjectController`**：重构为 keyed-diff reconcile——`Reconcile(ObjectConfig)` 幂等对齐控件树（同 key 同类型复用控件 + `Update` 改参数、增删建/弃、纯参数变化不 `Relayout`、仅结构变才重排）；`SetConfig`（数据对象切换）先 `ResetConfig` 再全建（复用 creator 仍绑旧对象字段，故 data 变必须重建）。effect 参数面板 / 嵌套 ObjectConfig 走静态 `SetConfig`（reconcile 一次、不订阅）。
- **`PropertySideBarContentProvider`**：在数据对象 `Modified` 的**结果态**通道订阅（§三.21：commit 才触发、中间拖动态 `canIgnore` 不触发）——part 值 commit → 重算 part 面板 + 沿链重算 note 面板；note 值 commit → 重算 note 面板。context 由 `mPart.Properties.GetInfo()`（稀疏）+ 选中 note 三态合并快照（`MergeNoteSnapshots`：同 key 全等给值、不等给 `Multiple`、全缺不出现）构造。数据对象切换（选中 / voice 变）走 `SetConfig` 重建。

**实际取舍**：
- 同 key 换控件类型 = dispose + 新建；数据 key 同名但旧值类型不符时由 `DataPropertyObject.GetValue` 的 `TypeEquals` 退默认（未做值迁移）。
- reconcile 在 `Modified` 多播链里**同步**执行——横向依赖（B 依赖 A）下被 commit 的控件 A 同 key 同类型必被复用、不会在自身事件回调中被 dispose，安全；"自依赖"（A 改 A 自身结构）这一 §三.13 明示"本不该写"的边界，commit 触发已把它从"拖动中崩"降级到"提交后重建"，若真机暴露同步 dispose 问题再改 defer。
- **记忆化未做**：commit 语义下每次触发必伴随值变化、几乎永不命中，深比较是净开销，砍掉。
- **context 统一推迟**（用户定）：现用专用 `IPropertyContext`；将来做 voice API 整体改造（合成 API context 化）时，把 `GetNoteConfig`/`GetPartConfig` 参数换成统一 context（合成与 config 求值共用），`IPropertyContext` 退役——内测期 + DIM 默认，签名演进加性平滑。

**测试**：测试声库 `[v1-suite] Conditional`（同包新增 `ConditionalVoiceSource`，不动基线 `SuiteVoiceSource`）演示显隐/换控件、选项随值变、动态数量控件（key 唯一边界）、part→note 沿链、多选降级；独立文档 `tests/PROPERTY-CONDITIONAL-TEST-CASES.md`（不污染三态/导航基线）。

### 26. ComboBox 升级：option 任意基础类型 + DisplayText（值/显示分离）（#12 后查漏补缺，已改代码）

承 §三.12「combo/config 入口用具体类型重载拿编译期保证、不引入第二个 box」、§三.14「单一 `PropertyValue` box」。原 `ComboBoxConfig` 选项是纯 `string`、存进数据的就是选项字符串本身、控件硬绑 `StringField`——插件无法用 int/double/bool 当值，也无法「界面显示文本 ≠ 底层存储值」。

**契约（SDK.Base）**：
- `ComboBoxOption`（struct）：`PropertyValue Value` + `string? DisplayText`（`ShowText()` = DisplayText 缺省回退 `Value.ToString()`）；实现 `IEquatable`（供 reconcile 的 `Options.SequenceEqual` 免反射）。对 `bool`/`string`/全部整数与浮点类型提供隐式转换（数字一律 `PropertyValue.Create((double)v)`，与 JSON number 一致）——插件可直接写裸值。
- `ComboBoxConfig(IReadOnlyList<ComboBoxOption> options, ComboBoxOption defaultValue)`：默认值是「值」而非「索引」；`IValueConfig.DefaultValue => DefaultOption.Value`。

**关键取舍：不设 `IReadOnlyList<基础类型>` 的便捷构造器。** 否则集合表达式字面量（如 `["a","b"]`）会在「string-list ctor」与「ComboBoxOption-list ctor（元素经隐式转换）」间产生重载二义——C# 12 的择优规则判不出更优、直接报 CS0121（C# 13 才补此规则）。只留唯一的 ComboBoxOption-list ctor 后：字面量 `["a","b"]` / `[1,2,3]` / 混合 `[1,"x",true]` 都逐元素隐式转成 ComboBoxOption、匹配该唯一 ctor，**C# 12 也不二义**；已建好的 typed 变量（如 `List<string>`，不会逐元素隐式转）由调用方就地 `.Select(o => (ComboBoxOption)o).ToList()`。

**host**：
- `ComboBoxController` 值模型 `string`→`PropertyValue`（`Display` 按值在 options 里反查下标高亮——`PropertyValue` 是 struct，手写 `.Equals` 循环，不能用 `where T:class` 的 `IndexOf`）；保留 `int Index`（FunctionBar 按选中位置联动）+ 显式 `IValueController<string>` 外观（Settings 的语言/驱动下拉、`Select(int.Parse)` 桥到 int 设置）。
- 新增 `IDataPropertyObject.ValueField(key, default)`：裸 `PropertyValue` 字段（identity read/write），供值类型不定的控件按原始值绑定；三态经 `IRawValueProperty.RawValue` 仍由绑定层分派。`ComboBoxCreator` 改绑 `ValueField`（存的是 option 值本身、非显示文本）。
- 调用点 index→值语义迁移：preset 下拉、FunctionBar 量化、Settings×3、conditional `pick`、legacy 兼容层 `EnumConfig→ComboBoxConfig`（typed list 一并转 ComboBoxOption）。

**测试**：基线 `SuiteVoiceSource` 加 `quality` 项（int 值 0/1/2 + DisplayText 显示 Low/Mid/High，演示值/显示分离 + 任意类型）；独立文档 `tests/COMBOBOX-TYPED-OPTIONS-TEST-CASES.md`（不污染条件面板基线）。`TuneLab.sln` + `V1.Suite.Voice`（net8 默认 C# 12）均 **0 错误**。

### 27. PropertyValue 内部装箱优化（#12 后查漏补缺，已改代码）

`PropertyValue`（`readonly struct`）原以 `object mValue + System.Type mType` 存储——每次 `Create(double)`/`Create(bool)` 都把标量装箱进 `object`，高频构造（属性值、撤销去重、合成数据）累积可观 GC 压力。

**改为「类型标签 + 字段联合」**：`PropertyType mType`（标签）+ `double mNumber`（number 值；bool 编码为 0/1）+ `object? mReference`（string/PropertyObject——本就是引用、零额外装箱）。null/multiple 哨兵仅由标签表达、不占引用槽。`ToDouble`/`ToBool` 直读 `mNumber`、零拆箱；`ToString`/`ToObject` 取 `mReference`。

**行为与公开 ABI 完全保持**：`Type`/`TypeIs<T>`/`TypeEquals`/`Is*`/`To*`/`To<T>`/`Equals`/`==`/`ToString()`/`Create`/隐式转换签名与语义不变（`Equals` 仍按标签分派，number/boolean 用 `double.Equals` 令 NaN 相等，string/object 走引用 `Equals`/深比较）。`default(PropertyValue)` 仍为 `Null`（`PropertyType.Null == 0`）。`PropertyNull`/`PropertyMultiple` 哨兵类保留（不再被存储，删除属独立的 ABI 清理）。泛型 `To<T>` 的 number/boolean 分支仍装箱（罕用路径），具体类型走 `To*` 零拆箱。`TuneLab.sln`(Debug/Release) + `legacy/Legacy.slnx`(Release) 均 **0 错误**；纯内部重构、行为保持（真机验证序列化往返 / 面板三态待用户）。

---

### 28. voice 部分更新后 effect 链按段增量重渲染（确立，已改代码：提交①②③）

承 §三.19（effect = 离线整段音频变换 + 每级缓存仅下游失效）。§三.19 落地时 effect 链跑在整 part 链尾音频上：voice 产物一变就从 stage 0 整链重跑（昂贵 SVC 整段重过），voice 的分片粒度没传导到 effect。本话题确立**按段增量**——voice 改了哪段，只有那段重新过 effect 链。

**根因**：现管线（`VoiceSynthesisPipeline`）在 `PullProducts` 把所有已合成状态段求并集拉成单条 buffer，effect 链跑这一条；每片 voice 完成都 `RunEffectChain(0)`（靠 generation + `StopEffectTask` 反复取消重启，实际只在 voice 停止出货后完成一次）。effect-self 脏已是按级增量（`SetEffectChainDirty(i)` 从 i 级重跑），但 voice 侧变化无段粒度——这是 §四 #11 当年挂账的"按状态段增量过链"后续。

**机制：音频段握柄（voice SDK 加性扩展）**。把 voice 本就内部持有的分片，提升成宿主持有的一等握柄 `IAudioSegment`（详见 voice 设计文档「产物与状态」节）：
- `context.CreateAudioSegment(long sampleOffset, int sampleCount)` → 宿主据声明的**固定起始（全局采样位置）+ 固定长度**一次性分配缓冲并登记；插件经 `Write(int offset, ReadOnlySpan<float>)` **就地写子区间**（`ReadOnlySpan` 借用语义、宿主拷入自有缓冲，插件可复用 scratch；渐进合成因就地写不累积重拷、O(n)），`Commit()` 标完成，`Dispose()` 释放。**位置/长度需变 → 删旧段建新段**。
- **采样点级寻址**：起始用 `long`（全局轴位置，沿用旧 `ReadAudio` 协议、不给全局轴封 int），段内 `Write` offset 用 `int`（索引 `float[]`/`Span`，CLR 本就 int 域）。秒→采样的换算归插件（它持 native 率、知帧对齐），避免宿主侧 `(long)(秒×率)` 舍入。
- **不在创建时钉死长度的理由**：模型按帧产出（frames×hop）、含 look-ahead/对齐/可选时长伸缩，合成开始未必知精确采样数。一次性渲染的插件**渲染完再 create**（长度已知）；真流式插件按估算 create、估偏（罕见）即重建。
- **与状态段解耦**：音频段是音频承载 + effect 失效单元；`SynthesisStatusSegment` 仍是 UI 状态带（着色/进度/错误）。两套分区可不同，插件内部是否用一个对象同时背两套是插件自由，宿主不假设对齐。
- **Commit 是送 effect 的唯一闸门**：合成中间态（Commit 前的写入）只走状态段让用户看波形/进度，冻结数据（Commit）才进 effect——合成爆发期不拖着 effect 频繁重合成，闸门在协议层而非宿主防抖。

**effect 重渲染：二维失效（段 × 级）**。缓存升为 `cache[segment][stage]`：
- voice 某段 Commit → 丢 `cache[seg][*]`，该段从 stage 0 重过；
- effect[i] 参数/启用/自动化变化 → 丢 `cache[*][i..]`，各段从 i 级重过；
- 链结构变化 → 从 0；
- 链尾 = 各段末级输出按时间拼接。

现有"单 buffer + `SetEffectChainDirty(i)`"是"只有一个段"的退化情形。effect SDK 形状不变（仍 `CreateSynthesisTask(input, output)`，input 是整段 `MonoAudio`）——effect 整段进整段出，只是输入从"整 part"换成"一个段"，effect 引擎无感。

**段间拼接 / 波形 / 播放（提交③ 已逐段）**：段边界由 voice 自己挑（理想落在停顿处），跨段连续性归 voice 分片（承 §三.19 本意，只是从宿主自持 piece 换成插件报出的段），默认不交叉淡化。管线对外暴露**段列表**（各段末级音频 + 波形），不再拼整 part 单条 buffer——播放（`MidiPart.GetAudioData`）与波形（`PianoScrollView`）都按段各自混音/绘制；段间空洞**留白不画**（回到 legacy 分 piece 形态）。收益：稀疏 part 不摊零 buffer、编辑只重算变化段的波形峰值（其余段按 `Samples` 引用复用）、补全提交② 的增量闭环（不再每次拼接 + 整段重算峰值）。`SynthesizedAudio`/`Waveform` 两属性塌缩为 `SynthesizedSegments`（`record struct {MonoAudio, Waveform}`），原子换数组引用供跨线程读。

**兼容**：legacy 插件出整条 buffer → compat adapter 建一个覆盖整 part 的段，effect 看到单段 = 今日整段行为，零行为变化。

**落地分阶段（均已改代码、三 sln 0 错 + host 33/compat 5 绿）**：
- **提交①**（行为保持的重构）：voice 音频改经 `IAudioSegment` 段握柄交付（去 `ReadAudio`），宿主把各段拼成单条 buffer 喂**现有整 part 一条链**——听感/effect/状态带与改动前一致。
- **提交②**（按段增量，本话题核心收益）：`cache[segment][stage]` 二维缓存 + 段级失效（段 `Commit` 经 `context.AudioSegmentsChanged` 通知管线、握柄身份/CommitVersion 识别变更段）+ `VoiceSynthesisPipeline` 单链运行器（单 `mEffectTask`/`mRunStage`）改为**按段多链、段间串行**（SVC 慢、避免资源爆）+ 链尾各段拼接。改一段只重过该段链、其余段缓存复用。
- **提交③**（波形 + 播放逐段、丢弃拼接 buffer）：管线暴露段列表 `SynthesizedSegments` 替代单条 `SynthesizedAudio`/`Waveform`；`MidiPart.GetAudioData`（播放）与 `PianoScrollView.DrawWaveform`（波形）改为遍历段各自混音/绘制；段波形按 `Samples` 引用相等缓存（只重算重跑段）。段间空洞留白、稀疏 part 省内存、补全增量闭环。
- **后续（缓后，纯加性）**：宿主累积 `Write` 的子区间、随 `Commit` 把脏区间交 effect + effect 自决段内局部重合成 + 段内拼接/淡化（含跨级脏传播形态再定）；写 API 已为此铺好（`Write(offset, samples)` 本就带区间），无需再加接口。

**消费者爆炸半径（已核/已改）**：voice SDK `ISynthesisContext`（加 `CreateAudioSegment` + `AudioSegmentsChanged`）/ `ISynthesisSession`（去 `ReadAudio`）/ 新 `IAudioSegment`；测试插件 `V1.Voice` / `V1.Suite.Voice` / `V1.I18N`；compat `LegacySessionAdapter`（每块一段）；宿主 `SynthesisContext`（段握柄实现 + 登记表 + 通知）+ `VoiceSynthesisPipeline`（按段链运行 + 段列表产物）；消费者 `MidiPart.GetAudioData`（播放逐段混音）+ `PianoScrollView.DrawWaveform`（逐段绘制）改读 `SynthesizedSegments`（`IMidiPart` 的 `SynthesizedAudio`/`Waveform` 塌缩为 `SynthesizedSegments`）。两 SDK 预发布、无野外插件，churn 内部（沿用 §三.19「V1 ABI 零破坏」）。

### 29. PropertyArray（有序可重复列表）+ Config 标签随槽走（设计定稿，待落地）

承 §三.12（值模型留 `Array` 段未产出 / live-doc 数组形态「`DataList` vs `DataObjectList` 随需求定」）、§三.14（`PropertyArray` 推迟，typed 叶子包装 `PropertyBoolean/Number/String` 已弃——零消费者、具体类型重载替代）、§三.23（三态呈现）、§三.25（`config = f(context)` 整树重算）、§三.26（ComboBox 值/显示分离）。对应跟踪 issue「PropertyArray（有序可重复列表）」。需求驱动：重复音素 / 可中插删列表。先讨论达成共识，本节锁形状。

**值容器（地板 `TuneLab.Foundation`，单份）`PropertyArray`** —— 与 `PropertyObject` 完全对称的 sealed 值类：构造拷入 `PropertyValue[]`、`Empty` 单例、`Count` + 索引器 + 枚举、深相等 + `GetHashCode` + `ToString`。元素是 `PropertyValue` 基类型（天然可嵌套：数组套对象 = 重复音素、数组套数组合法），**heterogeneous-capable**，值层不设同型约束；可靠索引访问、可知大小，取出是 `PropertyValue`、消费方按自身协议转。`PropertyArray` 本身即不可变冻结类型，满足「走冻结类型而非裸 `IList<>`」，不另造地板列表接口。`PropertyValue` 加 `Array` 臂：`Create(PropertyArray)` / 隐式转换 / 构造（存 `mReference`、`mType=Array`）/ `IsArray` / `ToArray(out)` / `To<PropertyArray>` / `TypeIs` / 三个 switch（`ToString`/`Equals`/`GetHashCode`）补 Array。

**live-doc（`TuneLab.Hosting.Foundation`）`DataPropertyArray = DataObjectList<DataPropertyValue>`** —— 选 `DataObjectList` 而非 `DataList`（结掉 §三.12 那个未决点）：每元素槽是 `DataPropertyValue`（标量/子对象/子数组三选一），逐元素 attach/undo，中插/删/原位改都是细粒度命令，元素（含嵌套对象）有独立 live 身份、可被面板原位 live-bind。`PropertySlot` 扩第三臂（标量 / 子对象 / **子数组**）；`DataPropertyObject.ToSlot` 把 array 型值 canonicalize 成活 `DataPropertyArray` 子节点（与 object 型→`DataPropertyObject` 对称）。`GetInfo()→PropertyArray` / `SetInfo` / Insert/RemoveAt 递归触底。

**序列化（TLP/CBOR）** —— 抽递归 `ReadPropertyValue`/`WritePropertyValue`（元素可任意类型），CBOR 读补 `StartArray`、写补 `value.ToArray(...)`→`WriteStartArray`。**空数组照写**（present-`[]` 是真实值，不能因空跳过——见下 presence 语义）。只动 TLP 原生格式；ACEP/VPR 等映射各自 schema、不沾。

**Config 标签随槽走：去 `IControllerConfig` 的 DisplayText，引入 `PropertyKey`** —— 显示标签是「插槽」属性、非 config 内在：在 ObjectConfig 字段里是 key 的翻译、在数组元素里是行/类型名、在 `+` 菜单里是可加类型名——同一 config 放不同槽含义不同，故不属 config 本身。
- `IControllerConfig` 回归**纯 marker**（无 DisplayText）；`IValueConfig.DefaultValue`（boxed `PropertyValue`）**保留**——唯一消费者是 `PropertySideBarContentProvider.ResetPartPropertiesToDefaults` 的「config→默认值」递归 walker（恢复默认 / 应用 preset），它要泛型拿任意 config 默认值；数组落地时此 walker 长出 `ArrayConfig`/`ListConfig` 两臂（递归 `Elements` 各位默认值拼 `PropertyArray`）。
- `PropertyKey { string Id; string? DisplayText }`（`readonly struct`）：`Id` 是数据寻址用的稳定标识（= 落进 `PropertyObject` 的 key 字符串），`DisplayText` 是其翻译（缺省回退 `Id`）。**相等性/哈希只认 `Id`**，`DisplayText` 是注解、不入键身份——这恰好让 keyed-diff 在「语言切换、仅 DisplayText 变」时判同键、只重贴标签不重建控件。隐式转换 `string`→`PropertyKey`（无译文）与 `(string, string?)`→`PropertyKey`（带译文）保作者人体工学与 `[CollectionBuilder]` 字面量。
- `ObjectConfig.Properties` 改 `IReadOnlyOrderedMap<PropertyKey, IControllerConfig>`（value 保持纯 config、不套娃）。**一致到底**：`AutomationConfig` 也归此制——`IVoiceEngine.GetAutomationConfigs`/`GetSynthesizedParameterConfigs` 与 `IEffectEngine.GetAutomationConfigs` 的返回从 `OrderedMap<string, AutomationConfig>` 改 `OrderedMap<PropertyKey, AutomationConfig>`、`AutomationConfig` 删 DisplayText。各叶子 config（Slider/TextBox/CheckBox/ComboBox）删各自 DisplayText；`ComboBoxOption.DisplayText` 不动（那是第三概念：选项值→显示文本）。

**两种数组型 config（共用 `PropertyArray` 值容器）** ——
- `ArrayConfig`（定长）：`IReadOnlyList<IControllerConfig> Elements`，逐 index 声明、允许异型，长度 = 声明数、不可增删；第 i 元素由 `Elements[i]` 渲染并绑定 `array[i]`。
- `ListConfig`（变长）：`Elements`（当前已存元素的逐元素 config，插件读 context 传入 array 现算、长度 = 数据元素数）+ `IReadOnlyList<AddableElement> AddableElements`（`+` 菜单候选；单项点 + 直接追加该类型默认值、多项弹下拉按类型名选）。随 `f(context)` 重算故可依状态变化（达上限返回空 → + 禁用）。
- `AddableElement { IControllerConfig Template; string? Label }`（`readonly struct`，隐式自 `IControllerConfig`）：**刻意独立成类型**而非复用 `List<IControllerConfig>`——与 `Elements` 破撞型（这是「下一个元素可选的若干类型」选择集、非「后续若干元素」位置序列）。`Template` 提供新元素 seed 默认值（宿主递归解析）+ 渲染配置，`Label` 是菜单类型名。
- 数组元素无 key、故**无逐元素标签**；要带标签的行用 ObjectConfig 元素（其内部字段自带 key 标签），与「多类型宜用 ObjectConfig」一脉相承。

**初始内容 / 默认值语义：presence 判别（key 在不在），非 emptiness** —— 空数组与「未初始化」必须可分，否则用户显式清空会被误当初始态重新 seed。判别符是 **key 是否存在**（`IPartPropertyContext` 既有语义「默认 = 字段不存在」、缺席读到 `Invalid`）：
- key 缺席（从未写）= 未初始化 → 插件读到 `Invalid` → 按需 emit N 个 element config 当 seed（其默认值即初始值；任意 seed 内容由各 element config 默认值表达，无需独立 DefaultItems 字段）。
- key 存在、值 = 空数组 `[]` = 用户显式清空 → 插件读到 count=0 → emit 空 `Elements` → 不再 seed。
- 两条承重约束：**删到空写入「存在的空数组」、绝不删 key**（任何一次删除都把 absent 翻成 present、关闭 default 通道）；**序列化保留 present-`[]`**。default 是 absent 的有效值替身（展示 / 喂 getconfig / 读取统一用），首次写即物化、从此关闭。原则同 §三.23：别把多语义压进一个值、用显式标记（这里是 key presence）区分态。

**类型不匹配 → 退回该 config 默认值**（沿既有 `DataPropertyObject.GetValue` 逻辑，**不做特殊 UI 呈现**）：类型不符时值已无意义，直接展示默认值；数据层原样保留原始 slot 值、不静默改写。

**keyed-diff 键源** —— ObjectConfig 字段按 `PropertyKey`（Id-only）；list 行**按元素身份**（`DataObjectList` 给每个 `DataPropertyValue` 稳定引用身份），非 index——故中插/删只增删一行、后续行控件原样保留（这正是 plane② 选 `DataObjectList` 喂出来的；`DataList` 存值快照无元素身份、给不出稳定行键）。重排**先不做**（做时限同类型互换位置，纯 UX、与协议无关）。

**消费者爆炸半径** —— 值模型 `PropertyValue`/`PropertyType` + 新 `PropertyArray`；live-doc 新 `DataPropertyArray` + `PropertySlot` 三臂 + `ToSlot`；序列化 `TuneLabProjectCbor` 递归读写；SDK config 面（`IControllerConfig` 去 DisplayText、新 `PropertyKey`/`ArrayConfig`/`ListConfig`/`AddableElement`、`ObjectConfig`/`AutomationConfig` 改键、叶子 config 删 DisplayText、`IVoiceEngine`/`IEffectEngine` 自动化返回改键）；面板渲染器（`PropertySideBarContentProvider` walker 扩臂 + 标签改从 `PropertyKey.DisplayText` 取）；全部声明 config 的站点（测试插件 `V1.*`）。两 SDK 预发布、无野外插件，按 release/2.0.0 不留兼容约定，破坏性换形态零 compat 负担。

**落地范围（待用户定）**：数据核心（①地板 `PropertyArray` ②live `DataPropertyArray` ③TLP 读写）先行、④两 config + 控件 + 标签改制随真实 UI 消费者（如音素重复编辑）落；还是四层一并做。④带表现层增长面，按 §三.7「零消费者不建空壳」倾向前者。

---

## 四、讨论话题清单（按依赖顺序）

每个话题独立成 session。session 开始时：

1. 阅读本文档
2. 阅读对应话题条目
3. 查阅 effect 分支引用的文件 / commit
4. 与用户讨论核心问题，达成本话题设计共识
5. 实现 + 验证 + commit
6. 把本话题的结论补充到本文档"话题进度"小节

### 1. 项目结构 & .NET 9 升级

- **目标**：决定最终的 .csproj 拆分粒度 + 是否同步升 .NET 9
- **effect 参考**：
  - commits：`升级.net9.0`、`分离Extensions程序集`、`移除不用的程序集项目`、`修改项目结构，从base程序集迁移到foundation`、`增加Foundation项目`
  - 文件：effect 分支根目录的 `.sln` 和各 `.csproj`、`Directory.Build.props`
- **核心问题**：
  - .NET 9 与 master 当前 Avalonia 是否兼容？升级风险面？
  - 程序集要拆几个？Foundation / Core / Extensions.{Format,Voice,Effect} / SDK.* / Compat.* 哪些必要、哪些可合并？
  - `Directory.Build.props` 用来统一什么（LangVersion / Nullable / TargetFramework）？
- **前置话题**：无

### 2. Foundation 抽离

- **目标**：把 `TuneLab.Base` 改造为 `TuneLab.Foundation`，定义其边界
- **effect 参考**：
  - 目录：`TuneLab.Foundation/`（DataStructures / Document / Event / Expression / Property / Science / Utils）
  - commits：`Foundation类修改`、`代码清理`、`修正命名空间`
- **核心问题**：
  - Foundation 的边界：纯通用工具？还是允许 TuneLab 语义类型？
  - `Document/` 子目录的新内容（DataPropertyObject、MultipleDataPropertyObject 等）是否合理放在 Foundation？
  - `Science/` 是否独立成程序集？
- **前置话题**：#1

### 3. TuneLab.Core 程序集

- **目标**：决定 Core 程序集是否必要、范围是什么
- **effect 参考**：
  - 目录：`TuneLab.Core/`（ControllerConfigs / DataInfo / Environment / Synthesizer）
- **核心问题**：
  - Core 与 Foundation 的边界（业务核心契约 vs 通用基础设施）是否站得住？
  - master 新增的 `EditorState`、`PlayheadPos wrap` 应归 Core 还是 Foundation？
  - `ITuneLabContext` 是 DI 入口？设计目的？
- **前置话题**：#2

### 4. DataStructures 接口规范化

- **目标**：定下 IMap / IOrderedMap / ILinkedList / IDataList 等接口体系
- **effect 参考**：
  - 目录：`TuneLab.Foundation/DataStructures/`、`TuneLab.Foundation/Document/`
  - commits：`propertyvalue优化`、`代码清理`
- **核心问题**：
  - IReadOnly* / IMutable* / 默认实现 三层划分是否过度？
  - DataList vs DataObjectList 的语义分裂是否必要？
  - LinkedList、OrderedMap 服务于什么具体场景？
- **前置话题**：#2

### 5. Property 体系升级

- **目标**：定下新的 Property/PropertyValue/DataPropertyObject 设计
- **effect 参考**：
  - 目录：`TuneLab.Foundation/Property/`、`TuneLab.Foundation/Document/DataPropertyObject.cs`
  - commits：`propertyvalue添加类型区分`、`property基本类型添加tostring`、`property增加相等性判定`、`propertyvalue优化`、`DataPropertyObject实现`、`property修改`
- **核心问题**：
  - PropertyValue 类型区分解决了什么痛点？
  - MultipleDataPropertyObject 用例（多选编辑？）
  - 与 undo/redo、序列化、UI 双向绑定如何协作？
- **前置话题**：#2、#4

### 6. 条件表达式系统

- **目标**：评估 `Expression/` 的设计目的与必要性
- **effect 参考**：
  - 目录：`TuneLab.Foundation/Expression/`、`TuneLab.SDK.Base/Expression_V1.cs`、`IExpression_V1.cs`
  - commits：`条件表达式`
- **核心问题**：
  - 引入表达式的具体用例（UI conditional visibility？属性依赖？）
  - 能否用 C# lambda 替代？引入表达式的收益是否覆盖复杂度？
  - 是否需要跨进程/跨语言序列化？
- **前置话题**：#5

### 7. SDK 分层 + 命名设计落实

- **目标**：把"clean SDK + extern alias adapter"共识落到具体 .csproj 和文件
- **effect 参考**：
  - 目录：`TuneLab.SDK.Base/`、`TuneLab.SDK.Effect/`、`TuneLab.SDK.Format/`、`TuneLab.SDK.Voice/`
  - **重要**：只看类型形状，命名全部去掉 `_V1`
- **核心问题**：
  - 公开 SDK 的接口/struct 形状清单
  - SDK 与 Foundation/Core 的引用关系；SDK 是否要自带基础类型副本
  - description.json 中如何声明 SDK 版本
- **前置话题**：#2、#3、#4、#5

### 8. Adapter 模式 + ALC 隔离评估

- **目标**：定下兼容老插件的具体机制
- **effect 参考**：
  - commits：`新的接口适配器模式`、`format接口适配器`、`voice extension加载`、`format兼容层实现`、`部分voice兼容层实现`、`兼容层实装`、`自动构建兼容层`、`老插件兼容层`
  - 目录：`ExtensionCompatibilityLayer/`
- **核心问题**：
  - ALC 隔离要不要做？做了能减多少 adapter 代码？
  - Capability pattern 如何与现有 IEffectEngine / IVoiceEngine / IFormat 结合？
  - 双向数据穿越（host ↔ 老插件）的所有权和生命周期
  - 性能基准：Properties[key]、双向集合 wrapper、enumerator 装箱
- **前置话题**：#7

### 9. 老插件兼容范围

- **目标**：决定要兼容到哪个版本、覆盖哪些功能
- **effect 参考**：
  - 目录：`ExtensionCompatibilityLayer/` 整体
  - commits：`修复插件加载时依赖缺失的问题`
- **核心问题**：
  - 野外已发布的老插件清单与版本范围？
  - format / voice / effect 三类兼容优先级（effect 是新概念，无老插件）
  - 兼容层长期维护成本 vs 回报，是否设 deprecation 时间表？
- **前置话题**：#8

### 10. description.json & 扩展加载机制

- **目标**：定下扩展包描述格式和加载流程
- **effect 参考**：
  - commits：`description.json扩展字段`、`完善format插件加载机制`、`修复插件加载时依赖缺失的问题`
  - 文件：`TuneLab/Extensions/ExtensionsManager.cs`、`ExtensionDescription.cs`、`ExtensionInfo.cs`
- **核心问题**：
  - description.json 字段清单：id / version / sdk-version / deps / entry / capabilities？
  - 加载流程：发现 → 校验 → 依赖解析 → ALC 加载 → 实例化
  - 与 master 新增的 "Extensions sidebar UI / install / uninstall" 如何衔接
- **前置话题**：#7、#8

### 11. Effect 新功能（核心需求）

- **目标**：定义 effect 插件的完整接口与运行时集成
- **effect 参考**：
  - 目录：`TuneLab.Extensions.Effect/`、`TuneLab.SDK.Effect/`、`TuneLab/Extensions/EffectManager.cs`、`TuneLab.Core/DataInfo/EffectInfo.cs`、`TuneLab.Core/Synthesizer/DirtyEvent.cs`
  - commits：`effect独立程序集`、`完善effect接口`
- **核心问题**：
  - IEffectEngine 的接口形状（process 签名、生命周期、参数访问）——按 capability pattern 拆？
  - Effect 链：每条 track 多个 effect 串联，时序、bypass、自动化如何管理
  - Dirty 事件传播（property / automation / 上游音频 dirty）
  - 与现有 voice 合成、audio part 渲染管线的接入点
  - UI：effect 列表、参数面板、preset
- **前置话题**：#5、#7、#10

### 12. 新 Property 面板 UI

- **目标**：评估 effect 分支的新 property 面板设计是否还要走
- **effect 参考**：
  - commits：`新的property面板`
  - effect 分支 `TuneLab/UI/` 下相关变更
- **核心问题**：
  - 新面板解决了旧面板做不到的什么？
  - 与 master 新加的 Settings sidebar / Switch 等 UI 改造是否冲突？
  - 是否应与 Effect 功能一起设计（effect 参数面板即 property 面板）？
- **前置话题**：#5、#11

---

## 五、master 自分叉点以来的新内容（迁移时需兼顾）

master 在 effect 分叉后新增的功能，迁移设计时不能丢：

- `EditorState` 抽取、`PlayheadPos wrap`
- Auto-save history and rotation
- Avalonia 升级 + minimal macOS 支持
- Export sidebar、多速率导出、per-track 导出
- Extensions sidebar UI + 安装/卸载
- Settings sidebar tabs
- Switch 组件
- Anchor tool on parameter panel
- Import tempos
- `TuneLabProjectCbor` 二进制格式
- 多个修复（AudioPart.Reload 线程安全、vibratoenvelope drag 等）

特别注意 **Extensions sidebar UI** 与 #10 的扩展加载机制紧密相关——新的 ExtensionsManager 需要把这套 UI 接上。

---

## 六、话题进度

> 每完成一个话题，在下面追加 **2–4 行** changelog。详细论证、备选方案、拒绝理由写在 commit message / PR 描述里，不进本文档。设计共识写进 §三 并就地更新，不堆历史。

- ✅ **#1 项目结构 & .NET 版本**（2026-05-28）— host TFM 暂留 net8；`Directory.Build.props` 仅删死变量 `AvaloniaVersion`，csproj 设置维持就地内联（理由见 §三.8）；项目结构重整推迟到对应话题（#2/#3/#7/#8）。蓝图见 §三.7，compat 单程序集理由见 §三.2。
- ✅ **#2 Foundation 抽离**（2026-05-29）— `TuneLab.Base` 冻结，新建 `TuneLab.Foundation`（fork + 改名 + 文件夹重组 `Data`/`Structures`/`Properties`→`Document`/`DataStructures`/`Property`，namespace 跟随）；主程序 + 内建扩展改引 Foundation，旧 Base.dll 留给 #8/compat；`IValueController`/`IDataValueController` 移至 `TuneLab/GUI/Controllers`；顺手修 `NotifiableProperty` 装箱（`EqualityComparer<T>.Default`）与 `ValueCommited`→`ValueCommitted` 拼写。DataStructures/Property/Expression 的**内部接口重写**与值-Config-Controller 终态拆分留 #4/#5/#6。边界与命名宪章见 §三.9，TFM 仍 net8，构建 0 错误。
- ✅ **#4 DataStructures 接口规范化**（2026-06-01）— 纯决策，不改代码。集合接口按 **ABI 边界二分**：**map 家族整体**（`IReadOnlyKeyValuePair`/`IReadOnlyMap`/`IMap`/`IReadOnlyOrderedMap`/`IOrderedMap` + 具体 `Map`/`OrderedMap`）→ **Primitives 冻结**（出现在 `Properties`/`ObjectConfig`/`AutomationConfig`/`VoiceInfos`/`SynthesizedAutomations` 等插件契约，且数据须可 `new`，整族被契约+构造拽入）；**LinkedList 族**（`INote`/`IPart` 侵入链）+ `IMutableList` + wrapper 扩展 + **整个 Document 框架** → **Foundation 富实现**（永不跨边界、自由演进）。三层只在边界值得；`DataList`/`DataObjectList` 分裂必要但属 Foundation 内部。否决 effect 在 `SDK.Base` 重拷 `*_V1`。规范化形状（KeyValuePair 改名、删 `At` 递归 bug、`[CollectionBuilder]`）与物理落地 Primitives 留 **#7**；Document 终态留 **#5**。详见 §三.11。
- ✅ **#5 Property 体系升级**（2026-06-01）— 纯决策，不改代码（落地随 #7）。Property 三段按 ABI 边界归位：**值模型**（`PropertyValue` JSON 树 + `IPrimitiveValue` 标记接口）→ **Primitives**（Foundation+SDK+序列化共用，过双方准入）；**通用控件 Config 家族**（`IControllerConfig`+Slider/CheckBox/ComboBox/TextBox/Object）→ **SDK.Base**（Foundation 不引用、不过 Primitives 准入，且为表现增长面，**修订 §三.7/§三.10**——原 ControllerConfigs→Primitives 改为 →SDK.Base）；域专属 `AutomationConfig`→`SDK.Voice`；**live-doc**（`DataPropertyObject` 族）→ Foundation。Config **按 UI 控件命名**（采纳 effect，否决 Base 值类型命名）；值模型用**单一 `PropertyValue` box + `IPrimitiveValue` 标记**（否决 effect 标量/树双 box，combo/config 用具体类型重载拿编译期保证）。值模型须补齐 effect 漏掉的相等性/ToString/数组走冻结集合/命名清理。live-doc 内部形状（`IDataPropertyObjectField`/`MultipleDataPropertyObject`/`PropertyPath`、property 数组 DataList vs DataObjectList）**按需求随 #11/#12 定**。详见 §三.12。
- ✅ **#6 条件表达式系统**（2026-06-01）— 纯决策，不改代码。**功能保留**："选不同值→面板展示不同控件/字段"确认要做；#6 只否决 effect 那份半成品响应式 DSL 现在锁进冻结 ABI。`IExpression<T>`（响应式计算单元 + If-ElseIf-Else 组合子）意图用例是条件属性面板（`ConditionConfig`/`IControllerConfig.When`）。三问：不需也不可跨进程/跨语言序列化（持活委托+事件订阅）；可用 host 侧 `Func<…,bool>` 谓词替代；effect 里**零调用点、半成品**（host 消费未接通、引擎有 bug、用例未提交）。**决策：丢弃 effect 实现，当前不进任何层；功能随 #12 连同 Config 家族设计落地**（条件 config 叠在 SDK.Base Config 家族之上，书写形状依赖面板设计；现在冻结违不提前建空壳+内核增长纪律；非数据值→非 Primitives；`_V1` 即已否决的重拷）。落地倾向 B 内核（host 求值、小 ABI）+ 薄 `If/ElseIf/Else` 糖，永不进 Primitives。详见 §三.13。
- ✅ **#3 TuneLab.Core 程序集**（2026-05-29）— 结论 **不建 Core**（effect 的 Core 是 grab-bag，内容各归位）；确立 **数据/服务分家**（数据=具体类型直接 `new`，服务=接口 host 注入）；新增中性冻结内核 **`TuneLab.Primitives`**（`Foundation` 与 `SDK.*` 共同引用，因生命周期不同而独立于 Foundation，`internal`/IVT 不能替代）。边界类型 `Point`/`MonoAudio`/Property值模型/ControllerConfigs→`Primitives`、`DataInfo`→`SDK.Format`、服务接口→`SDK.Base`；`ILog`/`ITuneLabContext` 进 `SDK.Base` 注入式（弃 `static Global`，因 ALC 下每-ALC 一份）；命名用诚实 namespace + `global using` 别名；冻结类型经"整代版本化 + 归档 dll + `extern alias` compat"安全演进。详见 §三.10，蓝图 §三.7 已更新（去 Core、加 Primitives）。落地留 #7/#8，本话题不改代码。
- ✅ **#7 SDK 分层 + 命名落实**（2026-06-01）— **首个改代码的落地话题**，全解 Debug/Release **0 编译错误**。新建 `TuneLab.Primitives`（零依赖卫生卡口已验证）+ `SDK.{Base,Format,Voice,Effect}`（均 net8 ABI 地板、设置内联）；按 §三.10/§三.11/§三.12 搬类型：map 家族+`Point`+`MonoAudio`+`PropertyValue`/`PropertyObject` 值模型→Primitives，Config 家族（按 UI 控件改名 `Slider/CheckBox/TextBox/ComboBox/Object` + 族根 `IControllerConfig`）+ `ILog`/`ITuneLabContext`→SDK.Base，`DataInfo`+format 契约→SDK.Format，voice 契约+`AutomationConfig`→SDK.Voice，SDK.Effect 占位留 #11。map 家族**冻结前一次规范到对称完整**：改名 `IReadOnlyKeyValuePair`/`ReadOnlyKeyValuePair`（后者改不可变）、删 `At` 自递归 bug、补可变 `IMap`/`IOrderedMap`、`IOrderedMap` 补 `RemoveAt`(对称 `Insert`)、Keys/Values 收紧为 `IReadOnlyCollection`/有序 `IReadOnlyList`（Foundation 补对称 `ReadOnlyCollectionWrapper`）；值模型加 `PropertyType`+`PropertyNull.Shared`+深相等性。`Foundation` 加 →Primitives 边，主程序改引 Primitives+4×SDK，plural `Extensions.Formats`/`Voices` 移出 .sln，churn 逐文件 `using` 改写。**裁剪/推迟**（§三.14 在案）：仅契约层（内建实现留主程序）、PropertyValue 全树重构（包装类型+`IPrimitiveValue`+数组）推迟 #12、`Invalid` 留转发 shim、`MonoAudio`/服务接口暂无消费者。详见 §三.14。
- ✅ **#9 老插件兼容范围**（2026-06-02）— 纯决策，不改代码。**版本**：Legacy 三程序集从未设 `<Version>` → 野外绑定身份一律 `1.0.0.0`（未签名）；v1.0.0→v1.6.0 公共面是**纯加性 DTO 增长**（唯一反向 `NoteInfo.Lyric` required→可选无二进制破坏）→ `Compat.Legacy` 冻结 **master/v1.6.0 超集一份**、三程序集全钉 `AssemblyVersion=1.0.0.0`。**冻结源取自 master**（`using TuneLab.Base.Structures`），**非** effect-migration 磁盘副本（已被 #2 Foundation 改名污染）。**范围**：全 1.x 社区插件**尽力而为**+ 加载失败优雅降级，权威清单留 #10 实测补；内置格式（ACEP/Midi/TLP/UFData/VPR）走新 SDK 不经 compat。**优先级**：effect 无老插件；format/voice **并行**——Format 可立即完整落地（effect 参考 `FormatConverter` 224 行已全实现），**Voice 适配器填实被 #11 接口冻结门控**（如实记录依赖张力）。**collectible 触发**：voice 引擎确捆绑冲突原生依赖（ONNX）→ 坐实 per-plugin ALC 必要，但冲突由非 collectible ALC 已根除、**不触发 collectible**；触发条件 = 要免重启卸载 UX（当前重启式 `ExtensionInstaller.exe` 是 fallback）→ collectible 维持留 #10。**维护**：Legacy 源码隔离冻结成本低，**不设 deprecation 时间表、长期维护**。详见 §三.16。
- ✅ **#10 description.json & 扩展加载机制**（2026-06-02）— **改代码话题**，Debug/Release **0 错误**。落地新版加载器：**代际判定看 `id` 有无**（V1 必带 id；讨论中由 `sdk-version` 改定，因资源包无 sdk-version——id 通用于代码+资源包）；**物理键仍是文件夹**、id 为 V1 逻辑标识、Legacy 不造假 id、注册键来自 attribute。**插件单位 = 文件夹（包）= ALC = 卸载原子单位**，一包可多插件（`extensions[]` 保留每插件元数据）共享同 ALC/基建，单插件简写经 `EffectiveExtensions` 归一化。新增 `ExtensionInfo`/`ExtensionDescription`（加 `id`/`sdk-version`/`extensions`）、`PluginLoadContext`（per-folder ALC + 契约共享 + `isCollectible` 预留）、`ExtensionLoadResult`；`ExtensionManager` 重写为统一管线（发现→判代际→校验→V1 选择性 ALC 加载/Legacy `LegacyLoadHook`+盲扫 fallback）优雅降级；Format/Voice manager 改 `RegisterFromTypes`；sidebar 接真实 `LoadResults`、删 `DetectExtensionType`。**推迟**：collectible 热卸载（已 ready，维持重启式）、effect（识别但待 #11）、Compat.Legacy 实装（`LegacyLoadHook` 是接入点，留 #9 尾）、context 注入（待 #11）。产出开发者指南 + AI 参考两份文档。详见 §三.17。
- ✅ **#8 Adapter 模式 + ALC 隔离评估**（2026-06-02）— 纯决策，不改代码（Compat 代码留 #9/#10/#11）。**命名分代**：Legacy（现 master 老 SDK，野外插件链接）/ V1（#7 新 SDK）/ V2；兼容层 `Compat.<被桥接代>`（当下 `Hosting.Compat.Legacy`）。**extern alias** 仅消歧同名不同版 → Legacy→V1 名字不同**无需 alias**，首次用在 V1→V2。**跨 .NET 升级**靠 TFM ABI 地板 + roll-forward（host 升级不重编 SDK、插件不重编），非 ALC。**ALC 加载模型**：永远 per-plugin ALC + **共享契约**（Primitives+SDK.* 走 Default 共享、插件私有依赖进各自 ALC）+ **非 collectible 起步**（隔离好处全得、无泄漏/性能税）；collectible 热卸载留 #10（触发=卸载即时生效），靠"事件 IDisposable 退订 + 插件实例单点持有"不变量保证升级为**加性**。ALC **不**减 adapter 代码、**不**提供崩溃隔离（纠正 §三.5）。**老 SDK 留源码隔离冻结**（修订 §三.3：独立 sln + 钉死版本/TFM + 禁改标注）。**Capability** 与 compat 正交（compat 为 Legacy 合成能力面）。**双向穿越**：DTO eager 深拷贝、热缓冲零拷贝共享、note 包装身份保持缓存、live doc 永不跨界、事件适配器 IDisposable。**性能实测**：wrapper/装箱全落冷设置路径可忽略，`Properties[key]` eager 转换零分配（37ns/0B）；规则=边界 eager 转换不给 lazy wrapper。详见 §三.15。
- ✅ **#9 尾 Compat.Legacy 实装**（2026-06-02）— **改代码话题**，`TuneLab.sln` + `legacy/Legacy.slnx`（均 Debug/Release）**0 错误**，编译期隔离经探针验证（主程序 `using TuneLab.Base` 报 CS0234）。**Legacy 冻结**：三程序集 `git mv` 进 `legacy/sdk/src/`，内联钉 `AssemblyVersion=1.0.0.0` + 冻结禁改头 + 空 `Directory.Build.props` 截断主仓继承；从 `TuneLab.sln` 移除、自成 `legacy/Legacy.slnx`。**引用策略=反射加载零编译依赖（用户定，强于原设想）**：主程序无任何 ProjectReference，`LegacyCompatLoader.Wire()` 反射 `LoadFrom` Compat.dll 取 `LegacyCompatEntry.TryLoad`、注入注册委托（参数全是共享契约类型，跨 Default ALC 同一 Type）装上 `LegacyLoadHook`；注册反转（host 实现委托转发 `FormatsManager.RegisterImporter`/`Exporter`、`VoicesManager.RegisterEngine`）；MSBuild target 构建 Compat + 拷产物 dll 进输出。**Format + Voice 真正并行全实装**（采纳 §三.16 #10 修订，Voice 不门控 #11）：`LegacyPluginLoadContext`（per-plugin ALC + 共享三冻结+SDK 契约）；`FormatConverter` 全字段双向深拷贝；Voice 全套适配器 + **note 身份缓存**（phonemes 键映射回宿主）+ **`SynthesisTaskAdapter : IDisposable`** 事件桥退订（宿主 `SynthesisPiece.Dispose` 触发，落实 §三.15 不变量）+ audio `float[]` 零拷贝；`Segment<T>` 泛型保留、适配器用下标回查**零强制转换**（SDK 重设计另案）；Property/Config/Point 边界转换 1:1。全程优雅降级。**前瞻**：多版本 compat 定为**直桥当前代不链式**（Voice 活 wrapper 链式有 per-access 成本），加载器统一参数化、仅适配器按代写，宿主单发现管线 + 代→Compat 注册表（今 `LegacyLoadHook` 为退化特例）。**测试与硬化（2026-06-03）**：产出 `tests/` 测试插件套件（13 包 + 用例 + .tlx 脚本 + 样例文件），**全部用例通过**，真实野外插件（ChoristaUtau 等）端到端验证；真机暴露并修复 7 处（加载顺序竞态[关键]、批量安装坏包崩溃、Compat 诊断日志、侧边栏状态徽标、Skipped 原因、待卸载可撤销、重装对话框列名）。详见 §三.18。
- ✅ **#11 Effect 新功能**（2026-06-03）— **改代码话题**（effect 分支的核心需求），`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx` 均 **0 错误**；零 effect 既有工程行为不变。先做设计讨论达成共识：effect = **耗时长的离线整段音频变换**（如 SVC 换声），**非实时 VST**（后者留后续）→ 任务式异步 + 整段 `MonoAudio` 进出。**SDK.Effect**（最小冻结面）：`IEffectEngine`/`IEffectSynthesisInput`(`MonoAudio`+`PropertyObject`+`TryGetAutomation`)/`IEffectSynthesisOutput`(音频 sink，塌缩、不再 `: ISynthesisOutput`)/`IEffectSynthesisTask`(一次性 Complete/Progress/Error)+`[EffectEngine]`；**dirty/缓存全归宿主、SDK 不含 DirtyEvent**，输出仅音频（pitch/automation 回写推迟）。**`IAutomationValueGetter`+`AutomationConfig` 从 SDK.Voice 搬 SDK.Base**（voice/effect 共用，修订 §三.12；V1 无野外插件 ABI 零破坏）。**数据模型**纯加性：`EffectInfo`→SDK.Format、`MidiPartInfo.Effects`、host `IEffect`/`Effect`（挂 MidiPart、`IsEnabled`=bypass、复用 `Automation`）、`mEffects: DataObjectList<IEffect>` 有序链。**EffectManager** 镜像 VoicesManager（attribute 发现 + 惰性 Init），接入 `ExtensionManager`（删"effect 暂不支持"）。**合成接入** SynthesisPiece：voice 完成后按 **piece 独立**串行过链（context 缩减、连续性归 voice 分片），**每级缓存 + 仅下游失效**（避免重跑昂贵 SVC），bypass/缺失/出错=passthrough，generation 防竞态；不动 voice SDK/AudioGraph。**最小 UI**：属性侧栏 Effects 面板（`EffectsController`，增删/上下移/bypass + Add 菜单）+ 参数面板复用 `ObjectController`。**推迟**：per-effect 时间轴自动化编辑 UI（模型已支持）、preset、面板重设计。开发文档 [plugin-development.md](plugin-development.md) §6 + AI 参考 effect 接口清单已补。详见 §三.19。
- ✅ **查漏补缺：DataObject / 事件模型重构**（2026-06-04，#12 后）— **改代码**，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**。先讨论达成共识（§三.21）再落地，引进另一仓库 C++ `DataObject` 新实现的理念、不引进 Qt 机制。**事件**：新增 `IModifiedEvent`（单一事件、无参 Subscribe(Action)=结果态 + 带 `Subscribe(Action<bool>)`=全量携 `canIgnore`；仅继承 `IActionEvent` 避免 When/Any 推断歧义）+ `ModifiedEvent : ActionEvent`（复用 settled 通道、零重复多播/分配，只加 bool 通道）；数据对象 `Modified`/`ListModified`/`MapModified` 改 `IModifiedEvent`、退出 `IMergableEvent`；合并入口归数据对象（`MergeNotify()` using 作用域 + 公开 `BeginMergeNotify`/`EndMergeNotify`），`.ListModified.BeginMerge()` 调用点迁 `.BeginMergeNotify()`；**`IMergableEvent`/`MergableEvent` 保留**（自合并独立事件，真实消费者=选择变更 `ISelectableCollection.SelectionChanged`，修订 §三.21 的"零消费者"误判）。**核心**：`IDataObject` 改纯契约、状态与算法移入 `DataObject` 抽象基类（退役嵌套 `Implementation`、消除 `((IDataObject)this)` 回转 cast、parent 存 `DataObject?`）；**`GetInfo()`/`SetInfo(T)` 对称契约 + `Set` 交互式分层**（对齐 C++ `info()/setInfo()`）：`IDataObject<T>.SetInfo`=纯应用无副作用（复合扇出/装载走它）、`IDataProperty<T>.Set`=交互式带副作用（用户编辑走它，类型上复合无 `.Set`）；叶子读/写原语为方法 `GetInfo()`(读)/`SetValue(T)`(裸写),`Value` 是非虚只读 getter `=> GetInfo()`(只 getter、无 setter);契约 `SetInfo`(去重+命令,经 SetValue 落值)/交互 `Set`(默认=SetInfo、可覆写)在基类。多态落在方法(`BPM`/`DataPropertyValue` 覆写 `SetValue`、`DataLyric` 覆写 `Set`),不虚化 `Value`；业务/UI 统一走 `Set`(透明副作用)、纯 `SetInfo` 只被复合扇出/装载使用。**删 `protected static SetInfo` 扇出通路**（124 处 `IDataObject<X>.SetInfo(child,…)` → `child.SetInfo(…)`）；`IDataObjectExtension.SetInfo` 扩展替代退役的 `.Set` 扩展。**复合节点二分**：可分解（Note/Tempo/Phoneme/Track/Project/MidiPart/Effect/AudioPart/Automation/Vibrato/TempoManager/TimeSignatureManager/PiecewiseCurve…）= `using MergeNotify()` + 逐子 `child.SetInfo` 扇出、无复合命令；**原子复合**（Voice，普通字段无子数据对象）= `SetInfo` 自持复合 `ModifyCommand` + 私有裸写 `WriteInfo`。容器 `SetInfo` 统一为 merge+Clear+AddRange（走推命令方法，构造期无 document 命令自然丢弃）；删 `DataObjectList`/`DataObjectMap` 死字段 `mListModified`/`mMapModified`；修 `AnyEvent.OnRemove` 订阅/退订 bug；删投机 `FromGet`/`ToSet` 与未用泛型 Wrapper，三字段适配器合一 `PropertyField<T>`。**已解**：扇出走 `child.SetInfo`（纯）根除了"装载触发 `DataLyric` 清音素"的副作用风险（SetInfo 纯/Set 交互二分）。**风险/待核（真机验证）**：复合原子性从"单复合命令"改为"per-leaf 命令 + Commit 打包"（原子复合 Voice 例外；须核对依赖单复合命令粒度处）；构造/装载期 `child.SetInfo` 创建并丢弃命令（冷路径分配）。详见 §三.21。**真机验证待用户进行**。
- ✅ **#12 新 Property 面板 UI + effect 自动化接入**（2026-06-04）— **改代码话题**（effect 分支最后一个编号话题），分两提交，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx` 均 **0 错误**。先设计讨论达成共识。**提交① 面板架构**：属性面板由 path-routing（PropertyPath 下发值+事件上抛+外部手工写回/多选哨兵）改为 **live-bind**（`IDataPropertyObject` 桥 + 字段适配器，控件逐字段 `BindDataProperty` 复用单属性撤销/刷新/提交机器）；`PropertyObjectController` 控件注册表 + 嵌套用 `PropertyPath` 寻址（不合成嵌套对象）；voice note/part 属性 + effect 参数面板自动并入，删旧 `ObjectController`；`MultipleDataPropertyObject` 多选合一（**合并修改事件**修正"只听单对象→中途 Invalid 卡住"，撤销根委托首对象、同文档归一撤销单元）；自动化默认值保留专用控件（merge-dirty + 按需 AddAutomation，**功能优先于 bind 糖**）。**提交② effect 自动化**：`AutomationKey`（来源+plain id）类型路由（**否决伪造字符串前缀**防撞名，不持久化），`IMidiPart` 加路由扩展分派 voice/effect；voice 与各 effect 自动化平等汇入底部参数栏 + 右侧栏默认值（分组分隔符 + effect 名表头，**点眼睛叠加/toggle 编辑**统一交互）；effect 无颤音→active 为 effect 时跳颤音段；渲染器补订阅 `effect.Automations`（修拖动不重绘）。**推迟**（均论证安全后补）：条件表达式（纯增量可后补——config 加可选 Func 谓词/DIM、host 求值、不进 Primitives）、PropertyValue 全树重构（ComboBox 维持 string、单 box、零消费者）、dataobject 集合接口/DIM 重构（Foundation 内部正交、零 ABI 风险）、effect preset、颤音影响 effect 参数。测试 effect 插件 TLTestGain 增 gain_env 自动化轨。**真机验证由用户进行中**。详见 §三.20。
- ✅ **查漏补缺：IEvent 事件框架**（2026-06-04，#12 后）— **改代码**，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**；纯结构重构、行为保持（真机验证待用户）。先讨论达成共识（§三.22）再落地。**形状决策**：保留委托形状 `IEvent<Action<…>>`、**不上 Rx 值形状**，立"push 通知 / pull 值"原则（与 §三.13 否决反应式值图同源）；理由=只用 When/WhenAny/Where 三个领域重接器、委托形状多元+热路径零分配、上 Rx 会破 §三.21 的 `IModifiedEvent` 零迁移。**WhenAny 单一原语**：原 `Any` 三份拷贝（List/Map/LinkedList 各嵌"活订阅矩阵"）收敛为单一基接口 `IReadOnlyDataCollection<out T>`（`ItemAdded`/`ItemRemoved`/`Items`）+ 唯一 `AnyEvent` 扩展，List/LinkedList 直接继承基（单一来源无钻石）、Map 另持一元投影喂 `IReadOnlyDataCollection<TValue>`；`Any`→`WhenAny`（消 LINQ 歧义）；**修 `OnRemove` 误用 Subscribe 的退订泄漏**。**Where（响应式过滤）一并迁**（与 §三.13 SDK Config 条件系统不同层），**修谓词订阅泄漏**（留存 handler 真正退订）。**Holder 命名**：`IProvider`/`Owner`→`IHolder`/`Holder`、事件 `ObjectWillChange`/`ObjectChanged`→`WillModify`/`Modified`、`Object`→`Value`；`When` 挂 `IHolder`、与 `WhenAny`（基座不同）**不强并**。`INotifiableProperty` 保留仅统一词汇。**推迟**（无消费者+加性可补）：`ISource`/`IChangeNotify` 统一根、`IDataObject` 补 `WillModify`（C++ 有对称 `aboutToModify`）、dataobject 并入根（撞属性不变性疣）——使能消费者预计是 live-bind 出现瞬态字段。详见 §三.22。
- ✅ **查漏补缺：属性面板 multiple/invalid 三态呈现**（2026-06-05，#12 后）— **改代码**，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**；真机已验证。先讨论达成共识（§三.23）再落地。**问题**：live-bind 后 `PropertyField<T>` 把空哨兵 coerce 成默认值，多选"多值"呈现断了（`DisplayMultiple` 沦为死代码），且 `Null`/`Multiple` 共用一哨兵不可分。**三态**（叶子值层、沿 JSON 树递归）：Concrete/Multiple（多选不全等）/Invalid（无选中），**前端+插件两轴都区分**（插件诉求来自条件谓词需知宿主真实态；不开放 JSON null 作合法值——与 Invalid 撞车）。**机制**：Primitives 增 `PropertyMultiple` 哨兵 + `PropertyValue.Multiple`/`IsMultiple()` + `PropertyType.Multiple`（**反转 §三.14"清理 Invalid"为"正式化 Invalid + 增补 Multiple"**；哨兵瞬态永不序列化，非 PropertyValue 全树重构仍推迟）；`MultipleDataPropertyObject.GetValue` 三态返回 + 容空列表，字段经新增 `IRawValueProperty` 暴露未 coerce 原始值、绑定 `Refresh()` 据此分派 `Display`/`DisplayMultiple`/`DisplayNull`（不往单/多共用契约塞"多选"方法）。**控件呈现**：CheckBox 高亮底+dash/空框；TextBox watermark `(Multiple)`/空；ComboBox placeholder `(Multiple)`/空；Slider 空轨+标签 `-`/空；无选中绑空源在遮罩下呈 Invalid。**真机暴露并修复相邻缺陷**：① 扇出逐对象刷新致中间态闪烁/文本框光标跳 → `SetValue` 包 merge + `TextInput` 聚焦中不被刷新覆盖；② CheckBox 共享图标残留 + 颜色淡出动画把 √ 画出 → 仅进入勾选态设 √、取消勾选不动图标；③ Slider thumb 初次选中跳动 = 首帧 Bounds 滞后三侧面（bind 先于 add / `finalSize` 算端点 / `Piovt` 用 `DesiredSize`）。测试 voice 增四控件+嵌套 ObjectConfig，独立文档 `tests/PROPERTY-TRISTATE-TEST-CASES.md`。详见 §三.23。
- ✅ **查漏补缺：干掉 PropertyPath，改导航式数据模型**（2026-06-05，#12 后）— **改代码**，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）均 **0 错误**；真机已验证。先讨论达成共识（§三.24）再落地，落地 effect 原始意图：路径上每个节点都是 `IDataPropertyObject`（对象）或 `IDataProperty<T>`（叶子），一视同仁。**接口**：`IDataPropertyObject : IDataObject`（节点即撤销根，折进 `DataRoot`，方案 A）+ `Object(string)` 导航 + 单层 `string` 版 `GetValue/SetValue`；删 `PropertyPath` 类型；叶子保留 typed 三件套（否决泛型 `Field<T>`——type 固定、泛型把传错类型降级成运行期）。**单选**：`Object(key)` 返回懒视图 `ObjectView`（读经 internal `FindObject` 返默认不创建、写经 `GetOrCreateObject` 按需建路径，保住浏览不污染序列化/bind 不记假撤销），internal `ILazyObjectNode` 互链不进公开接口。**多选**（effect 没写完处）：`MultipleDataPropertyObject` 改持 `IReadOnlyList<IDataPropertyObject>`，`Object(key)` 复合各成员 `Object(key)` 递归——**缺该嵌套的成员经懒视图读默认值仍正确参与三态比较**（不误判全等）；`MultiDataRoot` 折进本体。**连带**：删 `PropertyModified`（`IActionEvent<PropertyPath>`）及 `OnAdd/OnRemove` 路径簿记，note dirty 改订 `Properties.Modified`（本就冒泡）；三态机器（`IRawValueProperty`+`Refresh()`）模型无关、一行未改。**array 押后**（零消费者+形态未定，倾向 `DataObjectList`，接口前向兼容加性补）。测试 `V1.Suite.Voice` 加深到 3 层 `vibrato→lfo→range`，独立文档 `tests/PROPERTY-NAVIGATION-TEST-CASES.md`。一并修复（非本话题、相邻）：tlx 安装弹窗支持多选、测试正弦合成器加 attack/release 消爆音、侧栏底部加跟随视口高的透明 spacer（折叠面板展开/收起不撞底/不猛弹）。详见 §三.24。
- ✅ **查漏补缺：条件属性面板（config = f(context)）**（2026-06-07，#12 后）— **改代码**，`TuneLab.sln`（Debug/Release）+ `legacy/Legacy.slnx`（Release）+ 测试插件 sln 均 **0 错误**，真机验证待用户。voice 插件交纯函数 `GetNoteConfig`/`GetPartConfig(IPropertyContext)` 取代静态 `NoteProperties`/`PartProperties`（**DIM 默认回退静态 → 旧插件 / Legacy 零改动**）；宿主在属性 **commit**（结果态通道，中间拖动态 `canIgnore` 不触发）按当前值重算整棵 `ObjectConfig`、**keyed-diff** 到控件树（同 key 同类型复用控件仅更参数、纯参数变不重排、仅结构变才重排）；part→note **单向沿链**重算；多选喂**三态合并快照**（`Multiple` 哨兵插件侧安全降级）；**默认 = 字段不存在**。**砍记忆化**（commit 语义下必 miss、净开销）；**context 统一推迟**（待 voice API 整体改造时并入合成 context）。`PropertyObjectController` 重构为 reconcile（保留静态 `SetConfig` 兼容 effect/嵌套）。测试声库 `[v1-suite] Conditional`（同包新增、不动基线）+ 独立文档 `tests/PROPERTY-CONDITIONAL-TEST-CASES.md`。详见 §三.25。
