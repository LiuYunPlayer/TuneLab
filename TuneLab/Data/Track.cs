using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Utils;
using TuneLab.Base.Utils;
using Avalonia.Media;

namespace TuneLab.Data;

internal class Track : DataObject, ITrack
{
    public IProject Project => mProject;
    public ITempoManager TempoManager => mProject.TempoManager;
    public ITimeSignatureManager TimeSignatureManager => mProject.TimeSignatureManager;
    public DataProperty<string> Name { get; }
    public DataProperty<bool> IsMute { get; }
    public DataProperty<bool> IsSolo { get; }
    public DataProperty<double> Gain { get; }
    public DataProperty<double> Pan { get; }
    public DataProperty<Color> Color { get; }
    public IReadOnlyDataObjectLinkedList<IPart> Parts => mParts;

    IDataProperty<string> ITrack.Name => Name;

    IDataProperty<bool> ITrack.IsMute => IsMute;

    IDataProperty<bool> ITrack.IsSolo => IsSolo;

    IDataProperty<double> ITrack.Gain => Gain;
    IDataProperty<double> ITrack.Pan => Pan;
    IDataProperty<Color> ITrack.Color => Color;

    public Track(IProject project, TrackInfo info)
    {
        mProject = project;
        Name = new DataString(this, string.Empty);
        IsMute = new DataStruct<bool>(this);
        IsSolo = new DataStruct<bool>(this);
        Gain = new DataStruct<double>(this);
        Pan = new DataStruct<double>(this);
        Color = new DataStruct<Color>(this);
        mParts = new();
        mParts.Attach(this);

        mParts.ItemAdded.Subscribe(part => { part.Track = this; part.Activate(); });
        mParts.ItemRemoved.Subscribe(part => { part.Deactivate(); });

        IDataObject<TrackInfo>.SetInfo(this, info);
    }

    public void InsertPart(IPart part)
    {
        mParts.Insert(part);
    }

    public bool RemovePart(IPart part)
    {
        return mParts.Remove(part);
    }

    public MidiPart CreatePart(MidiPartInfo info)
    {
        return new MidiPart(this, info);
    }

    public AudioPart CreatePart(AudioPartInfo info)
    {
        return new AudioPart(this, info);
    }

    public Part CreatePart(PartInfo info)
    {
        if (info is MidiPartInfo midiPartInfo)
            return CreatePart(midiPartInfo);
        else if (info is AudioPartInfo audioPartInfo)
            return CreatePart(audioPartInfo);

        throw new ArgumentException();
    }

    public void Activate()
    {
        AudioEngine.AddTrack(this);
        foreach (var part in mParts)
        {
            part.Activate();
        }
    }

    public void Deactivate()
    {
        AudioEngine.RemoveTrack(this);
        foreach (var part in mParts)
        {
            part.Deactivate();
        }
    }

    public TrackInfo GetInfo()
    {
        return new()
        {
            Name = Name,
            Mute = IsMute,
            Solo = IsSolo,
            Gain = Gain,
            Pan = Pan,
            Color = System.Drawing.Color.FromArgb(Color.Value.A,Color.Value.R,Color.Value.G,Color.Value.B),
            Parts = mParts.GetInfo().ToInfo()
        };
    }

    void IDataObject<TrackInfo>.SetInfo(TrackInfo info)
    {
        IDataObject<TrackInfo>.SetInfo(Name, info.Name);
        IDataObject<TrackInfo>.SetInfo(IsMute, info.Mute);
        IDataObject<TrackInfo>.SetInfo(IsSolo, info.Solo);
        IDataObject<TrackInfo>.SetInfo(Gain, info.Gain);
        IDataObject<TrackInfo>.SetInfo(Pan, info.Pan);
        IDataObject<TrackInfo>.SetInfo(mParts, info.Parts.Convert(CreatePart).ToArray());
        IDataObject<TrackInfo>.SetInfo(Color, new Color(info.Color.A,info.Color.R,info.Color.G,info.Color.B));
    }

    class PartList : DataObjectLinkedList<IPart>
    {
        protected override bool IsInOrder(IPart prev, IPart next)
        {
            if (prev.StartPos() < next.StartPos())
                return true;

            if (prev.StartPos() > next.StartPos())
                return false;

            if (prev.EndPos() > next.EndPos())
                return true;

            if (prev.EndPos() < next.EndPos())
                return false;

            return true;
        }
    }

    readonly IProject mProject;
    readonly PartList mParts;
}
