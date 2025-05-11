using System;
using TuneLab.Extensions.Adapters.DataStructures;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.SDK.Base.Property;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Extensions.Format.Adapters;

internal static class ImportableFormatAdapterExtensions
{
    public static Map<TKey, TValue> ToMap<TKey, TValue>(this IReadOnlyMap<TKey, TValue> dictionary) where TKey : notnull
    {
        Map<TKey, TValue> map = [];
        foreach (var item in dictionary)
        {
            map.Add(item.Key, item.Value);
        }
        return map;
    }

    public static PropertyObject Convert(this PropertyObject_V1 propertyObject_V1)
    {
        Map<string, IPropertyValue> map = [];
        foreach (var property in propertyObject_V1)
        {
            // map.Add(property.Key, property.Value.Convert());
        }
        return new PropertyObject(map);
    }

    public static PropertyValue Convert(this PropertyValue_V1 propertyValue_V1)
    {/*
        if (propertyValue_V1.IsNull)
        {
            return PropertyValue.Invalid;
        }
        else if (propertyValue_V1.ToBoolean(out var propertyBoolean_V1))
        {
            return PropertyValue.Create(propertyBoolean_V1);
        }
        else if (propertyValue_V1.ToNumber(out var propertyNumber_V1))
        {
            return PropertyValue.Create(propertyNumber_V1);
        }
        else if (propertyValue_V1.ToString(out var propertyString_V1))
        {
            return PropertyValue.Create(propertyString_V1);
        }
        else if (propertyValue_V1.ToObject(out var propertyObject_V1))
        {
            return PropertyValue.Create(propertyObject_V1.Convert());
        }
        else
        {
            return PropertyValue.Invalid;
        }*/
        return new();
    }

    public static TempoInfo Convert(this TempoInfo_V1 tempoInfo_V1)
    {
        return new TempoInfo()
        {
            Pos = tempoInfo_V1.Pos,
            Bpm = tempoInfo_V1.Bpm,
        };
    }

    public static TimeSignatureInfo Convert(this TimeSignatureInfo_V1 timeSignatureInfo_V1)
    {
        return new TimeSignatureInfo()
        {
            BarIndex = timeSignatureInfo_V1.BarIndex,
            Numerator = timeSignatureInfo_V1.Numerator,
            Denominator = timeSignatureInfo_V1.Denominator,
        };
    }

    public static VoiceInfo Convert(this VoiceInfo_V1 voiceInfo_V1)
    {
        return new VoiceInfo()
        {
            Type = voiceInfo_V1.Type,
            ID = voiceInfo_V1.ID,
        };
    }

    public static Point Convert(this AutomationPointInfo_V1 automationPointInfo_V1)
    {
        return new Point(automationPointInfo_V1.Pos, automationPointInfo_V1.Value);
    }

    public static AutomationInfo Convert(this AutomationInfo_V1 automationInfo_V1)
    {
        return new AutomationInfo()
        {
            DefaultValue = automationInfo_V1.DefaultValue,
            Points = automationInfo_V1.Points.ConvertAll(Convert),
        };
    }

    public static EffectInfo Convert(this EffectInfo_V1 effectInfo_V1)
    {
        return new EffectInfo()
        {
            Type = effectInfo_V1.Type,
            IsEnabled = effectInfo_V1.IsEnabled,
            Automations = effectInfo_V1.Automations.ToDomain().Convert(Convert).ToMap(),
            Properties = effectInfo_V1.Properties.Convert(),
        };
    }

    public static NoteInfo Convert(this NoteInfo_V1 noteInfo_V1)
    {
        return new NoteInfo()
        {
            Pos = noteInfo_V1.Pos,
            Dur = noteInfo_V1.Dur,
            Pitch = noteInfo_V1.Pitch,
            Lyric = noteInfo_V1.Lyric,
            Pronunciation = noteInfo_V1.Pronunciation,
            Properties = noteInfo_V1.Properties.Convert(),
        };
    }

    public static VibratoInfo Convert(this VibratoInfo_V1 vibratoInfo_V1)
    {
        return new VibratoInfo()
        {
            Pos = vibratoInfo_V1.Pos,
            Dur = vibratoInfo_V1.Dur,
            Frequency = vibratoInfo_V1.Frequency,
            Phase = vibratoInfo_V1.Phase,
            Amplitude = vibratoInfo_V1.Amplitude,
            Attack = vibratoInfo_V1.Attack,
            Release = vibratoInfo_V1.Release,
            AffectedAutomations = vibratoInfo_V1.AffectedAutomations.ToDomain().ToMap(),
        };
    }

    public static MidiPartInfo Convert(this MidiPartInfo_V1 midiPartInfo_V1)
    {
        return new MidiPartInfo()
        {
            Name = midiPartInfo_V1.Name,
            Pos = midiPartInfo_V1.Pos,
            Dur = midiPartInfo_V1.Dur,
            Gain = midiPartInfo_V1.Gain,
            Voice = midiPartInfo_V1.Voice.Convert(),
            Effects = midiPartInfo_V1.Effects.ConvertAll(Convert),
            Notes = midiPartInfo_V1.Notes.ConvertAll(Convert),
            Automations = midiPartInfo_V1.Automations.ToDomain().Convert(Convert).ToMap(),
            Pitch = midiPartInfo_V1.Pitch.ConvertAll(list => list.ConvertAll(Convert)),
            Vibratos = midiPartInfo_V1.Vibratos.ConvertAll(Convert),
            Properties = midiPartInfo_V1.Properties.Convert(),
        };
    }

    public static AudioPartInfo Convert(this AudioPartInfo_V1 audioPartInfo_V1)
    {
        return new AudioPartInfo()
        {
            Name = audioPartInfo_V1.Name,
            Pos = audioPartInfo_V1.Pos,
            Dur = audioPartInfo_V1.Dur,
            Path = audioPartInfo_V1.Path,
        };
    }

    public static PartInfo Convert(this PartInfo_V1 partInfo_V1)
    {
        if (partInfo_V1 is MidiPartInfo_V1 midiPartInfo_V1)
        {
            return midiPartInfo_V1.Convert();
        }
        else if (partInfo_V1 is AudioPartInfo_V1 audioPartInfo_V1)
        {
            return audioPartInfo_V1.Convert();
        }
        else
        {
            throw new NotSupportedException("Unsupported part type.");
        }
    }

    public static TrackInfo Convert(this TrackInfo_V1 trackInfo_V1)
    {
        return new TrackInfo()
        {
            Name = trackInfo_V1.Name,
            Gain = trackInfo_V1.Gain,
            Pan = trackInfo_V1.Pan,
            Mute = trackInfo_V1.Mute,
            Solo = trackInfo_V1.Solo,
            AsRefer = trackInfo_V1.AsRefer,
            Color = trackInfo_V1.Color,
            Parts = trackInfo_V1.Parts.ConvertAll(Convert),
        };
    }

    public static ProjectInfo Convert(this ProjectInfo_V1 projectInfo_V1)
    {
        return new ProjectInfo()
        {
            Tempos = projectInfo_V1.Tempos.ConvertAll(Convert),
            TimeSignatures = projectInfo_V1.TimeSignatures.ConvertAll(Convert),
            Tracks = projectInfo_V1.Tracks.ConvertAll(Convert),
        };
    }
}
