using TuneLab.Foundation;

namespace TuneLab.SDK;

public class NoteInfo
{
    // 位置 / 时长，单位 = tick（PPQ 480，即每四分音符 480 tick）。Pos 相对所属 part 的锚点（PartInfo.Pos），非全局。
    public required double Pos { get; set; }
    public required double Dur { get; set; }
    // 音高 = MIDI note number（60 = C4）。
    public required int Pitch { get; set; }
    public string Lyric { get; set; } = string.Empty;
    public string Pronunciation { get; set; } = string.Empty;
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
    // 钉死音素的结构化双列表：引导（核前前置辅音）/ 主体（核 + 尾辅音），时间序。两者皆空 = 非钉死。
    public List<PhonemeInfo> LeadingPhonemes { get; set; } = new();
    public List<PhonemeInfo> BodyPhonemes { get; set; } = new();
    // 主体起点（= 两列表结合线）相对 note 头的有符号偏移，单位 = 秒（左负右正）：junction = noteStart + BodyOffset。
    // 注意与 Pos/Dur 的 tick 不同——音素域一律用秒（同 PhonemeInfo.Duration）。仅在有音素时有意义；默认 0（元音起手 / 无钉死）。
    public double BodyOffset { get; set; }

    // 全序列 = LeadingPhonemes 后接 BodyPhonemes（时间序）。刻意不设计算属性：DTO 上的派生属性会被反射式
    // 序列化器（如 System.Text.Json）当数据写出、与两个源列表重复。消费方直接 LeadingPhonemes.Concat(BodyPhonemes) 即可。
}

public static class NoteInfoExtension
{
    public static double StartPos(this NoteInfo info)
    {
        return info.Pos;
    }

    public static double EndPos(this NoteInfo info)
    {
        return info.Pos + info.Dur;
    }
}
