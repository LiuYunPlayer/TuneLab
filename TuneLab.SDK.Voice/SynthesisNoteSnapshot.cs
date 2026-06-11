using TuneLab.Primitives.Property;
using TuneLab.SDK.Base.Timing;

namespace TuneLab.SDK.Voice;

// note 的不可变快照值（worker 线程、将来 worker 进程只读）：已解析双域 Position、
// Properties 值拷。触底到值类型：不含任何 live 数据对象引用（Phonemes 也须是物化时新建的副本），
// 自包含可序列化值树，为跨进程演进留路。
// 邻居导航不进本面（接口最小化）：SynthesisSnapshot.Notes 即有序列表（与 GetSnapshot
// 递入的 notes 索引对齐），协同发音按索引取邻居。
// 形态 = 无参构造 + required init 属性：初始化后不可变（只写一次纪律），将来加字段
// 纯加性（新字段带默认值、不标 required，不破构造签名）。
public sealed class SynthesisNoteSnapshot
{
    public required Position StartPosition { get; init; }
    public required Position EndPosition { get; init; }
    public required int Pitch { get; init; }
    public required string Lyric { get; init; }
    public required IReadOnlyList<PhonemeInfo> Phonemes { get; init; }
    public required PropertyObject Properties { get; init; }
}
