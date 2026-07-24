namespace TuneLab.SDK;

// 派生音符：位置说秒（自然描述，宿主转 tick），不带 Properties/Pronunciation 等创作字段。
// 音素用 DerivedPhoneme（与 DataInfo NoteInfo 的音素结构同构、不共类型）：转谱 / 强制对齐类插件能产音素级信息，
// 落成真实 MidiPart 后就是可编辑用户数据、且音素域本就是秒基，宿主逐字转换、零信息损失。
public sealed class DerivedNote
{
    // 起点 / 终点，单位 = 秒（绝对音频内容时间，与 part Time 同坐标系）。EndTime > StartTime。
    public required double StartTime { get; init; }
    public required double EndTime { get; init; }
    // 音高 = MIDI note number（60 = C4）。
    public required int Pitch { get; init; }
    // 歌词，空 = 不产（宿主用默认空歌词）。
    public string Lyric { get; init; } = string.Empty;

    // 钉死音素的结构化双列表（同构 DataInfo NoteInfo）：引导（核前前置辅音）/ 主体（核 + 尾辅音），时间序。
    // 两者皆空 = 非钉死（不产音素）。DerivedPhoneme.Duration 单位 = 秒。
    public IReadOnlyList<DerivedPhoneme> LeadingPhonemes { get; init; } = [];
    public IReadOnlyList<DerivedPhoneme> BodyPhonemes { get; init; } = [];
    // 主体起点（两列表结合线）相对 note 头的有符号偏移，单位 = 秒（同 DataInfo NoteInfo.BodyOffset）。默认 0。
    public double BodyOffset { get; init; }
}
