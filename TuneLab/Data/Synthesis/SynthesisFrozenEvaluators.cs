using System;
using System.Collections.Generic;
using TuneLab.Data;
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
    public void Evaluate(IReadOnlyList<double> times, Span<double> results)
    {
        double[] globalTicks = timesToTicks(times);
        double[] ticks = new double[times.Count];
        for (int i = 0; i < times.Count; i++)
        {
            ticks[i] = globalTicks[i] - partPos;
        }

        baseEvaluator.Evaluate(ticks, results);
        if (vibratos.Count == 0)
            return;

        var deviation = VibratoMath.GetDeviation(vibratos, ticks, envelopeSampler, partPos, tickToTime);
        for (int i = 0; i < results.Length; i++)
        {
            if (skipNaN && double.IsNaN(results[i]))
                continue;

            results[i] += deviation[i];
        }
    }
}

internal sealed class ConstantAutomationEvaluator(double value) : IAutomationEvaluator
{
    public void Evaluate(IReadOnlyList<double> times, Span<double> results)
    {
        results.Fill(value);
    }
}

// 标度量化装饰器：把内层求值器的最终输出投影到 config 标度的可表示集（离散标度落格；NaN 透传）。
// 包在链最外层（vibrato/envelope 叠加之后）——保证插件读到的最终值处处落格，即"离散 scale ⇒ 量化信号"的数据层强制。
// 只量化、不钳值域：越界值照原样喂给插件，由引擎按自身需求校验——理由见 ScaleExtensions.Project。越界来源既有
// 存量数据（量程变更后的旧工程 / preset / 插件回喂），也有本链自身的加性合成：锚点存相对 DefaultValue 的偏移
// （用户改默认值即整批平移，见 AutomationSnapshot）、内层 vibrato deviation 也是加性叠加。
// 连续标度下投影为恒等，仅余 ULP 级浮点扰动。
internal sealed class ScaleQuantizingEvaluator(IAutomationEvaluator inner, INormalizedScale scale) : IAutomationEvaluator
{
    public void Evaluate(IReadOnlyList<double> times, Span<double> results)
    {
        inner.Evaluate(times, results);
        for (int i = 0; i < results.Length; i++)
            results[i] = scale.Project(results[i]);
    }
}
