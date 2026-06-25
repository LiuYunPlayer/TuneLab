using TuneLab.Foundation;

namespace TuneLab.SDK;

// note 的不可变快照值（worker 线程、将来 worker 进程只读）——instrument 专属面。
// 触底到值类型：不含任何 live 数据对象引用，自包含可序列化值树，为跨进程演进留路。
// 与 voice 的 SynthesisNoteSnapshot 差异：EndTime 是【满末】（不去重叠），且无 Lyric / Phonemes。
// 形态 = 无参构造 + required init：初始化后不可变；将来加字段纯加性（新字段带默认、不标 required）。
public sealed class InstrumentNoteSnapshot
{
    public required double StartTime { get; init; }
    // 满末（全局秒）：重叠未截，instrument 引擎原味消费。
    public required double EndTime { get; init; }
    public required int Pitch { get; init; }
    public required PropertyObject Properties { get; init; }
}
