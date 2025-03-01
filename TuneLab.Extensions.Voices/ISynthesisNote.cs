using TuneLab.Base.Properties;

namespace TuneLab.Extensions.Voices;

public interface ISynthesisNote
{
    ISynthesisNote? Next { get; }
    ISynthesisNote? Last { get; }
    double StartTime { get; }
    double EndTime { get; }
    int Pitch { get; }
    string Lyric { get; }
    PropertyObject Properties { get; }
    IReadOnlyList<SynthesizedPhoneme> Phonemes { get; }
}

public static class ISynthesisNoteExtension
{
    public static double Duration(this ISynthesisNote note)
    {
        return note.EndTime - note.StartTime;
    }
}