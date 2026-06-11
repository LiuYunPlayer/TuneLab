namespace TuneLab.SDK.Base.Timing;

// 推导后的换算表条目。internal：只能经 TempoConvert.Resolve 产出，
// Seconds/TicksPerSecond 与 (Tick, Bpm) 的一致性由构造路径保证，外部无法注入不一致的派生值。
internal readonly record struct ResolvedTempoMark(double Tick, double Seconds, double TicksPerSecond);

// tempo 推导与 tick↔秒换算的唯一一份纯函数实现（live 缓存与冻结快照共用，杜绝实现漂移）。
// 前提约定：tempos 升序且首条 Tick = 0；负位置一律按首条速度线性外推。
// 批量版要求查询点升序：实现自尾向头单次扫描（O(n+m)），乱序输入结果未定义。
internal static class TempoConvert
{
    // ticksPerQuarter = 每四分音符 tick 数（宿主侧常量，经参数传入、不冻结进 SDK）。
    public static ResolvedTempoMark[] Resolve(IReadOnlyList<TempoMark> tempos, double ticksPerQuarter)
    {
        var resolved = new ResolvedTempoMark[tempos.Count];
        double seconds = 0;
        for (int i = 0; i < tempos.Count; i++)
        {
            var tempo = tempos[i];
            if (i > 0)
                seconds += (tempo.Tick - tempos[i - 1].Tick) / resolved[i - 1].TicksPerSecond;

            resolved[i] = new(tempo.Tick, seconds, tempo.Bpm / 60 * ticksPerQuarter);
        }
        return resolved;
    }

    public static double[] ToSeconds(ResolvedTempoMark[] tempos, IReadOnlyList<double> ticks)
    {
        double[] seconds = new double[ticks.Count];

        int tempoIndex = tempos.Length - 1;
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

    public static double[] ToTicks(ResolvedTempoMark[] tempos, IReadOnlyList<double> seconds)
    {
        double[] ticks = new double[seconds.Count];

        int tempoIndex = tempos.Length - 1;
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

    public static double ToSeconds(ResolvedTempoMark[] tempos, double tick)
    {
        return ToSeconds(tempos, [tick])[0];
    }

    public static double ToTick(ResolvedTempoMark[] tempos, double seconds)
    {
        return ToTicks(tempos, [seconds])[0];
    }
}
