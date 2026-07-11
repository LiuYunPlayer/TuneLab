using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

public class NoteInfo
{
    public required double Pos { get; set; }
    public required double Dur { get; set; }
    public required int Pitch { get; set; }
    public string Lyric { get; set; } = string.Empty;
    public string Pronunciation { get; set; } = string.Empty;
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
    // 钉死音素的结构化双列表：引导（核前前置辅音）/ 主体（核 + 尾辅音），时间序。两者皆空 = 非钉死。
    public List<PhonemeInfo> LeadingPhonemes { get; set; } = new();
    public List<PhonemeInfo> BodyPhonemes { get; set; } = new();
    // 主体起点（= 两列表结合线）相对 note 头的有符号偏移：junction = noteStart + BodyOffset（左负右正）。
    // 仅在有音素时有意义；默认 0（元音起手 / 无钉死）。
    public double BodyOffset { get; set; }

    // 全序列只读视图 = LeadingPhonemes ++ BodyPhonemes（时间序）；供只读消费者用（每次拼接）。
    public IReadOnlyList<PhonemeInfo> Phonemes => LeadingPhonemes.Concat(BodyPhonemes).ToList();
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
