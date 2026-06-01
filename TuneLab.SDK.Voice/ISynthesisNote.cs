using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Voice;

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