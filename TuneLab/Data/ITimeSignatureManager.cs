using System.Collections.Generic;
using System.Linq;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal interface ITimeSignatureManager : IDataObject<List<TimeSignatureInfo>>
{
    IProject Project { get; } // TODO: Remove this
    IReadOnlyList<ITimeSignature> TimeSignatures { get; }

    int AddTimeSignature(int barIndex, int numerator, int denominator);
    void RemoveTimeSignatureAt(int index);
    void SetMeter(int index, int numerator, int denominator);
}

internal static class ITimeSignatureManagerExtension
{
    public static void RemoveTimeSignature(this ITimeSignatureManager manager, ITimeSignature timeSignature)
    {
        manager.RemoveTimeSignatureAt(manager.TimeSignatures.IndexOf(timeSignature));
    }

    public static void SetMeter(this ITimeSignatureManager manager, ITimeSignature timeSignature, int numerator, int denominator)
    {
        manager.SetMeter(manager.TimeSignatures.IndexOf(timeSignature), numerator, denominator);
    }

    public static MeterStatus[] GetMeterStatus(this ITimeSignatureManager manager, IReadOnlyList<double> ticks)
    {
        MeterStatus[] meters = new MeterStatus[ticks.Count];

        double firstPos = manager.TimeSignatures.First().Pos;
        int timeSignatureIndex = manager.TimeSignatures.Count - 1;
        for (int i = ticks.Count - 1; i >= 0; i--)
        {
            double pos = ticks[i];
            if (pos < firstPos)
            {
                meters[i] = new MeterStatus(manager, 0, pos);
                continue;
            }

            for (; timeSignatureIndex >= 0; timeSignatureIndex--)
            {
                if (manager.TimeSignatures[timeSignatureIndex].Pos <= pos)
                    break;
            }

            meters[i] = new MeterStatus(manager, timeSignatureIndex, pos);
        }

        return meters;
    }

    public static (int, int)[] GetBarAndBeatIndexes(this ITimeSignatureManager manager, IReadOnlyList<int> beatIndexes)
    {
        (int, int)[] barAndBeatIndexes = new (int, int)[beatIndexes.Count];

        int timeSignatureIndex = manager.TimeSignatures.Count - 1;
        for (int i = beatIndexes.Count - 1; i >= 0; i--)
        {
            int beatIndex = beatIndexes[i];
            if (beatIndex < 0)
            {
                barAndBeatIndexes[i] = (0, beatIndex);
                continue;
            }

            for (; timeSignatureIndex >= 0; timeSignatureIndex--)
            {
                if (manager.TimeSignatures[timeSignatureIndex].GlobalBeatIndex <= beatIndex)
                    break;
            }

            var last = manager.TimeSignatures[timeSignatureIndex];
            int beatMore = beatIndex - last.GlobalBeatIndex;
            barAndBeatIndexes[i] = (beatMore / last.Numerator, beatMore % last.Numerator);
        }

        return barAndBeatIndexes;
    }

    public static (int, double)[] GetBarAndBeatIndexes(this ITimeSignatureManager manager, IReadOnlyList<double> ticks)
    {
        (int, double)[] barAndBeatIndexes = new (int, double)[ticks.Count];

        int timeSignatureIndex = manager.TimeSignatures.Count - 1;
        for (int i = ticks.Count - 1; i >= 0; i--)
        {
            double pos = ticks[i];
            if (pos < 0)
            {
                barAndBeatIndexes[i] = (0, pos / manager.TimeSignatures[0].TicksPerBeat());
                continue;
            }

            for (; timeSignatureIndex >= 0; timeSignatureIndex--)
            {
                if (manager.TimeSignatures[timeSignatureIndex].GlobalBeatIndex <= pos)
                    break;
            }

            var last = manager.TimeSignatures[timeSignatureIndex];
            double beatMore = pos / last.TicksPerBeat() - last.GlobalBeatIndex;
            barAndBeatIndexes[i] = ((int)(beatMore / last.Numerator), beatMore % last.Numerator);
        }

        return barAndBeatIndexes;
    }

    public static double[] GetTicksForBarIndex(this ITimeSignatureManager manager, IReadOnlyList<int> barIndexes)
    {
        var ticks = new double[barIndexes.Count];

        int timeSignatureIndex = manager.TimeSignatures.Count - 1;
        for (int i = barIndexes.Count - 1; i >= 0; i--)
        {
            int barIndex = barIndexes[i];
            if (barIndex < 0)
            {
                ticks[i] = barIndex * manager.TimeSignatures[0].TicksPerBar();
                continue;
            }

            for (; timeSignatureIndex >= 0; timeSignatureIndex--)
            {
                if (manager.TimeSignatures[timeSignatureIndex].BarIndex <= barIndex)
                    break;
            }

            var last = manager.TimeSignatures[timeSignatureIndex];
            ticks[i] = last.Pos + (barIndex - last.BarIndex) * last.TicksPerBar();
        }

        return ticks;
    }

    public static double[] GetTicksForBeatIndex(this ITimeSignatureManager manager, IReadOnlyList<int> beatIndexes)
    {
        var ticks = new double[beatIndexes.Count];

        int timeSignatureIndex = manager.TimeSignatures.Count - 1;
        for (int i = beatIndexes.Count - 1; i >= 0; i--)
        {
            int beatIndex = beatIndexes[i];
            if (beatIndex < 0)
            {
                ticks[i] = beatIndex * manager.TimeSignatures[0].TicksPerBeat();
                continue;
            }

            for (; timeSignatureIndex >= 0; timeSignatureIndex--)
            {
                if (manager.TimeSignatures[timeSignatureIndex].BarIndex <= beatIndex)
                    break;
            }

            var last = manager.TimeSignatures[timeSignatureIndex];
            ticks[i] = last.Pos + (beatIndex - last.GlobalBeatIndex) * last.TicksPerBeat();
        }

        return ticks;
    }

    public static double[] GetBeatIndexes(this ITimeSignatureManager manager, IReadOnlyList<double> ticks)
    {
        double[] beatIndexes = new double[ticks.Count];

        int timeSignatureIndex = manager.TimeSignatures.Count - 1;
        for (int i = ticks.Count - 1; i >= 0; i--)
        {
            double pos = ticks[i];
            if (pos < 0)
            {
                beatIndexes[i] = pos / manager.TimeSignatures[0].TicksPerBeat();
                continue;
            }

            for (; timeSignatureIndex >= 0; timeSignatureIndex--)
            {
                if (manager.TimeSignatures[timeSignatureIndex].GlobalBeatIndex <= pos)
                    break;
            }

            var last = manager.TimeSignatures[timeSignatureIndex];
            beatIndexes[i] = last.GlobalBeatIndex + (pos - last.Pos) / last.TicksPerBeat();
        }

        return beatIndexes;
    }

    public static MeterStatus GetMeterStatus(this ITimeSignatureManager manager, double tick)
    {
        return manager.GetMeterStatus([tick])[0];
    }

    public static (int, int) GetBarAndBeatIndexForBeatIndex(this ITimeSignatureManager manager, int beatIndex)
    {
        return manager.GetBarAndBeatIndexes(new int[] { beatIndex })[0];
    }

    public static (int, double) GetBarAndBeatIndexForTick(this ITimeSignatureManager manager, double tick)
    {
        return manager.GetBarAndBeatIndexes(new double[] { tick })[0];
    }

    public static double GetTickForBarIndex(this ITimeSignatureManager manager, int barIndex)
    {
        return manager.GetTicksForBarIndex([barIndex])[0];
    }

    public static double GetTickForBeatIndexForBeatIndex(this ITimeSignatureManager manager, int beatIndex)
    {
        return manager.GetTicksForBeatIndex([beatIndex])[0];
    }

    public static double GetBeatIndexForTick(this ITimeSignatureManager manager, double tick)
    {
        return manager.GetBeatIndexes([tick])[0];
    }
}