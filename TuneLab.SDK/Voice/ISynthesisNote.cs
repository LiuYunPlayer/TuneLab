using TuneLab.Foundation;

namespace TuneLab.SDK;

// 会话活视图中的 note（可订阅，仅数据线程）。固定字段保持最小（通用乐理属性）；
// voice 专属 per-note 参数一律走 Properties（keyed）——加新参数 = 加会话声明 NoteProperties
// 的 key，不动本接口固定面。
//
// 时间量为全局秒（插件侧统一秒轴，与音频产物同系）：note 边界是 tempo 表派生的秒值，tempo
// 变化时边界值随之改变，其 StartTime/EndTime.Modified 即触发——这正是"tempo 变了"对插件的
// 具体体现，无需独立的时基信号。tick 是宿主乐谱内部表示、不外露。
public interface ISynthesisNote
{
    IReadOnlyNotifiableProperty<double> StartTime { get; }
    IReadOnlyNotifiableProperty<double> EndTime { get; }
    IReadOnlyNotifiableProperty<int> Pitch { get; }
    IReadOnlyNotifiableProperty<string> Lyric { get; }
    IReadOnlyNotifiableProperty<IReadOnlyList<PinnedPhoneme>> Phonemes { get; }
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 邻居链（协同发音用）。活视图上仅供数据线程的分片决策 live 导航——事件 handler 内
    // 只有 note 自身引用、无列表索引上下文，O(1) 邻居导航是真实便利；
    // 合成须在快照（snapshot.Notes 有序列表按索引）上导航，不回活对象。
    ISynthesisNote? Next { get; }
    ISynthesisNote? Last { get; }
}
