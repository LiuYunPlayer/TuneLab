using System.Collections.Generic;
using System.Linq;
using Old = TuneLab.Extensions.Formats.DataInfo;
using New = TuneLab.Core.DataInfo;
using TuneLab.Foundation.Property;

namespace ExtensionCompatibilityLayer.Format;

internal static class FormatConverter
{
    // ProjectInfo
    public static New.ProjectInfo ToCoreFormat(this Old.ProjectInfo oldObj) => new()
    {
        Tempos = oldObj.Tempos.Select(t => t.ToCoreFormat()).ToList(),
        TimeSignatures = oldObj.TimeSignatures.Select(t => t.ToCoreFormat()).ToList(),
        Tracks = oldObj.Tracks.Select(t => t.ToCoreFormat()).ToList()
    };
    public static Old.ProjectInfo ToOldFormat(this New.ProjectInfo newObj) => new()
    {
        Tempos = newObj.Tempos.Select(t => t.ToOldFormat()).ToList(),
        TimeSignatures = newObj.TimeSignatures.Select(t => t.ToOldFormat()).ToList(),
        Tracks = newObj.Tracks.Select(t => t.ToOldFormat()).ToList()
    };

    // TempoInfo
    public static New.TempoInfo ToCoreFormat(this Old.TempoInfo oldObj) => new()
    {
        Pos = oldObj.Pos,
        Bpm = oldObj.Bpm
    };
    public static Old.TempoInfo ToOldFormat(this New.TempoInfo newObj) => new()
    {
        Pos = newObj.Pos,
        Bpm = newObj.Bpm
    };

    // TimeSignatureInfo
    public static New.TimeSignatureInfo ToCoreFormat(this Old.TimeSignatureInfo oldObj) => new()
    {
        BarIndex = oldObj.BarIndex,
        Numerator = oldObj.Numerator,
        Denominator = oldObj.Denominator
    };
    public static Old.TimeSignatureInfo ToOldFormat(this New.TimeSignatureInfo newObj) => new()
    {
        BarIndex = newObj.BarIndex,
        Numerator = newObj.Numerator,
        Denominator = newObj.Denominator
    };

    // TrackInfo
    public static New.TrackInfo ToCoreFormat(this Old.TrackInfo oldObj) => new()
    {
        Name = oldObj.Name,
        Gain = oldObj.Gain,
        Pan = oldObj.Pan,
        Mute = oldObj.Mute,
        Solo = oldObj.Solo,
        AsRefer = oldObj.AsRefer,
        Color = oldObj.Color,
        Parts = oldObj.Parts.Select(p => p.ToCoreFormat()).ToList()
    };
    public static Old.TrackInfo ToOldFormat(this New.TrackInfo newObj) => new()
    {
        Name = newObj.Name,
        Gain = newObj.Gain,
        Pan = newObj.Pan,
        Mute = newObj.Mute,
        Solo = newObj.Solo,
        AsRefer = newObj.AsRefer,
        Color = newObj.Color,
        Parts = newObj.Parts.Select(p => p.ToOldFormat()).ToList()
    };

    // PartInfo 多态
    public static New.PartInfo ToCoreFormat(this Old.PartInfo oldObj)
    {
        return oldObj switch
        {
            Old.MidiPartInfo m => m.ToCoreFormat(),
            Old.AudioPartInfo a => a.ToCoreFormat(),
            _ => throw new System.NotSupportedException($"未知PartInfo类型: {oldObj.GetType().FullName}")
        };
    }
    public static Old.PartInfo ToOldFormat(this New.PartInfo newObj)
    {
        return newObj switch
        {
            New.MidiPartInfo m => m.ToOldFormat(),
            New.AudioPartInfo a => a.ToOldFormat(),
            _ => throw new System.NotSupportedException($"未知PartInfo类型: {newObj.GetType().FullName}")
        };
    }

    // MidiPartInfo
    public static New.MidiPartInfo ToCoreFormat(this Old.MidiPartInfo oldObj) => new()
    {
        Name = oldObj.Name,
        Pos = oldObj.Pos,
        Dur = oldObj.Dur,
        Gain = oldObj.Gain,
        Voice = oldObj.Voice.ToCoreFormat(),
        Notes = oldObj.Notes.Select(n => n.ToCoreFormat()).ToList(),
        Automations = oldObj.Automations.ToCoreFormat(a => a.ToCoreFormat()),
        Pitch = oldObj.Pitch.Select(list => list.ToCoreFormat()).ToList(),
        Vibratos = oldObj.Vibratos.Select(v => v.ToCoreFormat()).ToList(),
        Properties = oldObj.Properties.ToCoreFormat(),
        Effects = new List<New.EffectInfo>() // 旧版无Effects，置空
    };
    public static Old.MidiPartInfo ToOldFormat(this New.MidiPartInfo newObj) => new()
    {
        Name = newObj.Name,
        Pos = newObj.Pos,
        Dur = newObj.Dur,
        Gain = newObj.Gain,
        Voice = newObj.Voice.ToOldFormat(),
        Notes = newObj.Notes.Select(n => n.ToOldFormat()).ToList(),
        Automations = newObj.Automations.ToOldFormat(a => a.ToOldFormat()),
        Pitch = newObj.Pitch.Select(list => list.ToOldFormat()).ToList(),
        Vibratos = newObj.Vibratos.Select(v => v.ToOldFormat()).ToList(),
        Properties = newObj.Properties.ToOldFormat()
        // 旧版无Effects
    };

    // AudioPartInfo
    public static New.AudioPartInfo ToCoreFormat(this Old.AudioPartInfo oldObj) => new()
    {
        Name = oldObj.Name,
        Pos = oldObj.Pos,
        Dur = oldObj.Dur,
        Path = oldObj.Path
    };
    public static Old.AudioPartInfo ToOldFormat(this New.AudioPartInfo newObj) => new()
    {
        Name = newObj.Name,
        Pos = newObj.Pos,
        Dur = newObj.Dur,
        Path = newObj.Path
    };

    // NoteInfo
    public static New.NoteInfo ToCoreFormat(this Old.NoteInfo oldObj) => new()
    {
        Pos = oldObj.Pos,
        Dur = oldObj.Dur,
        Pitch = oldObj.Pitch,
        Lyric = oldObj.Lyric,
        Pronunciation = oldObj.Pronunciation,
        Properties = oldObj.Properties.ToCoreFormat(),
        Phonemes = oldObj.Phonemes.Select(p => p.ToCoreFormat()).ToList()
    };
    public static Old.NoteInfo ToOldFormat(this New.NoteInfo newObj) => new()
    {
        Pos = newObj.Pos,
        Dur = newObj.Dur,
        Pitch = newObj.Pitch,
        Lyric = newObj.Lyric,
        Pronunciation = newObj.Pronunciation,
        Properties = newObj.Properties.ToOldFormat(),
        Phonemes = newObj.Phonemes.Select(p => p.ToOldFormat()).ToList()
    };

    // PhonemeInfo
    public static New.PhonemeInfo ToCoreFormat(this Old.PhonemeInfo oldObj) => new()
    {
        StartTime = oldObj.StartTime,
        EndTime = oldObj.EndTime,
        Symbol = oldObj.Symbol
    };
    public static Old.PhonemeInfo ToOldFormat(this New.PhonemeInfo newObj) => new()
    {
        StartTime = newObj.StartTime,
        EndTime = newObj.EndTime,
        Symbol = newObj.Symbol
    };

    // VibratoInfo
    public static New.VibratoInfo ToCoreFormat(this Old.VibratoInfo oldObj) => new()
    {
        Pos = oldObj.Pos,
        Dur = oldObj.Dur,
        Frequency = oldObj.Frequency,
        Phase = oldObj.Phase,
        Amplitude = oldObj.Amplitude,
        Attack = oldObj.Attack,
        Release = oldObj.Release,
        AffectedAutomations = oldObj.AffectedAutomations.ToCoreFormat()
    };
    public static Old.VibratoInfo ToOldFormat(this New.VibratoInfo newObj) => new()
    {
        Pos = newObj.Pos,
        Dur = newObj.Dur,
        Frequency = newObj.Frequency,
        Phase = newObj.Phase,
        Amplitude = newObj.Amplitude,
        Attack = newObj.Attack,
        Release = newObj.Release,
        AffectedAutomations = newObj.AffectedAutomations.ToOldFormat()
    };

    // VoiceInfo
    public static New.VoiceInfo ToCoreFormat(this Old.VoiceInfo oldObj) => new()
    {
        Type = oldObj.Type,
        ID = oldObj.ID
    };
    public static Old.VoiceInfo ToOldFormat(this New.VoiceInfo newObj) => new()
    {
        Type = newObj.Type,
        ID = newObj.ID
    };

    // AutomationInfo
    public static New.AutomationInfo ToCoreFormat(this Old.AutomationInfo oldObj) => new()
    {
        DefaultValue = oldObj.DefaultValue,
        Points = oldObj.Points.Select(p => p.ToCoreFormat()).ToList()
    };
    public static Old.AutomationInfo ToOldFormat(this New.AutomationInfo newObj) => new()
    {
        DefaultValue = newObj.DefaultValue,
        Points = newObj.Points.Select(p => p.ToOldFormat()).ToList()
    };
}
