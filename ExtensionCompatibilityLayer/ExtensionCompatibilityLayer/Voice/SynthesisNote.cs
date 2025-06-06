using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Voice;

namespace ExtensionCompatibilityLayer.Voice;

internal class SynthesisNote(ISynthesisNote note) : TuneLab.Extensions.Voices.ISynthesisNote
{
    public TuneLab.Extensions.Voices.ISynthesisNote? Next => ToISynthesisNote(note.Next);

    public TuneLab.Extensions.Voices.ISynthesisNote? Last => ToISynthesisNote(note.Last);

    public double StartTime => note.StartTime;

    public double EndTime => note.EndTime;

    public int Pitch => note.Pitch;

    public string Lyric => note.Lyric;

    public PropertyObject Properties => throw new NotImplementedException();

    public IReadOnlyList<TuneLab.Extensions.Voices.SynthesizedPhoneme> Phonemes => throw new NotImplementedException();

    static TuneLab.Extensions.Voices.ISynthesisNote? ToISynthesisNote(ISynthesisNote? note)
    {
        return note == null ? null : new SynthesisNote(note);
    }
}
