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

> 每完成一个话题，在下面追加一条结论摘要。

_暂无_
