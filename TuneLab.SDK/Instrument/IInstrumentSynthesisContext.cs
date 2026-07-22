using TuneLab.Foundation;

namespace TuneLab.SDK;

// 会话级输入活视图：宿主实现，每次 CreateSession 新建、随会话死——instrument 专属面。
// 插件订阅它而非宿主长寿数据层——中间层短命使泄漏结构性不可能；宿主始终握着线程/时机/故障隔离/批量四个旋钮。
//
// 线程纪律：事件恒在数据线程触发与处理，handler 只做廉价记录 / 标脏；合成永不碰活视图，
// 只读派发时物化的 InstrumentSnapshot。
//
// 坐标系约定：插件侧时间量一律为全局秒（与音频产物、状态段同一时间系）；tick 仅是宿主乐谱内部表示、不外露。
//
// 与 voice 的 IVoiceSynthesisContext 差异：Notes 元素是 IInstrumentNote（满末、不去重叠）；
// 【无 Pitch / PitchDeviation 双音高通道】（v1 纯按 note 整数 pitch 发声）。其余（automation / 快照 /
// 音频段 / Committed 收口）与 voice 同构。
public interface IInstrumentSynthesisContext
{
    // 选定音源（= IInstrumentSynthesisEngine.InstrumentSourceInfos 的 key）：context 生命内**不可变**（换音源 = 宿主重建 context + 会话），
    // 故烘入 context、CreateSession 不再单列 instrumentId。
    string InstrumentId { get; }
    // 链表形态（无索引承诺，宿主数据层即双向链表）：顺序消费用枚举、头尾 O(1) 走 First/Last、
    // 邻居导航走 note.Next/Previous；支持 WhenAny（成员增删自动接线）。
    //
    // 排序契约（全序、确定性）：StartTime 升序 → 同起点 EndTime 降序（长 note 在前）→ 再同则保持宿主插入序。
    // 【note 可重叠且宿主不去重叠】——instrument 引擎原味消费重叠几何（和弦 / 多声部），自行决定叠加发声。
    IReadOnlyNotifiableLinkedList<IInstrumentSynthesisNote> Notes { get; }
    IReadOnlyNotifiablePropertyObject PartProperties { get; }

    // 通用 automation 轨（引擎声明的力度 / 表情 / 动态等，与 pitch 无关）：只读 map，可枚举可点取
    // （与 voice 的 IVoiceSynthesisContext.Automations 同语义）；引擎不声明即恒空。
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }

    // 物化合成快照（插件主动拉取）：notes = 本次合成需要的 note（段内 + 协同邻居，插件自由圈定，
    // 返回的 snapshot.Notes 与之索引对齐）；[startTime, endTime] = 曲线开窗区间（秒）。
    // 仅数据线程、仅 SynthesizeNext 的同步前缀（offload 之前）调用。
    InstrumentSynthesisSnapshot GetSnapshot(IReadOnlyList<IInstrumentSynthesisNote> notes, double startTime, double endTime);

    // 音频产物的宿主分配工厂：插件产出音频时申请段握柄，写入、Commit() 标完成，重分片时 Dispose() 释放重建。
    // 宿主据此持有段登记表、驱动下游 effect 链按段重渲染。仅数据线程调用；sampleOffset = 全局起始采样位置
    // （native 率，全局 0 时刻 = 采样点 0），sampleCount = 段长（采样数），sampleRate = 该段 native 采样率。
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);

    // 逻辑编辑收口信号：每个逻辑编辑（一个 command）的全部变更通知发完后触发一次——让插件延迟昂贵状态修正
    //（每条变更廉价标脏，Committed 一次性做重活，如重分片）。出方向事件，宿主在数据线程触发。
    IActionEvent Committed { get; }
}
