using TuneLab.SDK.Base.Property;

namespace TuneLab.SDK.Format.DataInfo;

public class NoteInfo_V1
{
    public required double Pos { get; set; }
    public required double Dur { get; set; }
    public required int Pitch { get; set; }
    public required string Lyric { get; set; }
    public string Pronunciation { get; set; } = string.Empty;
    public PropertyObject_V1 Properties { get; set; } = [];
    public List<PhonemeInfo_V1> Phonemes { get; set; } = [];
}
