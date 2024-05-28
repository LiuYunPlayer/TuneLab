using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Structures;
using TuneLab.Utils;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class Vibrato : DataObject, IDataObject<VibratoInfo>, ISelectable
{
    public IActionEvent<double, double> RangeModified => mRangeModified;
    public IActionEvent SelectionChanged => mSelectionChanged;
    public IMidiPart Part => mPart;
    public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }
    public DataStruct<double> Pos { get; } = new();
    public DataStruct<double> Dur { get; } = new();
    public DataStruct<double> Frequency { get; } = new();
    public DataStruct<double> Amplitude { get; } = new();
    public DataStruct<double> Phase { get; } = new();
    public DataStruct<double> Attack { get; } = new();
    public DataStruct<double> Release { get; } = new();
    public DataMap<string, double> AffectedAutomations { get; } = new();

    public Vibrato(IMidiPart part)
    {
        mPart = part;
        mMergeHandler = new(NotifyRangeModified);
        Pos.Attach(this);
        Dur.Attach(this);
        Frequency.Attach(this);
        Amplitude.Attach(this);
        Phase.Attach(this);
        Attack.Attach(this);
        Release.Attach(this);
        AffectedAutomations.Attach(this);
        Modified.Subscribe(mMergeHandler.Trigger);
    }

    public void BeginRangeModify()
    {
        if (!mMergeHandler.IsMerging)
        {
            NotifyRangeModified();
        }

        mMergeHandler.Begin();
    }

    public void EndRangeModify()
    {
        mMergeHandler.End();
    }

    public VibratoInfo GetInfo()
    {
        return new VibratoInfo()
        {
            Pos = Pos,
            Dur = Dur,
            Frequency = Frequency,
            Amplitude = Amplitude,
            Phase = Phase,
            Attack = Attack,
            Release = Release,
            AffectedAutomations = AffectedAutomations.GetInfo()
        };
    }

    void IDataObject<VibratoInfo>.SetInfo(VibratoInfo info)
    {
        IDataObject<VibratoInfo>.SetInfo(Pos, info.Pos);
        IDataObject<VibratoInfo>.SetInfo(Dur, info.Dur);
        IDataObject<VibratoInfo>.SetInfo(Frequency, info.Frequency);
        IDataObject<VibratoInfo>.SetInfo(Amplitude, info.Amplitude);
        IDataObject<VibratoInfo>.SetInfo(Phase, info.Phase);
        IDataObject<VibratoInfo>.SetInfo(Attack, info.Attack);
        IDataObject<VibratoInfo>.SetInfo(Release, info.Release);
        IDataObject<VibratoInfo>.SetInfo(AffectedAutomations, info.AffectedAutomations);
    }

    void NotifyRangeModified()
    {
        mRangeModified.Invoke(this.StartPos(), this.EndPos());
    }

    bool mIsSelected = false;

    readonly MergeHandler mMergeHandler;
    readonly ActionEvent<double, double> mRangeModified = new();
    readonly ActionEvent mSelectionChanged = new();
    readonly IMidiPart mPart;
}

internal static class VibratoExtension
{
    public static double StartPos(this Vibrato vibrato)
    {
        return vibrato.Pos;
    }

    public static double EndPos(this Vibrato vibrato)
    {
        return vibrato.Pos + vibrato.Dur;
    }

    public static double GlobalStartPos(this Vibrato vibrato)
    {
        return vibrato.Part.Pos.Value + vibrato.StartPos();
    }

    public static double GlobalEndPos(this Vibrato vibrato)
    {
        return vibrato.Part.Pos.Value + vibrato.EndPos();
    }

    public static double GlobalStartTime(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTime(vibrato.GlobalStartPos());
    }

    public static double GlobalEndTime(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTime(vibrato.GlobalEndPos());
    }

    public static double GlobalAttackTime(this Vibrato vibrato)
    {
        return vibrato.GlobalStartTime() + vibrato.Attack;
    }

    public static double GlobalReleaseTime(this Vibrato vibrato)
    {
        return vibrato.GlobalEndTime() - vibrato.Release;
    }

    public static double GlobalAttackTick(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTick(vibrato.GlobalAttackTime());
    }

    public static double GlobalReleaseTick(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTick(vibrato.GlobalReleaseTime());
    }
}
