using TuneLab.Primitives.Event;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base.Timing;

namespace TuneLab.SDK.Voice;

// 会话活视图中的 note（可订阅，仅数据线程）。固定字段保持最小（通用乐理属性）；
// voice 专属 per-note 参数一律走 Properties（keyed）——加新参数 = 加会话声明 NoteProperties
// 的 key，不动本接口固定面。
public interface ISynthesisNote
{
    IReadOnlyNotifiableProperty<Position> StartPosition { get; }
    IReadOnlyNotifiableProperty<Position> EndPosition { get; }
    IReadOnlyNotifiableProperty<int> Pitch { get; }
    IReadOnlyNotifiableProperty<string> Lyric { get; }
    IReadOnlyNotifiableProperty<IReadOnlyList<PhonemeInfo>> Phonemes { get; }
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 邻居链（协同发音用）。活视图上仅供数据线程的分片决策 live 导航——事件 handler 内
    // 只有 note 自身引用、无列表索引上下文，O(1) 邻居导航是真实便利；
    // 合成须在快照（snapshot.Notes 有序列表按索引）上导航，不回活对象。
    ISynthesisNote? Next { get; }
    ISynthesisNote? Last { get; }
}
