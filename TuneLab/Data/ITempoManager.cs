using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.Data.Timing;

namespace TuneLab.Data;

internal interface ITempoManager : IDataObject<List<TempoInfo>>
{
    IProject Project { get; } // TODO: Remove this
    IReadOnlyList<ITempo> Tempos { get; }
    int AddTempo(double pos, double bpm);
    void RemoveTempoAt(int index);
    void SetBpm(int index, double bpm);
    double[] GetTimes(IReadOnlyList<double> ticks);
    double[] GetTicks(IReadOnlyList<double> times);
    double GetTick(double time);
    double GetTime(double tick);
    // 不可变换算快照（合成快照物化用；live 侧缓存直接共享，零拷贝）。
    TempoSnapshot CreateSnapshot();
}

internal static class ITempoManagerExtension
{
    // 对象版（归属由集合判定）：非成员是编程错误，照 ITrack.RemovePart 的范式——DEBUG 期就地暴露、
    // Release 宽容 no-op。要点是 IndexOf 的 -1 【绝不透传】给按下标的重载：那一层越界即抛，且会把原因
    // 误报成"下标越界"，而真实原因是"这东西不属于本 manager"。
    public static void RemoveTempo(this ITempoManager manager, ITempo tempo)
    {
        int index = manager.Tempos.IndexOf(tempo);
        System.Diagnostics.Debug.Assert(index >= 0, "RemoveTempo: tempo does not belong to this manager.");
        if (index < 0)
            return;

        manager.RemoveTempoAt(index);
    }

    public static void SetBpm(this ITempoManager manager, ITempo tempo, double bpm)
    {
        int index = manager.Tempos.IndexOf(tempo);
        System.Diagnostics.Debug.Assert(index >= 0, "SetBpm: tempo does not belong to this manager.");
        if (index < 0)
            return;

        manager.SetBpm(index, bpm);
    }

    public static double GetBpmAt(this ITempoManager manager, double tick)
    {
        for (int i = manager.Tempos.Count - 1; i >= 0; i--)
        {
            var tempo = manager.Tempos[i];
            if (tempo.Pos <= tick)
                return tempo.Bpm;
        }

        return manager.Tempos[0].Bpm;
    }
}
