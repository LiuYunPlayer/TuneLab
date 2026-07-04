using TuneLab.Foundation;

namespace TuneLab.SDK;

// 会话活视图中的 note（可订阅，仅数据线程）。固定字段保持最小（通用乐理属性）；
// voice 专属 per-note 参数一律走 Properties（keyed）——加新参数 = 加会话声明 NoteProperties
// 的 key，不动本接口固定面。
//
// 时间量为全局秒（插件侧统一秒轴，与音频产物同系）：note 边界是 tempo 表派生的秒值。
// 时基变更（tempo 表 / part 平移）**不走增量通知**——宿主整体重建会话（含 context），新会话
// 读到新秒值；边界 Modified 只在 note 自身编辑时触发。tick 是宿主乐谱内部表示、不外露。
public interface IVoiceSynthesisNote
{
    IReadOnlyNotifiableProperty<double> StartTime { get; }
    IReadOnlyNotifiableProperty<double> EndTime { get; }
    IReadOnlyNotifiableProperty<int> Pitch { get; }
    IReadOnlyNotifiableProperty<string> Lyric { get; }
    IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> Phonemes { get; }
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 延续标志（宿主拥有的稳定契约）：true = 本 note 是**生效的延续**——延音符且经不断裂的相接链回溯到发声 note
    // （连音 / melisma 乘客）。孤儿延音符（被空隙断链）为 false，故读本标志即与宿主显示一致、不会把前元音误铺进静音。
    // 判据规则宿主独占、可演进，插件读本标志不自行匹配歌词记号。
    // **普通只读字段、无独立通知**：它是 Lyric + 相接(位置)的派生量，要响应其变化请订阅 Lyric / StartTime / EndTime。
    bool IsContinuation { get; }

    // 邻居链（协同发音用）。活视图上仅供数据线程的分片决策 live 导航——事件 handler 内
    // 只有 note 自身引用、无列表索引上下文，O(1) 邻居导航是真实便利；
    // 合成须在快照（snapshot.Notes 有序列表按索引）上导航，不回活对象。
    IVoiceSynthesisNote? Next { get; }
    IVoiceSynthesisNote? Last { get; }
}
