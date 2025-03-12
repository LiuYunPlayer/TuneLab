using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice;

public interface ISynthesisNote
{
    ISynthesisNote? Next { get; }
    ISynthesisNote? Last { get; }
    double StartTime { get; }
    double EndTime { get; }
    int Pitch { get; }
    string Lyric { get; }
    IReadOnlyMap<string, IReadOnlyPropertyValue> Properties { get; }
    IReadOnlyList<SynthesizedPhoneme> Phonemes { get; }
}

public static class ISynthesisNoteExtension
{
    public static double Duration(this ISynthesisNote note)
    {
        return note.EndTime - note.StartTime;
    }
}