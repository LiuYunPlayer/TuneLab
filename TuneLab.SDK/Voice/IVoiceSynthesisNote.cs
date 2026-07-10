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
    // 前置量（拍前发声量，自然秒）：note 头之前音素的占位长度，决定钉死音素的拍前 / 拍后归属（见 PhonemeLayout）。
    // 仅在 Phonemes 非空（整 note 钉死）时有意义；元音起手 / 无钉死时 = 0。
    IReadOnlyNotifiableProperty<double> Preutterance { get; }
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 延音身份不在本面：判定权完整归插件（IVoiceSynthesisSession.IsContinuation，宿主照单消费），
    // 回喂只会是实现自己的输出、纯冗余，故无此字段。实现以本面数据（Lyric / 位置 / Phonemes / 邻居导航）
    // 自行判定——SDK 刻意不提供判定实现（判定绑定合成行为，语义须实现完全自有，见会话方法注释）。

    // 邻居链（协同发音用）。活视图上仅供数据线程的分片决策 live 导航——事件 handler 内
    // 只有 note 自身引用、无列表索引上下文，O(1) 邻居导航是真实便利；
    // 合成须在快照（snapshot.Notes 有序列表按索引）上导航，不回活对象。
    IVoiceSynthesisNote? Next { get; }
    IVoiceSynthesisNote? Last { get; }
}
