namespace TuneLab.Foundation.Science;

public interface ITempoCalculatorHelper
{
    IReadOnlyList<ITempoHelper> Tempos { get; }
}

public static class ITempoCalculatorHelperExtension
{
    public static double[] GetTimes(this ITempoCalculatorHelper calculator, IReadOnlyList<double> ticks)
    {
        double[] times = new double[ticks.Count];

        int tempoIndex = calculator.Tempos.Count - 1;
        for (int i = ticks.Count - 1; i >= 0; i--)
        {
            double pos = ticks[i];
            if (pos < 0)
            {
                times[i] = pos / calculator.Tempos[0].Coe;
                continue;
            }

            for (; tempoIndex >= 0; tempoIndex--)
            {
                if (calculator.Tempos[tempoIndex].Pos <= pos)
                    break;
            }

            var last = calculator.Tempos[tempoIndex];
            times[i] = last.Time + (pos - last.Pos) / last.Coe;
        }

        return times;
    }

    public static double[] GetTicks(this ITempoCalculatorHelper calculator, IReadOnlyList<double> times)
    {
        double[] ticks = new double[times.Count];

        int tempoIndex = calculator.Tempos.Count - 1;
        for (int i = times.Count - 1; i >= 0; i--)
        {
            double time = times[i];
            if (time < 0)
            {
                ticks[i] = time * calculator.Tempos[0].Coe;
                continue;
            }

            for (; tempoIndex >= 0; tempoIndex--)
            {
                if (calculator.Tempos[tempoIndex].Time <= time)
                    break;
            }

            var last = calculator.Tempos[tempoIndex];
            ticks[i] = last.Pos + (time - last.Time) * last.Coe;
        }

        return ticks;
    }

    public static double GetTime(this ITempoCalculatorHelper calculator, double tick)
    {
        return calculator.GetTimes([tick])[0];
    }

    public static double GetTick(this ITempoCalculatorHelper calculator, double time)
    {
        return calculator.GetTicks([time])[0];
    }
}
