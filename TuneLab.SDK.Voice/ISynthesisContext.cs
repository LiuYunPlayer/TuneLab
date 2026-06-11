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

    // 音高的两个平行通道（绝对约束 + 相对偏差）：
    // Pitch = 用户钉死的绝对音高曲线（分段型：有值=钉死、NaN=插件自由发挥）；
    // PitchDeviation = 加性偏差（连续型：处处有值、默认 0、永不 NaN；宿主侧 vibrato 等偏差源都汇于此）。
    // 合成契约：finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)——插件先解析绝对面
    // （钉死区用用户值、自由区自己生成），再叠加偏差；偏差因此也作用于未绘制区域。
    ISynthesisAutomation Pitch { get; }
    ISynthesisAutomation PitchDeviation { get; }

    ITiming Timing { get; }   // tick↔秒换算（活视图侧，随 tempo 表变化）

    // 物化合成快照（插件主动拉取）：notes = 本次合成需要的 note（段内 + 协同发音邻居，
    // 插件自由圈定，返回的 snapshot.Notes 与之索引对齐）；[startTick, endTick] = 曲线开窗区间。
    // 仅数据线程、仅 SynthesizeNext 的同步前缀（offload 之前）调用；一次合成可按需拉多份
    // （如音素级小窗 + 音频级大窗）。物化/版本缓存/记账留在宿主实现内。
    ISynthesisSnapshot GetSnapshot(IReadOnlyList<ISynthesisNote> notes, double startTick, double endTick);

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
