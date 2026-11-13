# 只传值的声明（已实现）

插件声明一份数据，但宿主不给它任何编辑控件：数据照常存储、照常传给合成，界面上看不到它。

## 1. 要解决的问题

一个参数换了新的表达方式，旧数据还得继续生效。典型场景：某声源插件的"说话人混合"原先是一个 part 属性加每人一条连续轨，
新版改成一块 part 级时间轴画布。兼容做法（插件开发文档 §5.2「字段形状演进」）是"画布没设过值时，把旧数据换算成新形状显示；
用户一编辑就写成新格式"。

卡在合成侧：

- **声明侧拿得到**：声明视图的 `Automations` 是"有内容的轨"口径，不再声明的旧轨（孤儿）也在，画布据此换算显示。
- **合成侧拿不到**：合成上下文与快照只放**当前声明**的轨（`VoiceSynthesisContext`、各 `*SynthesisSnapshotFactory` 都按
  `AutomationConfigs` 遍历）。旧轨一不声明，合成就读不到——旧工程在画布没动过时，看到的是旧混合，听到的不是。
- **现在的折中**：旧轨照常声明、默认不在标签栏显示。合成拿得到了，但用户在标签栏右键列表里仍看得到它们，侧栏的默认值滑条也照样显示
  （见 §6），旧入口没有真正消失。

根子在于：声明同时说了两件事——"有这份数据"和"用这个控件编辑它"，想要前者就躲不开后者。本稿让声明可以只说前者。

否决过的方案：

- **合成快照另带孤儿轨**（`OrphanAutomations`）：为一个场景开一条绕过声明的数据通道。孤儿没有声明，标度、默认值、形态都得另定规矩，
  插件也说不清"哪些孤儿是我还要的"。声明本来就是插件说"这份数据我要"的地方。
- **插件在显示名后标"已过时"**：只能标记，入口还在。
- **画布首次绘制时自动提交换算结果**：一打开旧工程就多一条撤销记录、工程被标脏。
- **按数据类型各开一个纯值 config 类**（数、字符串……）：字段类型本来就不靠 config 说（见 §3.2），拆开只多了分派分支。
- **独立声明表的值类型改成标记接口、只传值另开类**：时间轴画布本来就需要"默认显示 / 默认隐藏"，再为"不显示"换类型，
  同一张表里就有两套机制；也想不出这张表里还要放第二种东西。
- **接口参数改成 `object`、文档里写类型**：约定还在，只是编译器和 PublicAPI 门禁不再检查，破坏改到运行时才暴露；
  加性演进靠工厂 + `With` 与带默认体的接口成员已经够用。

## 2. 两种声明，两种写法

| | 在哪 | 只传值的写法 | 用户能在标签栏显示 / 隐藏吗 |
|---|---|---|---|
| 自动化轨 `AutomationConfig` | 独立的声明表（`GetAutomationConfigs`） | `ControllerVisibility.Never`（§3.1） | 能 |
| 时间轴画布 `TimelineCanvasConfig` | 独立的声明表（`GetTimelineCanvasConfigs`） | 同上 | 能 |
| 控件树里的任何成员：滑条、下拉框……、侧栏画布 `CanvasConfig`、画布 lane `CanvasLaneConfig` | part / note / 音素 / 效果器的属性树 | 原位换成 `ValueOnlyConfig.Create()`（§3.2） | 不适用 |

分界线是"是不是控件树的成员"，根子在**数据存在哪**：

- 自动化轨与时间轴画布各有自己的存储（`Automations` / `PiecewiseAutomations` 的曲线；`TimelineCanvases` 的"偏移 + 整值"，
  拆分时宿主平移偏移、合成侧按区间标脏），它们的 config 就是这条独立通道的声明。把 `ValueOnlyConfig` 放进这两张表，
  同一个类在属性树里表示"存进属性"、在这里表示"存进通道"，一类两义。
- 控件树里，任何位置都能换成另一种 config，"没有控件"就是一种叶子，不需要别的机制。画布 lane 虽然编辑界面在参数面板，
  在侧栏里仍占一行（字段名 + 摘要 + 编辑按钮），它是树的成员；换成 `ValueOnlyConfig` 后侧栏那一行和标签栏的条目一起消失。
- 独立声明表里的条目没有"换成别的控件"这条路，所以自己带"不显示"这一档。

## 3. SDK 形状

### 3.1 `ControllerVisibility`：自动化轨与时间轴画布

```csharp
public enum ControllerVisibility
{
    ShownByDefault = 0,  // 默认：标签栏上显示，用户可以隐藏
    HiddenByDefault,     // 标签栏上默认隐藏，用户可以在右键列表里打开
    Never,               // 从不出现控件；数据照常存储、照常传给合成
}

AutomationConfig.WithVisibility(ControllerVisibility visibility)       // 取代 WithShownByDefault（未发布，直接替换）
TimelineCanvasConfig.WithVisibility(ControllerVisibility visibility)   // 同上

AutomationConfig.Create()       // 不收界面参数：没有量程（不量化），WithDefault 照常决定连续 / 分段；可见性固定为 Never
TimelineCanvasConfig.Create()   // 不收控件工厂；可见性固定为 Never
```

- **成员名**：前两档是初始状态、用户随时能改，所以成对带 `ByDefault`；`Never` 由插件定死，不带。`Shown = 0`：没调 `WithVisibility`
  就是显示，插件新增的参数自然出现。
- **名字里的 Controller**：管的是"编辑控件出不出现"，数据永远都在；范围是这条轨的所有控件（标签栏、侧栏默认值滑条），不只标签栏。
  术语表把单写的 `Visible` 留给了参数面板的小眼睛（作背景画不画），加了 Controller 限定后指的是控件，不混。
- **空壳 `Create()`**：`Never` 时只给界面用的字段（量程、颜色、格式、两端标签、控件工厂）都不用填。在空壳上调
  `WithVisibility(ShownByDefault / HiddenByDefault)` **当场抛参数异常**，开发时就发现，不会变成一条没有控件的空轨；
  调颜色等其他 `With` 不报错、没有效果。反过来，完整创建的 config 也可以 `WithVisibility(Never)`（插件按条件切换时方便）。
- **自动化轨的量程不全是界面信息**：离散标度会让合成读到的值落格。空壳不量化；旧数据需要量化就用 `Create(scale).WithVisibility(Never)`。
  空壳的 `WithDefault` 也不是显示兜底，而是数据形态：决定连续 / 分段，没有曲线的地方合成取它。
- **未知值容忍**（`docs/sdk-api-evolution.md` §3）：宿主遇到不认识的枚举值按 `ShownByDefault` 处理——宁可多显示，不让数据悄悄失去入口。

### 3.2 `ValueOnlyConfig`：控件树里的成员

```csharp
public sealed class ValueOnlyConfig : IControllerConfig   // 不实现 IValueConfig：没有默认值
{
    public static ValueOnlyConfig Create();
}
```

- **没有默认值**，值语义与画布完全相同（设计稿 `canvas-in-parameter-panel.md` §13）：声明里的默认值只是给宿主控件显示用的兜底，
  插件读值时本来就传自己的默认值；没有控件就用不着它。没设过 = 不存值，插件读到 Null 时补自己的默认值；恢复默认 = 删掉存值；
  宿主按原值读，不按默认值类型过滤。
- **不实现 `IValueConfig`**：否则预设抓取、恢复默认、新元素填充这些按 config 树递归取默认值的逻辑会去取它的默认值。
- 可以放在控件树的任何位置：对象成员、数组 / 列表元素、键控容器条目。数组 / 列表里它所在的位置上是 Null（同画布元素）。

## 4. 宿主行为

只传值的声明在数据与合成上和普通声明完全一样，差别只在界面：

| 地方 | 只传值 |
|---|---|
| 合成上下文 / 快照、`RangeModified`、标脏 | 照常（就是已声明的数据，零改动） |
| 存储、序列化、拆分 / 合并 | 照常 |
| part 预设 | 照常；`ValueOnlyConfig` 没存值的保持没存值（同画布） |
| 声明视图（插件读） | 照常 |
| 参数面板标签栏、右键显隐列表 | 不出现；用户以前对它设过的显隐记录不生效 |
| 侧栏：自动化轨默认值滑条（声源、效果器两处） | 不出现 |
| 侧栏：属性树里 `ValueOnlyConfig` 的位置 | 不占行；数组 / 列表里它所在的那一行整个不出现（其余行的序号仍按数据位置）。整个对象都不要控件，就把对象本身换成它 |
| 钉选、参数面板回落目标、标题栏、动作面的显隐参数 | 不出现 |
| 颤音影响哪些轨的界面 | 不列出（已有的影响照常生效） |
| 范围选区的复制 / 剪切 / 删除 | 照常作用：选区操作针对"这段时间的全部数据"，今天对隐藏轨、孤儿轨也是如此 |
| 脚本 / agent | 可读可写（脚本是合法的写入方，比如迁移脚本）；参数说明里标明"没有控件"，`ValueOnlyConfig` 字段接受 null（= 清掉） |

自动化轨 / 时间轴画布这边，实现上收成一个判定，与 `ParameterHiding.IsHidden` 并列，界面各处先过它，不在每个消费点各写一遍。

**只传值的数据用户清不掉**：与孤儿数据同等对待（保留、不显示）。插件切到新格式后就不再读它，留着不影响声音。不给只读 / 灰显入口——
要的话以后再加，是纯新增。

## 5. 上面那个场景怎么写

- 旧的 `mix:` 轨在**画布没设过值时**照常声明、`AutomationConfig.Create().WithDefault(...)`（空壳，只传值）；画布设过值后不再声明
  （成为孤儿，数据保留）。
- 画布的换算显示照旧读声明视图；合成会话读 `Automations` 就有 `mix:`，画面与声音一致。
- 旧 part 属性本来就整个传给合成；想让它进预设、出现在 agent 的参数说明里，原位声明为 `ValueOnlyConfig.Create()`。
- 用户第一次编辑画布即写成新格式，之后不再读旧数据。

## 6. 顺带修：标签栏隐藏的轨在侧栏仍有默认值滑条

侧栏的默认值面板（`AutomationDefaultsController`、`EffectsController.RebuildAutomationRows`）列出所有已声明的连续轨，
不看 `ParameterHiding`。用户在标签栏隐藏了一条轨（或插件声明了默认隐藏、用户没打开），侧栏仍有它的滑条。

改为：隐藏一条轨 = 藏掉它的所有控件，侧栏滑条也跟着藏（订阅 `ParameterHiding.Changed` 重建）。与 `Never` 共用同一个判定。

## 7. 改动清单

- SDK：`ControllerVisibility`；两个 config 的 `WithVisibility`、`Visibility`、空壳 `Create()`；删 `ShownByDefault` / `WithShownByDefault`
  （都在 `PublicAPI.Unshipped.txt`，未发布）；`ValueOnlyConfig`。SDK 版本这一轮已升过，不再升。
- 宿主：§4 各界面消费点；`ConfigValues`（agent / 脚本写值校验）、`CommandFormat`（参数说明）、`PartPresets` 认 `ValueOnlyConfig`；§6。
- 现有用法迁移：`tests/plugins/V1.Voice` 的旧曲线、`NoteLaneShapeTests` 的一个测试；外部开发中插件的默认隐藏写法改一行。
- 文档：插件开发文档三份（自动化轨、时间轴画布、属性值语义各补一段）、`naming-glossary.md`（`ControllerVisibility` 一行，
  与小眼睛的 Visible 区分）。

## 8. 以后可能加的（纯新增，等真实需求）

- **控件树里的成员按控件加 `WithVisibility(Never)`**：保留控件信息（下拉框候选、滑条量程）给脚本 / agent 写值时校验，界面不出现。
  触发条件：有了按 config 的值校验、且确有"无界面但要校验"的字段。
- **画布 lane 声明"默认隐藏"**：用户今天已能在标签栏隐藏 lane，只是插件定不了初始状态。
- **自动化轨挂插件自绘编辑器**（曲线数据 + 画布界面）：数据仍是宿主懂的曲线（拆分、合并、选区、插值、颤音照常），界面由插件画。
