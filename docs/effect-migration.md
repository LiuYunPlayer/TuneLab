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

适配代码在**独立 .csproj**（如 `TuneLab.Hosting.Compat.V1`），不与主程序混合。

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

**Compat 程序集划分粒度**：按**版本**分（`Hosting.Compat.V1`、未来 `Hosting.Compat.V2`），**不**按域（Format / Voice / Effect）分。理由：`MonoAudio` 等基础桥接类型在多域适配间共享，按域分必然挤出 `Compat.V1.Common` 第三方项目或重复代码；compat 层的真实生命周期是"整代下线"，按域独立 deprecation 没有实际需要。

### 3. 老 SDK 只保留 dll 归档，不留源码

```
legacy/sdk/v1/TuneLab.SDK.Effect.dll   ← 归档的发布产物
                                       ← 防止有人改它
```

### 4. 主程序的引用边界

`TuneLab.csproj` 只引用：
- 当前 SDK 程序集
- 适配层程序集（通过其暴露的工厂接口，返回的是**当前版**类型）

主程序代码的"语言世界"里，老 SDK 类型根本不存在。

### 5. 优先考虑 ALC 隔离与 Capability Pattern

未做最终决策，但讨论时倾向支持：

- **ALC（AssemblyLoadContext）隔离**：每个插件独立 ALC，自带其编译时的 SDK dll，可减少 adapter 代码量、提供崩溃隔离与热插拔
- **Capability Pattern（参考 CLAP）**：核心接口极小 `IPlugin { object? GetExtension(string id); }`，能力为独立小接口加字符串 ID（如 `"effect.processing/1.0"`）。加新能力不需要出 SDK 大版本

这两个机制将在话题 #8 中具体落实。

### 6. 性能考量

Adapter 对**冷路径**（Format I/O、property panel）开销可忽略。

需在 #8 中基准测试的两个点：
1. Effect process 循环里 `Properties[key]` 的 wrapper 分配
2. 双向回传集合 wrapper 的开销

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
| `TuneLab.Hosting.Compat.V1` | host TFM | #8 | 老插件适配（单程序集，见 §三.2） |
| `ExtensionInstaller` | net8 | 已存在 | 独立小工具 |

> 原蓝图曾列 `TuneLab.Core`（host-TFM 业务核心契约）；#3 决定**暂不建**（理由见 §三.10），表中已移除。host 业务契约待 #4 活文档模型 / command-undo 出现时再评估。

**TFM 策略**：

- **host TFM** = 主程序运行时；目前 net8，未来独立 PR 升级。结构重构与 TFM 升级**永远分开**。
- **ABI 地板** = `Primitives` + SDK.\* 系列锁在 host TFM 之下的一个 LTS（首次引入时锁 net8）。插件按此地板编译，host 升级到新 TFM 不破坏老插件加载。

**命名约定**：

- 公开类型**不带** `_V1` 版本后缀（见 §三.1）
- `Extensions.*` 和 `SDK.*` 都用**单数**（`Format` / `Voice` / `Effect`），与 `Microsoft.Extensions.*` 主流惯例一致
- 老 `Extensions.Formats` / `Extensions.Voices` 复数程序集**作为归档 dll** 保留在 `legacy/sdk/v1/`，供 compat 层 `extern alias` 引用
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
- `ILog` / `ITuneLabContext` 接口进 `SDK.Base`，host 加载插件时**注入每插件作用域**的实例（`context.Logger` 自动打插件 id 前缀、转发进 host 现有 `ILogger` sink）。
- **弃用 `static TuneLabContext.Global`**：服务定位器反模式，且 **ALC 隔离下静态是每-ALC 一份，全局静态根本共享不了**（恰在多版本场景失效）。host 自留 Foundation 里便捷的静态 `Log.*`（host 单 ALC 无虞），与插件 `ILog` 共用同一底层 sink。

**内核增长纪律**：每纳入一个类型 = 永久 ABI 承诺。内核只在具体插件 API 真需要时**克制、刻意**地增长；优先在 SDK.Base 暴露**冻结接口**、富实现留 Foundation，而非把富类型整个下沉。

**冻结类型的演进（安全路径，落地留 #7/#8）**：
- 非破坏改动（class 加成员、struct 加方法、服务接口用 DIM 加方法）**就地免费**，不升版本。
- 破坏性改动走**整代版本化**：ABI 地板（`Primitives` + `SDK.*`）按"代"统一升版本，旧代编译产物**归档为 dll**（§三.3，不留源码），`Hosting.Compat.V1` 用 `extern alias` 桥接整代（§三.2，单程序集按版本分）。同版本内零转换；跨版本仅在边界转换，热载荷（`Point[]`、audio `float[]`）可 `MemoryMarshal.Cast` 零拷贝或共享数组引用。主程序绝不引用旧代（§三.4）。

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
| 域专属 Config | `AutomationConfig`（name/min/max/color） | 对应 `SDK.*`（voice） | master 本就在 voice SDK；voice 概念，非中性 |
| live-doc | `DataPropertyObject`/`DataPropertyValue`/`DataPropertyArray` 等 | **Foundation** | host-only undo/attach 基础设施；插件只见 `PropertyObject` 快照，永不拿 live 树 |
| controllers | `IValueController`/`IDataValueController` | **UI**（#2 已搬） | 值编辑 UI 绑定 |

**Config 命名按 UI 控件，不按值类型**：同一值类型可对应多种控件（int 可用 TextBox 也可用 Slider），不同控件的可配参数不同（TextBox 有 `IsSingleLine`、ComboBox 有 display-text、Slider 有 min/max）。控件命名诚实承载"用哪个控件 + 配什么"的作者意图，是 TuneLab 一贯暴露给作者的模型。采纳 effect 的 `SliderConfig`/`CheckBoxConfig`/`ComboBoxConfig`/`TextBoxConfig`，**否决** Base 的值类型命名（`NumberConfig`…）。对现存第三方插件是破坏性改名，由整代版本化 + compat 层（§三.2）吸收。

**值模型用单一 box，不做标量/树双 box**：effect 曾分裂 `PrimitiveValue`（标量）vs `PropertyValue`（树）各带 readonly 变体——分裂的收益面窄（仅 combo 选项 / config 默认值的编译期"只收标量"保证），成本面宽（冻结表面翻倍 + 标量在两 box 间永久 up-cast 噪音，违 §三.10 内核增长纪律）。**折中**：只保留单一 `PropertyValue` box + 轻量 `IPrimitiveValue` 标记接口（标记 `PropertyBoolean/Number/String`）；combo/config 入口用**具体类型重载**（`ComboBoxOption(bool/double/string)`）拿编译期保证，无需第二个 box。读写方向仍按 §三.11 边界二分保留 `IPropertyValue`/`IReadOnlyPropertyValue`（服务回只读、插件填可变）。

**值模型形状**（采纳 effect 结构，但 effect 代码是半成品，#7 落地时补齐）：JSON 树 + `PropertyType` 枚举 + `PropertyNull.Shared` 哨兵（替代旧 `Invalid`）。**必须补齐** effect 漏掉/注释掉的：① 值相等性（`property增加相等性判定` 意图——undo 去重 `IDataObject<T>.Set` 的 `Equals(before,info)` 依赖它，类型 + 深比较）；② `ToString`；③ 数组走 §三.11 冻结集合接口而非裸 `IList<>`；④ 命名清理（统一 `UnBox`，删 `PrimitiveValue`/`UnBoxing` 不一致与残留 `PropertyValue_V1Extensions`）。

**live-doc 内部形状按未来需求定，本话题只锁分区不锁内部**：`DataPropertyObject` 族归 Foundation 已锁；但 effect 里 `IDataPropertyObjectField`（类型化可绑定字段，UI 双向绑定的桥）、`MultipleDataPropertyObject`（多选编辑）、`PropertyPath`（嵌套寻址 vs `GetField` 导航）三项 effect **全是 `NotImplementedException`**，其去留与终态随 #11(effect 参数)/#12(属性面板) 的具体需求落地时定。**对 §三.11 #4 尾的呼应**：property 数组用 `DataList`（值快照、整列替换）还是 `DataObjectList`（逐元素 attach/undo）同属此 live-doc 内部形状，一并随需求定。

**物理落地（值模型搬 Primitives、Config 搬 SDK.Base、改名、补齐、规范化）随 #7**。

### 13. 条件表达式系统：丢弃，推迟到 #12（#6 确立）

**它是什么**：`IExpression<out T>` = 响应式计算单元（`T Result` + `event ResultChanged`）+ 组合子（`And`/`Or`/`Not`/`Excute`/常量 `ToExpression`）+ 流式 `Expression.If(c).Return(x).ElseIf(c2).Return(y).Else(z)` 构建器。**真实用例**（引入 commit `b4c9b79` 同时加的 `ConditionConfig` + `IControllerConfig.When(...)` 揭示）：属性面板控件槽的**子 config 随其他控件实时值反应式切换**——作者声明式写"当 mode==Custom 显示 Slider，否则隐藏"，host 在 value-changed 时驱动重算更新 UI。

**三问结论**：
- **跨进程/跨语言序列化？** 否。持有活 `Func<>` 委托 + 事件订阅，纯进程内、不可序列化，无字符串/JSON DSL、无解释器，与跨语言无关。
- **能否 C# lambda 替代？** 能。手写响应式图谱脆弱（`ElseIf` 状态机、`+= ResultChanged` 把事件字段当 handler 都有 bug），且与 Foundation 现成 `INotifiableProperty<T>` / `IProvider.Select`/`When` 概念重叠。host 本就监听控件值变化，一个普通 `Func<…,bool>` 可见性谓词按 value-changed 重算即达成同样 UX，面更小。
- **必要性？** 当前**零消费者**（effect 全树仅 4 个定义文件互引，`ConditionConfig` 也无人用），它服务的"条件属性面板"功能本身未设计。纯投机基础设施。

**决策：从迁移中丢弃，当前不进任何层**。理由：非数据值类型（→不进 Primitives，§三.10）、非 host 注入服务、与 Foundation Event 基建重叠、功能未设计（违 §三.7 不提前建空壳 + §三.10 内核增长纪律）。SDK.Base 里那份 `IExpression_V1` 正是 §三.1/§三.11 已否决的"重拷 `_V1`"。**推迟到 #12**（属性面板）真正设计条件可见性时再定，默认优先级：(a) host 侧普通谓词 `Func<…,bool>` 按值变化重算；(b) 复用 Foundation `INotifiableProperty`/`IProvider` 组合子；(c) 仅当确实需要作者面向的声明式响应式 config，才引入**最小单份** `IExpression` 面（无 `_V1`，与 Config 家族同处）进 **SDK.Base**——**永不进 Primitives**。

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
- ✅ **#6 条件表达式系统**（2026-06-01）— 纯决策，不改代码。`IExpression<T>`（响应式计算单元 + And/Or/Not/Excute/If-ElseIf-Else 组合子）的真实用例是**条件属性面板**（控件槽子 config 随其他控件实时值反应式切换，引入 commit 同时加的 `ConditionConfig`/`IControllerConfig.When` 揭示）。三问：不需也不可跨进程/跨语言序列化（持活委托+事件订阅）；可用 host 侧 `Func<…,bool>` 谓词替代（手写图谱脆弱有 bug，且与 Foundation `INotifiableProperty`/`IProvider` 重叠）；**零消费者**、功能未设计。**决策：丢弃，当前不进任何层**（非数据值→非 Primitives，非服务，违不提前建空壳+内核增长纪律；SDK.Base 那份 `_V1` 即已否决的重拷）。**推迟到 #12**，届时优先 host 谓词 / 复用 Foundation Event 基建，必要才引最小单份 `IExpression` 进 SDK.Base，永不进 Primitives。详见 §三.13。
- ✅ **#3 TuneLab.Core 程序集**（2026-05-29）— 结论 **不建 Core**（effect 的 Core 是 grab-bag，内容各归位）；确立 **数据/服务分家**（数据=具体类型直接 `new`，服务=接口 host 注入）；新增中性冻结内核 **`TuneLab.Primitives`**（`Foundation` 与 `SDK.*` 共同引用，因生命周期不同而独立于 Foundation，`internal`/IVT 不能替代）。边界类型 `Point`/`MonoAudio`/Property值模型/ControllerConfigs→`Primitives`、`DataInfo`→`SDK.Format`、服务接口→`SDK.Base`；`ILog`/`ITuneLabContext` 进 `SDK.Base` 注入式（弃 `static Global`，因 ALC 下每-ALC 一份）；命名用诚实 namespace + `global using` 别名；冻结类型经"整代版本化 + 归档 dll + `extern alias` compat"安全演进。详见 §三.10，蓝图 §三.7 已更新（去 Core、加 Primitives）。落地留 #7/#8，本话题不改代码。
