using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 会话级输入活视图：宿主实现，每次 CreateSession 新建、随会话死。
// 插件订阅它而非宿主长寿数据层——中间层短命使泄漏结构性不可能（无需弱事件/退订契约），
// 且宿主始终握着线程/时机/故障隔离/批量四个旋钮（在 command 提交后、数据线程、try-catch 包裹下 emit）。
//
// 线程纪律：事件恒在数据线程触发与处理，handler 只做廉价记录/标脏；合成永不碰活视图，
// 只读派发时物化的 SynthesisSnapshot。
//
// 通知语义：只转发已提交的真实变更（拖拽等中间态被业务层 merge 折叠掉，订阅者眼中状态
// 从编辑前直达收口后）。变更定位的三种最小事实——字段变了（note 可订阅属性，配合
// WillModify/Modified 拿旧/新值）、区间变了（ISynthesisAutomation.RangeModified 带秒范围）、
// 集合变了（Notes 增删）。这些事实映射到哪些段、重合成到管线哪一级（失效依赖图）归插件；
// 机制粒度支撑最精细策略，也允许"任何通知 → 全部标脏"的懒实现。
//
// 坐标系约定（SDK 面）：插件侧时间量一律为全局秒（与音频产物、状态段同一时间系）；tick 仅
// 是宿主乐谱内部表示、不外露。tempo 变化无独立信号——它被分解为具体变更：note 边界秒值变 →
// 其 StartTime/EndTime.Modified 触发；automation 秒映射移位 → 宿主在批量括号内对受影响轨触发
// 全区间 RangeModified。插件用既有订阅机制即收到，无需"时基变了"这种元信号。
public interface ISynthesisContext
{
    // 链表形态（无索引承诺，宿主数据层即双向链表）：顺序消费用枚举、头尾 O(1) 走
    // First/Last、邻居导航走 note.Next/Last；支持 WhenAny（成员增删自动接线）。
    //
    // 排序契约（全序、确定性）：StartTime 升序 → 同起点 EndTime 降序（长 note 在前）→
    // 再同则保持宿主插入序。note 可重叠（和弦）——序列直传原始可重叠 note，"后盖前"等
    // 去重叠是插件自己的责任（单声部插件按需截断，和弦插件原味消费重叠）。
    IReadOnlyNotifiableLinkedList<ISynthesisNote> Notes { get; }
    IReadOnlyNotifiablePropertyObject PartProperties { get; }
    bool TryGetAutomation(string key, [MaybeNullWhen(false)] out ISynthesisAutomation automation);

    // 音高的两个平行通道（绝对约束 + 相对偏差）：
    // Pitch = 用户钉死的绝对音高曲线（分段型：有值=钉死、NaN=插件自由发挥）；
    // PitchDeviation = 加性偏差（连续型：处处有值、默认 0、永不 NaN；宿主侧 vibrato 等偏差源都汇于此）。
    // 合成契约：finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)——插件先解析绝对面
    // （钉死区用用户值、自由区自己生成），再叠加偏差；偏差因此也作用于未绘制区域。
    ISynthesisAutomation Pitch { get; }
    ISynthesisAutomation PitchDeviation { get; }

    // 物化合成快照（插件主动拉取）：notes = 本次合成需要的 note（段内 + 协同发音邻居，
    // 插件自由圈定，返回的 snapshot.Notes 与之索引对齐）；[startTime, endTime] = 曲线开窗区间（秒）。
    // 仅数据线程、仅 SynthesizeNext 的同步前缀（offload 之前）调用；一次合成可按需拉多份
    // （如音素级小窗 + 音频级大窗）。物化/版本缓存/记账留在宿主实现内。
    SynthesisSnapshot GetSnapshot(IReadOnlyList<ISynthesisNote> notes, double startTime, double endTime);

    // 批量变更括号：每个逻辑编辑（一个 command，含单条编辑）都包在括号里。
    // 它不是宿主缓冲，而是让插件延迟昂贵状态修正的作用域信号——每条变更通知里廉价记录、
    // BatchEnd 一次性做重活（如重分片），批量编辑（移调几百个 note）因此只重分片一次。
    event Action? BatchBegin;
    event Action? BatchEnd;
}
