using System.Collections.Generic;
using TuneLab.Foundation.Property;

namespace TuneLab.Core.DataInfo;

public class NoteInfo
{
    public required double Pos { get; set; }
    public required double Dur { get; set; }
    public required int Pitch { get; set; }
    public string Lyric { get; set; } = string.Empty;
    public string Pronunciation { get; set; } = string.Empty;
    public PropertyObject Properties { get; set; } = [];
    public List<PhonemeInfo> Phonemes { get; set; } = new();
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
