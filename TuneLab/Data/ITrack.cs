using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Data;
using TuneLab.Base.Science;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal interface ITrack : IDataObject<TrackInfo>, IAudioTrack
{
    IProject Project { get; }
    ITempoManager TempoManager { get; }
    ITimeSignatureManager TimeSignatureManager { get; }
    IDataProperty<string> Name { get; }
    new IDataProperty<bool> IsMute { get; }
    new IDataProperty<bool> IsSolo { get; }
    new IDataProperty<bool> IsGuide { get; }
    IDataProperty<double> Gain { get; }
    new IDataProperty<double> Pan { get; }
    IReadOnlyDataObjectLinkedList<IPart> Parts { get; }

    void InsertPart(IPart part);
    bool RemovePart(IPart part);
    MidiPart CreatePart(MidiPartInfo info);
    AudioPart CreatePart(AudioPartInfo info);
    Part CreatePart(PartInfo info);

    IDataProperty<string> Color { get; }

    Color GetColor()
    {
        if(Avalonia.Media.Color.TryParse(Color.Value,out var color)) return color;
        return Avalonia.Media.Color.Parse("#3A3F69");
    }

    void Activate();
    void Deactivate();

    bool IAudioTrack.IsMute => IsMute.Value;
    bool IAudioTrack.IsSolo => IsSolo.Value;
    double IAudioTrack.Volume => MusicTheory.dB2Level(Gain.Value);
    double IAudioTrack.Pan => Pan.Value;
    double IAudioTrack.EndTime => Parts.MaxBy(part => part.EndTime())?.EndTime() ?? 0;
    IEnumerable<IAudioSource> IAudioTrack.AudioSources => Parts;
}