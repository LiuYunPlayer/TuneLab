using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Science;
using TuneLab.SDK.Format.DataInfo;
using TuneLab.Foundation.Utils;
using TuneLab.SDK.Base.Timing;
using System;

namespace TuneLab.Data;

internal class TempoManager : DataObject, ITempoManager
{
    public IProject Project => mProject;
    public IReadOnlyList<ITempo> Tempos => mTempos;

    public const double DefaultBpm = 120;

    public TempoManager(IProject project, double bpm = DefaultBpm) : this(project, [new() { Pos = 0, Bpm = bpm }]) { }

    public TempoManager(IProject project, List<TempoInfo> tempos) : base(project)
    {
        mTempos = new(this);
        mProject = project;
        // 任何变更（含 undo/redo 与拖动中间态）都让换算快照失效，下次取用时惰性重建。
        Modified.Subscribe((bool _) => mSnapshot = null);
        SetInfo(tempos);
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

        EndMergeNotify();
        return result;
    }

    public void RemoveTempoAt(int index)
    {
        if ((uint)index >= Tempos.Count)
            return;

        mTempos.RemoveAt(index);
    }

    public void SetBpm(int index, double bpm)
    {
        if ((uint)index >= Tempos.Count)
            return;

        mTempos[index].Bpm.Set(bpm);
    }

    public double[] GetTimes(IReadOnlyList<double> ticks) => Snapshot.ToSeconds(ticks);
    public double[] GetTicks(IReadOnlyList<double> times) => Snapshot.ToTick(times);
    public double GetTick(double time) => Snapshot.ToTick(time);
    public double GetTime(double tick) => Snapshot.ToSeconds(tick);

    // 快照即换算实现：live 侧用的就是惰性重建的缓存，捕获时直接共享（不可变，零拷贝）。
    public TempoSnapshot CreateSnapshot() => Snapshot;

    public List<TempoInfo> GetInfo()
    {
        return mTempos.GetInfo().ToInfo();
    }

    public void SetInfo(List<TempoInfo> info)
    {
        if (info.Count == 0)
            info = [new() { Pos = 0, Bpm = DefaultBpm }];

        using var _ = MergeNotify();
        mTempos.SetInfo(info.Convert(t => new TempoForTempoManager(t)).ToArray());
    }

    TempoSnapshot Snapshot => mSnapshot ??= new TempoSnapshot(mTempos.Convert(t => new TempoMark(t.Pos, t.Bpm)), MusicTheory.RESOLUTION);

    TempoSnapshot? mSnapshot;
    readonly DataObjectList<TempoForTempoManager> mTempos;
    readonly IProject mProject;

    class TempoForTempoManager : DataObject, ITempo
    {
        public DataStruct<double> Pos { get; } = new();
        public DataStruct<double> Bpm { get; } = new();

        double ITempo.Pos => Pos;
        double ITempo.Bpm => Bpm;

        public TempoForTempoManager(TempoInfo info)
        {
            Pos.Attach(this);
            Bpm.Attach(this);
            SetInfo(info);
        }

        public TempoInfo GetInfo() => new() { Pos = Pos, Bpm = Bpm };

        public void SetInfo(TempoInfo info)
        {
            using var _ = MergeNotify();
            Pos.SetInfo(info.Pos);
            Bpm.SetInfo(info.Bpm);
        }
    }
}
