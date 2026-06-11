using TuneLab.Primitives.Property;
using TuneLab.SDK.Base.Timing;

namespace TuneLab.SDK.Voice;

// note 的不可变快照值（worker 线程、将来 worker 进程只读）：已解析双域 Position、
// Properties 值拷。触底到值类型：不含任何 live 数据对象引用（Phonemes 也须是物化时新建的副本），
// 自包含可序列化值树，为跨进程演进留路。
// 邻居导航不进本面（接口最小化）：ISynthesisSnapshot.Notes 即有序列表（与 segment.Notes
// 索引对齐），协同发音按索引取邻居。
// 只写一次：构造即定形（单线程），构造 happens-before worker 启动，此后只读。
public sealed class SynthesisNoteSnapshot(
    Position startPosition,
    Position endPosition,
    int pitch,
    string lyric,
    IReadOnlyList<PhonemeInfo> phonemes,
    PropertyObject properties)
{
    public Position StartPosition { get; } = startPosition;
    public Position EndPosition { get; } = endPosition;
    public int Pitch { get; } = pitch;
    public string Lyric { get; } = lyric;
    public IReadOnlyList<PhonemeInfo> Phonemes { get; } = phonemes;
    public PropertyObject Properties { get; } = properties;
}
