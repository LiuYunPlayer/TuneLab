using TuneLab.Primitives.Property;
using TuneLab.SDK.Base.Timing;

namespace TuneLab.SDK.Voice;

// note 的不可变快照值（worker 线程、将来 worker 进程只读）：已解析双域 Position、
// Properties 值拷、段内 Next/Last 成链——协同发音的邻居导航在快照链上做，不回活对象。
// 触底到值类型：不含任何 live 数据对象引用（Phonemes 也须是物化时新建的副本），
// 自包含可序列化值树，为跨进程演进留路。
// 只写一次：CreateChain 构造 + 接链后不再变；构造 happens-before worker 启动，此后只读。
public sealed class SynthesisNoteSnapshot
{
    public Position StartPosition { get; }
    public Position EndPosition { get; }
    public int Pitch { get; }
    public string Lyric { get; }
    public IReadOnlyList<PhonemeInfo> Phonemes { get; }
    public PropertyObject Properties { get; }

    // 段内邻居；段外为 null（快照自包含，链不出段）。
    public SynthesisNoteSnapshot? Next { get; private set; }
    public SynthesisNoteSnapshot? Last { get; private set; }

    // 单个 note 的快照字段（接链前的裸值），宿主物化时逐 note 填好递给 CreateChain。
    public sealed record Data(
        Position StartPosition,
        Position EndPosition,
        int Pitch,
        string Lyric,
        IReadOnlyList<PhonemeInfo> Phonemes,
        PropertyObject Properties);

    // 唯一构造入口：按列表顺序（即 segment.Notes 的声明顺序）构造并接好段内链，
    // 返回后整链不可变。
    public static IReadOnlyList<SynthesisNoteSnapshot> CreateChain(IReadOnlyList<Data> notes)
    {
        var snapshots = new SynthesisNoteSnapshot[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            snapshots[i] = new SynthesisNoteSnapshot(notes[i]);
        }
        for (int i = 0; i + 1 < snapshots.Length; i++)
        {
            snapshots[i].Next = snapshots[i + 1];
            snapshots[i + 1].Last = snapshots[i];
        }
        return snapshots;
    }

    SynthesisNoteSnapshot(Data data)
    {
        StartPosition = data.StartPosition;
        EndPosition = data.EndPosition;
        Pitch = data.Pitch;
        Lyric = data.Lyric;
        Phonemes = data.Phonemes;
        Properties = data.Properties;
    }
}
