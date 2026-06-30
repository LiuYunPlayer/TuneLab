using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Data.Timing;
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
        // 浠讳綍鍙樻洿锛堝惈 undo/redo 涓庢嫋鍔ㄤ腑闂存€侊級閮借鎹㈢畻蹇収澶辨晥锛屼笅娆″彇鐢ㄦ椂鎯版€ч噸寤恒€?
        Modified.AsEverytime().Subscribe((bool _) => mSnapshot = null);
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
    public double[] GetTicks(IReadOnlyList<double> times) => Snapshot.ToTicks(times);
    public double GetTick(double time) => Snapshot.ToTick(time);
    public double GetTime(double tick) => Snapshot.ToSecond(tick);

    // 蹇収鍗虫崲绠楀疄鐜帮細live 渚х敤鐨勫氨鏄儼鎬ч噸寤虹殑缂撳瓨锛屾崟鑾锋椂鐩存帴鍏变韩锛堜笉鍙彉锛岄浂鎷疯礉锛夈€?
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
