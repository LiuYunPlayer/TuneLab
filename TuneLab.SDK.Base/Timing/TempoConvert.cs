namespace TuneLab.SDK.Base.Timing;

// tick↔秒换算的唯一一份纯函数实现。宿主活 tempo 表与冻结快照都经由此处换算，杜绝两套实现漂移。
// 前提约定：tempos 升序且首条 Tick = 0、Seconds = 0；负位置一律按首条速度线性外推。
// 批量版要求查询点升序：实现自尾向头单次扫描（O(n+m)），乱序输入结果未定义。
public static class TempoConvert
{
    public static double[] ToSeconds<TMark>(IReadOnlyList<TMark> tempos, IReadOnlyList<double> ticks) where TMark : ITempoMark
    {
        double[] seconds = new double[ticks.Count];

        int tempoIndex = tempos.Count - 1;
        for (int i = ticks.Count - 1; i >= 0; i--)
        {
            double tick = ticks[i];
            if (tick < 0)
            {
                seconds[i] = tick / tempos[0].TicksPerSecond;
                continue;
            }

            for (; tempoIndex >= 0; tempoIndex--)
            {
                if (tempos[tempoIndex].Tick <= tick)
                    break;
            }

            var mark = tempos[tempoIndex];
            seconds[i] = mark.Seconds + (tick - mark.Tick) / mark.TicksPerSecond;
        }

        return seconds;
    }

    public static double[] ToTicks<TMark>(IReadOnlyList<TMark> tempos, IReadOnlyList<double> seconds) where TMark : ITempoMark
    {
        double[] ticks = new double[seconds.Count];

        int tempoIndex = tempos.Count - 1;
        for (int i = seconds.Count - 1; i >= 0; i--)
        {
            double second = seconds[i];
            if (second < 0)
            {
                ticks[i] = second * tempos[0].TicksPerSecond;
                continue;
            }

            for (; tempoIndex >= 0; tempoIndex--)
            {
                if (tempos[tempoIndex].Seconds <= second)
                    break;
            }

            var mark = tempos[tempoIndex];
            ticks[i] = mark.Tick + (second - mark.Seconds) * mark.TicksPerSecond;
        }

        return ticks;
    }

    public static double ToSeconds<TMark>(IReadOnlyList<TMark> tempos, double tick) where TMark : ITempoMark
    {
        return ToSeconds(tempos, [tick])[0];
    }

    public static double ToTick<TMark>(IReadOnlyList<TMark> tempos, double seconds) where TMark : ITempoMark
    {
        return ToTicks(tempos, [seconds])[0];
    }
}
