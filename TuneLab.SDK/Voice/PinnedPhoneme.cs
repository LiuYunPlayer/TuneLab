namespace TuneLab.SDK;

// 用户钉死的音素时长约束（host→engine 输入声明，挂在 ISynthesisNote.Phonemes，per note）。
// 命名与输出侧对仗：Pinned 进（用户约束）、Synthesized 出（引擎产物 SynthesizedPhoneme）。
// 时间为相对 note 起点的秒偏移：可编辑、随 note 平移自动跟随（偏移不变）、
// 负值表示越界到 note 之前的辅音引导。用秒（而非 tick）：音素时长是声学量，
// 应随 note 平移保持、跨 tempo 不变形。
// 钉死粒度为整 note：列表非空 = 全部音素用户钉死（引擎遵守约束）；
// 列表为空 = 引擎从 Lyric 做 G2P + 全自由定时。不支持单音素级部分钉死
// （半约束的组合空间对插件是真实负担，且宿主侧无生产者）。
public class PinnedPhoneme
{
    public string Symbol = string.Empty;
    public double StartTime;
    public double EndTime;
}
