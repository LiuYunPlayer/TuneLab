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
    public List<PhonemeInfo> Phonemes { get; set; } = new();
    // 前置量（拍前发声量，自然秒）：note 头之前钉死音素的占位长度，决定拍前 / 拍后归属（见 PhonemeLayout）。
    // 仅在 Phonemes 非空时有意义；默认 0（元音起手 / 无钉死）。
    public double Preutterance { get; set; }
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
