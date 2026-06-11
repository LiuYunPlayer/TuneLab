namespace TuneLab.SDK.Voice;

// 音素时长输入（host→engine，挂在 ISynthesisNote.Phonemes，per note）。
// 时间为相对 note 起点的秒偏移：可编辑、随 note 平移自动跟随（偏移不变）、
// 负值表示越界到 note 之前的辅音引导。用秒（而非 tick）：音素时长是声学量，
// 应随 note 平移保持、跨 tempo 不变形。
// PinnedStart/PinnedEnd 为 null = 引擎自由智能定时；有值 = 用户钉死的约束，引擎遵守。
// 整个列表为空 = 引擎从 Lyric 做 G2P + 全自由定时。
public class PhonemeInfo
{
    public string Symbol = string.Empty;
    public double? PinnedStart;
    public double? PinnedEnd;
}
