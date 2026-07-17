using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// 快照工厂（voice / effect）共用的冻结求值组合件：最终求值 = 开窗基础快照 + vibrato 偏移（共享纯函数）。
// 查询轴 = 全局秒，此处经 tempo 快照换算到全局 tick、再减 part 偏移到相对 tick 求值。
// skipNaN：pitch 段间空值不叠加偏移（与 live 行为一致）。
internal sealed class FrozenFinalAutomationEvaluator(
    IAutomationEvaluator baseEvaluator,
    IReadOnlyList<VibratoMath.VibratoData> vibratos,
    Func<double[], double[]>? envelopeSampler,
    double partPos,
    Func<double, double> tickToTime,
    Func<IReadOnlyList<double>, double[]> timesToTicks,
    bool skipNaN) : IAutomationEvaluator
{
    public double[] Evaluate(IReadOnlyList<double> times)
    {
        double[] globalTicks = timesToTicks(times);
        double[] ticks = new double[times.Count];
        for (int i = 0; i < times.Count; i++)
        {
            ticks[i] = globalTicks[i] - partPos;
        }

        var values = baseEvaluator.Evaluate(ticks);
        if (vibratos.Count == 0)
            return values;

        var deviation = VibratoMath.GetDeviation(vibratos, ticks, envelopeSampler, partPos, tickToTime);
        for (int i = 0; i < values.Length; i++)
        {
            if (skipNaN && double.IsNaN(values[i]))
                continue;

            values[i] += deviation[i];
        }
        return values;
    }
}

internal sealed class ConstantAutomationEvaluator(double value) : IAutomationEvaluator
{
    public double[] Evaluate(IReadOnlyList<double> times)
    {
        double[] values = new double[times.Count];
        values.Fill(value);
        return values;
    }
}
