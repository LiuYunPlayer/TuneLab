using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 宿主物化的不可变合成快照（IInstrumentSynthesisContext.GetSnapshot 的返回体）——instrument 专属面。
// 插件在 SynthesizeNext 的同步前缀（数据线程）主动拉取，之后才 offload——worker 永不碰活对象、只读它。
// 形状与 IInstrumentSynthesisContext 活视图镜像对称，但为纯数据体：无事件、无活引用；构造形态 = 无参 + required init。
//
// 替换，而非同步：快照只写一次（构造，数据线程），构造 happens-before worker 启动，此后只读；
// 数据变了走活视图通知 → 插件标脏 → 下次合成拉一份全新快照。无共享可变状态，无需锁。
//
// 与 voice 的 VoiceSynthesisSnapshot 差异：Notes 为满末快照（不去重叠）；【无 Pitch / PitchDeviation 通道】
// （instrument v1 纯按 note 整数 pitch 发声）。automation 双通道若将来需要（弯音）为纯加性扩展。
public sealed class InstrumentSynthesisSnapshot
{
    // 不可变值快照，有序列表；与 GetSnapshot 递入的 notes 索引对齐（产物归属契约），邻居按索引导航。
    public required IReadOnlyList<InstrumentSynthesisNoteSnapshot> Notes { get; init; }

    // 值拷（不可变 PropertyObject）。
    public required PropertyObject PartProperties { get; init; }

    // 已声明 automation 轨按开窗区间物化，只读 map：可枚举可点取（与活视图
    // IInstrumentSynthesisContext.Automations 同构）。
    public required IReadOnlyMap<string, SynthesisAutomationSnapshot> Automations { get; init; }
}
