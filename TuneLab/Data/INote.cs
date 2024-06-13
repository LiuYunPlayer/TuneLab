using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;
using TuneLab.Utils;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal interface INote : IDataObject<NoteInfo>, ISelectable, ILinkedNode<INote>, ISynthesisNote
{
    new INote? Next { get; }
    new INote? Last { get; }
    IMidiPart Part { get; }
    IDataProperty<double> Pos { get; }
    IDataProperty<double> Dur { get; }
    new IDataProperty<int> Pitch { get; }
    new IDataProperty<string> Lyric { get; }
    IDataProperty<string> Pronunciation { get; }
    new DataPropertyObject Properties { get; }
    new IDataObjectList<IPhoneme> Phonemes { get; }
    SynthesizedPhoneme[]? SynthesizedPhonemes { get; set; }
    IReadOnlyCollection<string> Pronunciations { get; }

    INote? NextInSegment { get; set; }
    INote? LastInSegment { get; set; }

    int ISynthesisNote.Pitch => Pitch.Value;
    string ISynthesisNote.Lyric => this.FinalPronunciation() ?? Lyric.Value;
    PropertyObject ISynthesisNote.Properties => new(Properties);
    IReadOnlyList<SynthesizedPhoneme> ISynthesisNote.Phonemes => Phonemes.Convert(GetPhoneme);
    ISynthesisNote? ISynthesisNote.Next => NextInSegment;
    ISynthesisNote? ISynthesisNote.Last => LastInSegment;
    double ISynthesisNote.StartTime => Part.TempoManager.GetTime(this.GlobalStartPos());
    double ISynthesisNote.EndTime => Part.TempoManager.GetTime(this.GlobalEndPos());
    private double PhonemeStartTime => Phonemes.IsEmpty() ? 0 : Phonemes.ConstFirst().StartTime.Value;
    private double PhonemeEndTime => Phonemes.IsEmpty() ? 0 : Phonemes.ConstLast().EndTime.Value;
    public double StartPhonemeRatio 
    { 
        get 
        { 
            if (LastInSegment == null) 
                return 1;

            double all = -PhonemeStartTime; 
            if (all <= 0) 
                return 1; 

            return Math.Min(1, (StartTime - LastInSegment.StartTime) / all); 
        } 
    }
    public double EndPhonemeRatio 
    { 
        get 
        { 
            double all = PhonemeEndTime; 
            if (all <= 0) 
                return 1;

            double end = EndTime; 
            if (NextInSegment != null)
                end = (NextInSegment.Phonemes.IsEmpty() ? 
                    (NextInSegment.SynthesizedPhonemes == null || NextInSegment.SynthesizedPhonemes.IsEmpty() ? NextInSegment.StartTime : NextInSegment.SynthesizedPhonemes.ConstFirst().StartTime) : 
                    NextInSegment.PhonemeStartTime + NextInSegment.StartTime).Limit(StartTime, EndTime);

            return (end - StartTime) / all;
        } 
    }

    private SynthesizedPhoneme GetPhoneme(IPhoneme phoneme)
    {
        double ConvertTime(double time)
        {
            return StartTime + time * (time <= 0 ? StartPhonemeRatio : EndPhonemeRatio);
        }

        return new SynthesizedPhoneme()
        {
            StartTime = ConvertTime(phoneme.StartTime.Value),
            EndTime = ConvertTime(phoneme.EndTime.Value),
            Symbol = phoneme.Symbol.Value
        };
    }
}

internal static class INoteExtension
{
    public static double StartPos(this INote note)
    {
        return note.Pos.Value;
    }

    public static double EndPos(this INote note)
    {
        return note.Pos.Value + note.Dur.Value;
    }

    public static double GlobalStartPos(this INote note)
    {
        return note.Part.Pos.Value + note.StartPos();
    }

    public static double GlobalEndPos(this INote note)
    {
        return note.Part.Pos.Value + note.EndPos();
    }

    public static INote SplitAt(this INote note, double pos)
    {
        note.Part.BeginMergeDirty();
        var newNote = note.Part.CreateNote(new NoteInfo() { Pos = pos, Dur = note.EndPos() - pos, Pitch = note.Pitch.Value, Lyric = "-" });
        note.Dur.Set(pos - note.StartPos());
        note.Part.InsertNote(newNote);
        note.Part.EndMergeDirty();
        note.Part.Notes.DeselectAllItems();
        return newNote;
    }

    public static void LockPhonemes(this INote note)
    {
        if (!note.Phonemes.IsEmpty())
            return;

        if (note.SynthesizedPhonemes == null)
            return;

        if (note.SynthesizedPhonemes.IsEmpty())
            return;

        var startTime = note.StartTime;
        foreach (var phoneme in note.SynthesizedPhonemes)
        {
            note.Phonemes.Add(Phoneme.Create(new PhonemeInfo() { StartTime = phoneme.StartTime - startTime, EndTime = phoneme.EndTime - startTime, Symbol = phoneme.Symbol }));
        }
    }

    public static string? FinalPronunciation(this INote note)
    {
        if (!string.IsNullOrEmpty(note.Pronunciation.Value))
            return note.Pronunciation.Value;

        return note.Pronunciations.FirstOrDefault();
    }
}
