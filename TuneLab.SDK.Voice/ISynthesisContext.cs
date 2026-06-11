using System.Diagnostics.CodeAnalysis;
using TuneLab.Primitives.Event;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.Timing;

namespace TuneLab.SDK.Voice;

// 会话级输入活视图：宿主实现，每次 CreateSession 新建、随会话死。
// 插件订阅它而非宿主长寿数据层——中间层短命使泄漏结构性不可能（无需弱事件/退订契约），
// 且宿主始终握着线程/时机/故障隔离/批量四个旋钮（在 command 提交后、数据线程、try-catch 包裹下 emit）。
//
// 线程纪律：事件恒在数据线程触发与处理，handler 只做廉价记录/标脏；合成永不碰活视图，
// 只读派发时物化的 ISynthesisSnapshot。
//
// 通知语义：只转发已提交的真实变更（拖拽等中间态被业务层 merge 折叠掉，订阅者眼中状态
// 从编辑前直达收口后）。变更定位的四种最小事实——字段变了（note 可订阅属性，配合
// WillModify/Modified 拿旧/新值）、区间变了（ISynthesisAutomation.RangeModified 带 tick 范围）、
// 集合变了（Notes 增删）、时基变了（TimingModified）。这些事实映射到哪些段、重合成到
// 管线哪一级（失效依赖图）归插件；机制粒度支撑最精细策略，也允许"任何通知 → 全部标脏"的懒实现。
public interface ISynthesisContext
{
    IReadOnlyNotifiableList<ISynthesisNote> Notes { get; }   // 支持 WhenAny（成员增删自动接线）
    IReadOnlyNotifiablePropertyObject PartProperties { get; }
    bool TryGetAutomation(string key, [MaybeNullWhen(false)] out ISynthesisAutomation automation);
    ISynthesisAutomation Pitch { get; }
    ITiming Timing { get; }   // tick↔秒换算（活视图侧，随 tempo 表变化）

    // tempo 变了：全部秒域派生随之失效（Position 由宿主 re-derive），引擎通常全量重排。
    event Action? TimingModified;

    // 批量变更括号：每个逻辑编辑（一个 command，含单条编辑）都包在括号里。
    // 它不是宿主缓冲，而是让插件延迟昂贵状态修正的作用域信号——每条变更通知里廉价记录、
    // BatchEnd 一次性做重活（如重分片），批量编辑（移调几百个 note）因此只重分片一次。
    event Action? BatchBegin;
    event Action? BatchEnd;
}

// automation 轨的会话级活视图：取值 + 区间变更订阅。
// 插件由此做最细粒度失效："某轨 [startTick, endTick) 变了 → 只标脏覆盖该区间的段"。
public interface ISynthesisAutomation : IAutomationValueGetter
{
    event Action<double, double>? RangeModified;   // (startTick, endTick)
}
