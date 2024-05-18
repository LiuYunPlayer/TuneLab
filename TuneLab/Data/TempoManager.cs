using System.Collections.Generic;
using System.Linq;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Base.Utils;
using System;

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
        IDataObject<IReadOnlyList<TempoInfo>>.SetInfo(this, tempos);
    }

    public int AddTempo(double pos, double bpm)
    {
        pos = Math.Max(pos, Tempos[0].Pos.Value);

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
        return result;
    }

    public void RemoveTempoAt(int index)
    {
        if (index < 0 || index >= mTempos.Count)
            return;

        mTempos.RemoveAt(index);
        CorrectStatusFrom(index);
    }

    public void SetBpm(int index, double bpm)
    {
        if (index < 0 || index >= Tempos.Count)
            return;

        mTempos[index].Bpm.Set(bpm);
        CorrectStatusFrom(index + 1);
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

        IDataObject<IReadOnlyList<TempoInfo>>.SetInfo(mTempos, info.Convert(t => new TempoForTempoManager(t)).ToArray());
        CorrectStatusFrom(0);
    }

    IReadOnlyList<ITempoHelper> ITempoCalculatorHelper.Tempos => mTempos;

    readonly DataObjectList<TempoForTempoManager> mTempos;
    readonly IProject mProject;

    class TempoForTempoManager : DataObject, ITempo, ITempoHelper
    {
        public DataStruct<double> Pos { get; } = new();
        public DataStruct<double> Bpm { get; } = new();
        public double Time { get; set; }
        public double Coe { get; private set; }

        double ITempoHelper.Pos => Pos;
        double ITempoHelper.Bpm => Bpm;

        IReadOnlyDataProperty<double> ITempo.Pos => Pos;
        IReadOnlyDataProperty<double> ITempo.Bpm => Bpm;

        public TempoForTempoManager(TempoInfo info)
        {
            Pos.Attach(this);
            Bpm.Attach(this);
            Bpm.Modified.Subscribe(() => { Coe = Bpm / 60 * MusicTheory.RESOLUTION; });
            IDataObject<TempoInfo>.SetInfo(this, info);
        }

        public TempoInfo GetInfo() => new() { Pos = Pos, Bpm = Bpm };

        void IDataObject<TempoInfo>.SetInfo(TempoInfo info)
        {
            IDataObject<TempoInfo>.SetInfo(Pos, info.Pos);
            IDataObject<TempoInfo>.SetInfo(Bpm, info.Bpm);
        }
    }
}