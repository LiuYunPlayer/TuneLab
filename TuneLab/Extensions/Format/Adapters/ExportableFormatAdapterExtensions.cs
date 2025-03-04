using System;
using System.Collections.Generic;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Extensions.Format.Adapters;

internal static class ExportableFormatAdapterExtensions
{
    public static Map_V1<TKey, TResult> ConvertToV1<TKey, TSource, TResult>(this IReadOnlyDictionary<TKey, TSource> dictionary, Func<TSource, TResult> converter) where TKey : notnull
    {
        return dictionary.Convert<Map<TKey, TResult>, TKey, TSource, TResult>(converter);
    }

    public static PropertyObject_V1 ConvertToV1(this PropertyObject propertyObject)
    {
        return propertyObject;
    }

    public static PropertyValue_V1 ConvertToV1(this PropertyValue propertyValue)
    {
        return propertyValue;
    }

    public static TempoInfo_V1 ConvertToV1(this TempoInfo tempoInfo)
    {
        return new TempoInfo_V1()
        {
            Pos = tempoInfo.Pos,
            Bpm = tempoInfo.Bpm,
        };
    }

    public static TimeSignatureInfo_V1 ConvertToV1(this TimeSignatureInfo timeSignatureInfo)
    {
        return new TimeSignatureInfo_V1()
        {
            BarIndex = timeSignatureInfo.BarIndex,
            Numerator = timeSignatureInfo.Numerator,
            Denominator = timeSignatureInfo.Denominator,
        };
    }

    public static VoiceInfo_V1 ConvertToV1(this VoiceInfo voiceInfo)
    {
        return new VoiceInfo_V1()
        {
            Type = voiceInfo.Type,
            ID = voiceInfo.ID,
        };
    }

    public static AutomationPointInfo_V1 ConvertToV1(this Point point)
    {
        return new AutomationPointInfo_V1() { Pos = point.X, Value = point.Y };
    }

    public static AutomationInfo_V1 ConvertToV1(this AutomationInfo automationInfo)
    {
        return new AutomationInfo_V1()
        {
            DefaultValue = automationInfo.DefaultValue,
            Points = automationInfo.Points.ConvertAll(ConvertToV1),
        };
    }

    public static EffectInfo_V1 ConvertToV1(this EffectInfo effectInfo)
    {
        return new EffectInfo_V1()
        {
            Type = effectInfo.Type,
            IsEnabled = effectInfo.IsEnabled,
            Automations = effectInfo.Automations.ConvertToV1(ConvertToV1),
            Properties = effectInfo.Properties.ConvertToV1(),
        };
    }

    public static NoteInfo_V1 ConvertToV1(this NoteInfo noteInfo)
    {
        return new NoteInfo_V1()
        {
            Pos = noteInfo.Pos,
            Dur = noteInfo.Dur,
            Pitch = noteInfo.Pitch,
            Lyric = noteInfo.Lyric,
            Pronunciation = noteInfo.Pronunciation,
            Properties = noteInfo.Properties.ConvertToV1(),
        };
    }

    public static VibratoInfo_V1 ConvertToV1(this VibratoInfo vibratoInfo)
    {
        return new VibratoInfo_V1()
        {
            Pos = vibratoInfo.Pos,
            Dur = vibratoInfo.Dur,
            Frequency = vibratoInfo.Frequency,
            Phase = vibratoInfo.Phase,
            Amplitude = vibratoInfo.Amplitude,
            Attack = vibratoInfo.Attack,
            Release = vibratoInfo.Release,
            AffectedAutomations = vibratoInfo.AffectedAutomations,
        };
    }

    public static MidiPartInfo_V1 ConvertToV1(this MidiPartInfo midiPartInfo_V1)
    {
        return new MidiPartInfo_V1()
        {
            Name = midiPartInfo_V1.Name,
            Pos = midiPartInfo_V1.Pos,
            Dur = midiPartInfo_V1.Dur,
            Gain = midiPartInfo_V1.Gain,
            Voice = midiPartInfo_V1.Voice.ConvertToV1(),
            Effects = midiPartInfo_V1.Effects.ConvertAll(ConvertToV1),
            Notes = midiPartInfo_V1.Notes.ConvertAll(ConvertToV1),
            Automations = midiPartInfo_V1.Automations.ConvertToV1(ConvertToV1),
            Pitch = midiPartInfo_V1.Pitch.ConvertAll(list => list.ConvertAll(ConvertToV1)),
            Vibratos = midiPartInfo_V1.Vibratos.ConvertAll(ConvertToV1),
            Properties = midiPartInfo_V1.Properties.ConvertToV1(),
        };
    }

    public static AudioPartInfo_V1 ConvertToV1(this AudioPartInfo audioPartInfo_V1)
    {
        return new AudioPartInfo_V1()
        {
            Name = audioPartInfo_V1.Name,
            Pos = audioPartInfo_V1.Pos,
            Dur = audioPartInfo_V1.Dur,
            Path = audioPartInfo_V1.Path,
        };
    }

    public static PartInfo_V1 ConvertToV1(this PartInfo partInfo)
    {
        if (partInfo is MidiPartInfo midiPartInfo)
        {
            return midiPartInfo.ConvertToV1();
        }
        else if (partInfo is AudioPartInfo audioPartInfo)
        {
            return audioPartInfo.ConvertToV1();
        }
        else
        {
            throw new NotSupportedException("Unsupported part type.");
        }
    }

    public static TrackInfo_V1 ConvertToV1(this TrackInfo trackInfo)
    {
        return new TrackInfo_V1()
        {
            Name = trackInfo.Name,
            Gain = trackInfo.Gain,
            Pan = trackInfo.Pan,
            Mute = trackInfo.Mute,
            Solo = trackInfo.Solo,
            AsRefer = trackInfo.AsRefer,
            Color = trackInfo.Color,
            Parts = trackInfo.Parts.ConvertAll(ConvertToV1),
        };
    }

    public static ProjectInfo_V1 ConvertToV1(this ProjectInfo projectInfo)
    {
        return new ProjectInfo_V1()
        {
            Tempos = projectInfo.Tempos.ConvertAll(ConvertToV1),
            TimeSignatures = projectInfo.TimeSignatures.ConvertAll(ConvertToV1),
            Tracks = projectInfo.Tracks.ConvertAll(ConvertToV1),
        };
    }
}
