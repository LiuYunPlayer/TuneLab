using System;
using System.Collections.Generic;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Utils;

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
}

internal static class ITempoManagerExtension
{
    public static void RemoveTempo(this ITempoManager manager, ITempo tempo)
    {
        manager.RemoveTempoAt(manager.Tempos.IndexOf(tempo));
    }

    public static void SetBpm(this ITempoManager manager, ITempo tempo, double bpm)
    {
        manager.SetBpm(manager.Tempos.IndexOf(tempo), bpm);
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
