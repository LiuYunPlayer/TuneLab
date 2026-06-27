using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data;

internal class Track : DataObject, ITrack
{
    public IProject Project => mProject;
    public ITempoManager TempoManager => mProject.TempoManager;
    public ITimeSignatureManager TimeSignatureManager => mProject.TimeSignatureManager;
    public DataProperty<string> Name { get; }
    public DataProperty<bool> IsMute { get; }
    public DataProperty<bool> IsSolo { get; }
    public DataProperty<bool> AsRefer { get; }
    public DataProperty<double> Gain { get; }
    public DataProperty<double> Pan { get; }
    public DataProperty<string> Color { get; }
    public bool ExportEnabled { get; set; } = false;
    public int ExportChannels { get; set; } = 1;
    public IReadOnlyDataObjectLinkedList<IPart> Parts => mParts;

    IDataProperty<string> ITrack.Name => Name;

    IDataProperty<bool> ITrack.IsMute => IsMute;

    IDataProperty<bool> ITrack.IsSolo => IsSolo;

    IDataProperty<bool> ITrack.AsRefer => AsRefer;

    IDataProperty<double> ITrack.Gain => Gain;
    IDataProperty<double> ITrack.Pan => Pan;
    IDataProperty<string> ITrack.Color => Color;

    public Track(IProject project, TrackInfo info)
    {
        mProject = project;
        Name = new DataString(this, string.Empty);
        IsMute = new DataStruct<bool>(this);
        IsSolo = new DataStruct<bool>(this);
        AsRefer = new DataStruct<bool>(this);
        Gain = new DataStruct<double>(this);
        Pan = new DataStruct<double>(this);
        Color = new DataString(this);
        mParts = new();
        mParts.Attach(this);

        mParts.ItemAdded.Subscribe(part => { part.Track = this; part.Activate(); });
        mParts.ItemRemoved.Subscribe(part => { part.Deactivate(); });

        SetInfo(info);
    }

    public void InsertPart(IPart part)
    {
        mParts.Insert(part);
    }

    public bool RemovePart(IPart part)
    {
        // 归属由集合判定（链表反向指针）；非成员删除是编程错误，DEBUG 期就地暴露，Release 仍宽容 no-op。
        System.Diagnostics.Debug.Assert(mParts.Contains(part), "RemovePart: part 不属于本 track。");
        return mParts.Remove(part);
    }

    // 同轨内重排（改 pos/dur）：摘除→跑 mutate→按新键重插。跨轨迁移属换父，仍走显式 RemovePart/InsertPart。
    public void MovePart(IPart part, Action mutate) => mParts.Move(part, mutate);

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
            Color = Color,
            AsRefer = AsRefer,
            ExportEnabled = ExportEnabled,
            ExportChannels = ExportChannels,
            Parts = mParts.GetInfo().ToInfo()
        };
    }

    public void SetInfo(TrackInfo info)
    {
        using var _ = MergeNotify();
        Name.SetInfo(info.Name);
        IsMute.SetInfo(info.Mute);
        IsSolo.SetInfo(info.Solo);
        Gain.SetInfo(info.Gain);
        Pan.SetInfo(info.Pan);
        Color.SetInfo(info.Color);
        AsRefer.SetInfo(info.AsRefer);
        ExportEnabled = info.ExportEnabled;
        ExportChannels = info.ExportChannels;
        mParts.SetInfo(info.Parts.Convert(CreatePart).ToArray());
    }

    class PartList : SortedDataObjectLinkedList<IPart>
    {
        public PartList() : base(IsInOrder) { }

        static bool IsInOrder(IPart prev, IPart next)
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
