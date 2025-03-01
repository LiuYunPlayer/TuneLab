using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal class TempoManager : DataObject, ITempoManager, ITempoCalculatorHelper
{
    public IProject Project => mProject;
    public IReadOnlyList<ITempo> Tempos => mTempos;

    public const double DefaultBpm = 120;

    public TempoManager(IProject project, double bpm = DefaultBpm) : this(project, [new() { Pos = 0, Bpm = bpm }]) { }

    public TempoManager(IProject project, List<TempoInfo> tempos) : base(project)
    {
        mTempos = new(this);
        mProject = project;
        IDataObject<List<TempoInfo>>.SetInfo(this, tempos);
    }

    public int AddTempo(double pos, double bpm)
    {
        pos = Math.Max(pos, Tempos[0].Pos);

        BeginMergeNotify();
        int i = mTempos.Count - 1;
        for (; i >= 0; --i)
        {
            if (mTempos[i].Pos <= pos)
                break;
        }

        int result;
        if (mTempos[i].Pos == pos)
        {
            mTempos[i].Bpm.Set(bpm);
            result = i;
        }
        else
        {
            mTempos.Insert(i + 1, new TempoForTempoManager(new() { Pos = pos, Bpm = bpm }));
            result = i + 1;
        }

        CorrectStatusFrom(i + 1);
        EndMergeNotify();
        return result;
    }

    public void RemoveTempoAt(int index)
    {
        if ((uint)index >= Tempos.Count)
            return;

        BeginMergeNotify();
        mTempos.RemoveAt(index);
        CorrectStatusFrom(index);
        EndMergeNotify();
    }

    public void SetBpm(int index, double bpm)
    {
        if ((uint)index >= Tempos.Count)
            return;

        BeginMergeNotify();
        mTempos[index].Bpm.Set(bpm);
        CorrectStatusFrom(index + 1);
        EndMergeNotify();
    }

    void CorrectStatusFrom(int index)
    {
        if (index <= 0)
        {
            mTempos[0].Time = 0;
            CorrectStatusFrom(1);
            return;
        }

        int n = mTempos.Count;
        for (int i = index; i < n; i++)
        {
            var last = mTempos[i - 1];
            var next = mTempos[i];

            mTempos[i].Time = last.Time + (next.Pos - last.Pos) / last.Coe;
        }
    }

    public double[] GetTimes(IReadOnlyList<double> ticks)
    {
        return ITempoCalculatorHelperExtension.GetTimes(this, ticks);
    }

    public double[] GetTicks(IReadOnlyList<double> times)
    {
        return ITempoCalculatorHelperExtension.GetTicks(this, times);
    }

    public double GetTick(double time)
    {
        return ITempoCalculatorHelperExtension.GetTick(this, time);
    }

    public double GetTime(double tick)
    {
        return ITempoCalculatorHelperExtension.GetTime(this, tick);
    }

    public List<TempoInfo> GetInfo()
    {
        return mTempos.GetInfo().ToInfo();
    }

    void IDataObject<List<TempoInfo>>.SetInfo(List<TempoInfo> info)
    {
        if (info.Count == 0)
            info = [new() { Pos = 0, Bpm = DefaultBpm }];

        IDataObject<List<TempoInfo>>.SetInfo(mTempos, info.Convert(t => new TempoForTempoManager(t)).ToArray());
        CorrectStatusFrom(0);
    }

    IReadOnlyList<ITempoHelper> ITempoCalculatorHelper.Tempos => mTempos;

    readonly DataObjectList<TempoForTempoManager> mTempos;
    readonly IProject mProject;

    class TempoForTempoManager : DataObject, ITempo, ITempoHelper
    {
        public DataStruct<double> Pos { get; } = new();
        public DataStruct<double> Bpm => mBpm;
        public double Time { get; set; }
        public double Coe => mBpm.Coe;

        double ITempoHelper.Pos => Pos;
        double ITempoHelper.Bpm => Bpm;

        double ITempo.Pos => Pos;
        double ITempo.Bpm => Bpm;
        BPM mBpm { get; } = new();

        public TempoForTempoManager(TempoInfo info)
        {
            Pos.Attach(this);
            Bpm.Attach(this);
            IDataObject<TempoInfo>.SetInfo(this, info);
        }

        public TempoInfo GetInfo() => new() { Pos = Pos, Bpm = Bpm };

        void IDataObject<TempoInfo>.SetInfo(TempoInfo info)
        {
            IDataObject<TempoInfo>.SetInfo(Pos, info.Pos);
            IDataObject<TempoInfo>.SetInfo(Bpm, info.Bpm);
        }

        class BPM : DataStruct<double>
        {
            public double Coe { get; private set; }

            protected override void SetInfo(double info)
            {
                base.SetInfo(info);
                Coe = info / 60 * MusicTheory.RESOLUTION;
            }
        }
    }
}