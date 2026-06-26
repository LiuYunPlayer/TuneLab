using TuneLab.Foundation;

namespace TuneLab.SDK;

// 会话活视图中的 note（可订阅，仅数据线程）——instrument（多声部音源）专属面。
// 与 voice 的 IVoiceSynthesisNote 的实质差异（分水岭）：
//   ① EndTime = note 满末（Pos+Dur 换算的全局秒），【不钳位】——instrument 原味消费重叠几何，
//      宿主不去重叠；
//   ② 无 Lyric / 无 Phonemes——instrument 无歌词、无音素系统。
// 固定字段保持最小（通用乐理属性）；instrument 专属 per-note 参数走 Properties（keyed）。
//
// 时间量为全局秒（与音频产物同系）：边界是 tempo 表派生的秒值，tempo 变化时边界值随之改变、
// 其 StartTime/EndTime.Modified 即触发。tick 是宿主乐谱内部表示、不外露。
public interface IInstrumentSynthesisNote
{
    IReadOnlyNotifiableProperty<double> StartTime { get; }
    // 满末：Pos+Dur 换算的全局秒，不钳到邻居起点——重叠的尾巴原样保留（和弦 / 多声部）。
    IReadOnlyNotifiableProperty<double> EndTime { get; }
    IReadOnlyNotifiableProperty<int> Pitch { get; }
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 邻居链（仅数据线程的分片决策 live 导航；合成须在快照按索引导航，不回活对象）。
    // 注：instrument 序列可重叠，邻居按排序契约（StartTime 升序…）取，不代表"前一个发音结束"。
    IInstrumentSynthesisNote? Next { get; }
    IInstrumentSynthesisNote? Last { get; }
}
