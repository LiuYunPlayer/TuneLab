using TuneLab.Foundation;

namespace TuneLab.SDK;

// note 的不可变快照值（worker 线程、将来 worker 进程只读）：边界为全局秒、Properties 值拷。
// 触底到值类型：不含任何 live 数据对象引用（Phonemes 也须是物化时新建的副本），自包含可
// 序列化值树，为跨进程演进留路。
// 邻居导航不进本面（接口最小化）：VoiceSnapshot.Notes 即有序列表（与 GetSnapshot
// 递入的 notes 索引对齐），协同发音按索引取邻居。
// 形态 = 无参构造 + required init 属性：初始化后不可变（只写一次纪律），将来加字段
// 纯加性（新字段带默认值、不标 required，不破构造签名）。
public sealed class VoiceNoteSnapshot
{
    public required double StartTime { get; init; }
    // EndTime = 有效末（宿主去重叠后盖前钳到下一 note 起点，单声部音频口径，可重叠的尾巴已截）。
    // 宿主独占音素布局（定位 / 跨 note 压缩 / melisma），插件只该见有效末——不再暴露 note 满末。
    public required double EndTime { get; init; }
    public required int Pitch { get; init; }
    public required string Lyric { get; init; }
    // 延续标志（宿主拥有的稳定契约）：true = 本 note 是**生效的延续**——延音符且经不断裂的相接链回溯到发声 note
    // （连音 / melisma 乘客）。孤儿延音符（被空隙断链）为 false，故读本标志即与宿主一致、不会把前元音误铺进静音。
    // 判据规则宿主独占、可演进；插件读本标志判延续，不自行匹配歌词记号。加性字段、默认 false。
    public bool IsContinuation { get; init; }
    public required IReadOnlyList<VoicePhoneme> Phonemes { get; init; }
    public required PropertyObject Properties { get; init; }
}
