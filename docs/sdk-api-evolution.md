# SDK API 演进纪律（冻结契约 · 维护者文档）

> 面向改动 `TuneLab.SDK` / `TuneLab.Foundation`（两个插件可引用的契约程序集）的维护者。
> 2.0 发布即冻结 ABI：已编译插件在所有 2.x 宿主上必须持续可加载、可运行。本文回答"哪种改动合法、按什么规矩做"。

## 0. 三道机械防线（本文档的执行基础）

| 防线 | 位置 | 拦什么 |
|---|---|---|
| **PublicAPI 门禁**（PublicApiAnalyzers） | 两项目的 `PublicAPI.Shipped.txt`/`Unshipped.txt`，RS0016/RS0017 = error | 编译期拦下一切未申报的 public 面变动——删/改签名必红，加 API 必须显式申报 |
| **AssemblyVersion 钉死** | 两 csproj 显式 `<AssemblyVersion>2.0.0.0</AssemblyVersion>`，2.x 永不再动 | 绑定层退出裁决：插件加载成败只取决于 ABI 真实兼容性，不取决于版本数字 |
| **sdk-version 门** | manifest `sdk-version` × `ExtensionManager.SdkVersion` | "插件用了新 API × 老宿主"这一唯一合法不兼容方向，加载前人话拒绝 |

**发布收口动作**（每次发布含新 API 的版本时，一并做）：`PublicAPI.Unshipped.txt` 整体挪进 `Shipped.txt` + `ExtensionManager.SdkVersion` 提一档（如 1.0 → 1.1）。

## 1. 接口三分类：加成员的规矩由"谁实现它"决定

判据：接口新增成员时，**破坏的是实现者**（未重编译的实现类缺该成员 → 加载/调用失败）；消费者只多一个可调成员、天然安全。所以政策按"设计意图上谁实现"分三类。分类是**契约宣告**：声明为宿主实现面的接口，插件自行实现不受版本兼容保护。

### A. 插件实现面 —— 新增成员**必须带 DIM 默认体**（C# 默认接口方法）

插件实现、宿主调用。加成员若无默认体，所有已编译插件即刻破坏。

| 接口 | 说明 |
|---|---|
| `IVoiceSynthesisEngine` / `IVoiceSynthesisSession` | voice 引擎/会话 |
| `IInstrumentSynthesisEngine` / `IInstrumentSynthesisSession` | instrument 引擎/会话 |
| `IEffectSynthesisEngine` / `IEffectSynthesisSession` | effect 引擎/会话 |
| `IImportFormat` / `IExportFormat` | 工程格式导入/导出 |
| `IExtensionSettings` | 扩展设置声明 |
| `INumberFormat` / `IDragResponse` / `INormalizedScale` | 策略接口：SDK 有 Custom 工厂，但插件直接实现同样合法 |
| `IEvent<TEvent>` / `IActionEvent`（0–8 元全家族） | 插件会话的产物事件出口（如 `StatusChanged`）可自定义实现 |
| `ISubscriber<T, TFunction>` | `WhenAny` 的订阅者定制点 |

DIM 默认体的语义责任：默认体必须是**可长期成立的兜底行为**（回退到既有重载、返回空集、声明"不支持"），不得替实现者许诺它未必满足的语义（`IsContinuation` 刻意无默认体即此判据的反面教材侧写——判定与合成行为成对绑定时，宁可 breaking 也不给假默认）。

### B. 宿主实现面 —— **可自由加成员**（含带 getter 的属性），插件勿实现

宿主实现、插件消费。加成员时宿主同步实现即可，任何已编译插件不受影响。**插件自行实现这些接口不在兼容承诺内**——版本升级可能使其缺成员而失败。

| 组 | 接口 |
|---|---|
| 会话活视图 | `IVoiceSynthesisContext` / `IVoiceSynthesisNote` / `IInstrumentSynthesisContext` / `IInstrumentSynthesisNote` / `IEffectSynthesisContext` / `IEffectSynthesisAudio` / `IEffectSynthesisView` / `ISynthesisAutomation` / `IAutomationEvaluator` / `IAudioSegment` |
| 声明面 context / 值视图 | `IVoiceSynthesisPartPropertyContext` / `IVoiceSynthesisNotePropertyContext` / `IVoiceSynthesisPartView` / `IVoiceSynthesisNoteView` / `IVoiceSynthesisPhonemeView` / instrument 平行四件（PartPropertyContext / NotePropertyContext / PartView / NoteView） / `IEffectSynthesisPropertyContext` / `IExtensionSettingsContext` |
| 环境 | `ITuneLabContext` / `ILogger` |
| 通知面（Foundation） | `IReadOnlyNotifiable` / `IReadOnlyNotifiableProperty<T>` / `IReadOnlyNotifiablePropertyObject` / `IReadOnlyNotifiableEnumerable<T>` / `IReadOnlyNotifiableCollection<T>` / `IReadOnlyNotifiableList<T>` / `IReadOnlyNotifiableLinkedList<T>` |

### C. 开放数据结构接口 —— 按插件实现面对待（加成员须 DIM）

`IReadOnlyMap<K,V>` / `IReadOnlyOrderedMap<K,V>` / `IMap<K,V>` / `IOrderedMap<K,V>` / `IReadOnlyKeyValuePair<K,V>`（Foundation）。

插件返回集合时通常用 SDK 自带实现（`Map`/`OrderedMap`/collection expression 经 CollectionBuilder），但接口开放、插件自实现合法（如惰性视图）。**不能排除外部实现者 ⇒ 加成员按 A 类规矩**。

### D. config 封闭族 —— SDK 内封闭实现，可同步改、无 DIM 需要

`IControllerConfig` / `IValueConfig` / `IValueConfig<T>` 及 9 个 config 类（Slider/ComboBox/CheckBox/TextBox/DraggableNumberBox/Automation/Object/Array/List/ExtensibleObject）。

- 实现集封闭在 SDK 内（config 类全 sealed + 私有构造 + 静态工厂/With，见既有"工厂化构造"约定）；宿主渲染器按具体类型分发，未知实现渲染为 unsupported 占位、不崩。
- 加新 config 类型 = 纯加性（新类 + 宿主新 creator）；给既有 config 加字段走"工厂化构造 + `With` 流式"既有 ABI 韧性机制。

## 2. 纯值 DTO：形态政策

加字段的通用规矩：**带默认值、不标 `required`**——已编译插件构造的旧对象缺新字段时取默认，纯加性。（托管 struct 加字段同样是加性兼容：布局 JIT 期按加载类型现算，前提是既有字段不动。）

### class 型快照/信息（sealed class + `required init`）

`VoiceSynthesisSnapshot` / `VoiceSynthesisNoteSnapshot` / `InstrumentSynthesisSnapshot` / `InstrumentSynthesisNoteSnapshot` / `SynthesisAutomationSnapshot` / `SynthesizedPitch` / `SynthesizedParameter` / `VoiceSourceInfo` / `InstrumentSourceInfo` / `AgentChat` 族（已收回宿主）等。

- 初次发布的必填成员标 `required`；**发布后新增成员一律不标 required、带默认**。
- struct vs class 判据：小、高频、无身份的合成值 → struct（零分配平铺数组）；有身份、引用型可选字段会增长、禁 default 空态的实体元数据 → class（required init 直接禁掉全 null 空态）。值类型 ↔ 引用类型互转是根本性破坏，冻结前按本质定终形。

### struct 型值 DTO 三形态（2.0 定案）

| 形态 | 何时用 | 实例 |
|---|---|---|
| **`readonly struct` + `{ get; init; }`**（房规默认） | 对象初始化器构造的合成值 | `SynthesizedPhoneme` / `SynthesisStatusSegment` / `PhonemeLayoutNote` |
| **`readonly struct` + 构造函数**（备选） | 全字段必填、需构造期校验或位置构造自然 | `VoiceSynthesisPhonemeSnapshot` / `SynthesizedSyllable` / `PhonemeTiming` / `SynthesisRange` / `PropertyKey` |
| ~~可变 struct + public 字段~~ | **已淘汰**：裸字段把成员永久锁死（field↔property 是二进制破坏），且有可变 struct 固有坑 | —（2.0 前已全部转换） |

成员一律经属性访问器暴露（`get_X` 稳定 ABI 面，backing 可演进为校验/计算属性）；`Point` 类几何值保持 `IEquatable<T>` + 自反 `Equals`（NaN 语义见其注释）。

### 集合持有纪律

复合对象构造时**拷入自持**传入集合（`[.. x]`），值语义由构造保证、不靠调用方纪律（PropertyObject / 复合 config 已统一）；事件 `Merge` 家族构造时固化源快照（动态成员走 `WhenAny`/`WhenAnyItem`）。

## 3. 枚举：加成员是加性，但要未知值容忍

SDK 公开枚举（`PropertyType` / `SynthesisSegmentStatus` / `DragAxis` 等）加成员对**产出方**是加性；对**消费方**（switch 的一侧）是半破坏——已编译代码遇未知值行为未定义。规矩：

- 跨边界枚举的**消费方须容忍未知值**（default 分支降级处理、不抛）；宿主对插件产出枚举的既有消费以判等/白名单式为主（如 `status == Synthesized`），未知值自然落否定分支——新增消费点保持此风格或补 default 分支。
- 新增成员时在枚举注释上标注引入版本，并确认对侧消费代码的 default 分支行为合理。

## 4. 变更操作速查

| 想做的事 | 合法性 | 操作 |
|---|---|---|
| 给 A/C 类接口加成员 | ✅ 加性 | 带 DIM 默认体 + 申报进 `PublicAPI.Unshipped.txt` |
| 给 B 类接口加成员 | ✅ 加性 | 宿主同步实现 + 申报 Unshipped |
| 给 DTO 加字段/属性 | ✅ 加性 | 带默认、不标 required + 申报 Unshipped |
| 加新类型（config/DTO/接口） | ✅ 加性 | 申报 Unshipped |
| 给枚举加成员 | ✅ 加性（有条件） | 确认消费侧未知值容忍 + 申报 Unshipped |
| 删/改任何 public 签名 | ❌ breaking | 编译必红（RS0017+RS0016）；发布前 = 编辑 Shipped.txt 重新定稿；发布后 = 3.0 事项，`*REMOVED*` 留痕 |
| struct ↔ class、field ↔ property、改字段类型宽度 | ❌ breaking | 同上——这三类在冻结前已按终形定案，不要再动 |
| 发布含新 API 的版本 | — | Unshipped → Shipped + `ExtensionManager.SdkVersion` 提档（一个动作、两处台账） |
