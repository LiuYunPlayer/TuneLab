using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// effect 合成快照物化器（镜像 VoiceSynthesisSnapshotFactory 的判例，砍掉 note/vibrato——effect 自动化
// 无 vibrato 投影）：数据线程 eager 物化参数值 + 自动化冻结求值器；音频不在此列（Input.Read 自物化）。
// 接现成基础件：AutomationSnapshot / PiecewiseAutomationSnapshot 无变形开窗、TempoManager.CreateSnapshot。
internal static class EffectSynthesisSnapshotFactory
{
    // 须在数据线程调用；[startTime, endTime] 为全局秒开窗区间。
    public static EffectSynthesisSnapshot Capture(MidiPart part, IEffect effect, double startTime, double endTime)
    {
        double partPos = part.Pos.Value;
        var timing = part.TempoManager.CreateSnapshot();
        Func<IReadOnlyList<double>, double[]> timesToTicks = timing.ToTicks;
        double relStart = timing.ToTick(startTime) - partPos;
        double relEnd = timing.ToTick(endTime) - partPos;

        // 全部已声明轨按区间开窗物化（连续无数据 → 默认值常量；分段无数据 → NaN 常量）。
        var automations = new Map<string, SynthesisAutomationSnapshot>();
        foreach (var kvp in effect.AutomationConfigs)
        {
            string key = kvp.Key.Id;
            IAutomationEvaluator baseEvaluator;
            if (kvp.Value.IsPiecewise)
            {
                baseEvaluator = effect.PiecewiseAutomations.TryGetValue(key, out var piecewise)
                    ? PiecewiseAutomationSnapshot.Capture(piecewise, relStart, relEnd)
                    : new ConstantEvaluator(double.NaN);
            }
            else
            {
                baseEvaluator = effect.Automations.TryGetValue(key, out var automation)
                    ? AutomationSnapshot.Capture(automation, relStart, relEnd)
                    : new ConstantEvaluator(kvp.Value.DefaultValue);
            }
            automations.Add(key, new SynthesisAutomationSnapshot
            {
                Evaluator = new FrozenSecondsEvaluator(baseEvaluator, partPos, timesToTicks),
            });
        }

        return new EffectSynthesisSnapshot
        {
            Properties = effect.Properties.GetInfo(),
            Automations = automations,
        };
    }

    // 查询轴全局秒 → 冻结 tempo 换算全局 tick → 减 part 偏移 → 基础快照求值（不引用任何活对象）。
    sealed class FrozenSecondsEvaluator(
        IAutomationEvaluator baseEvaluator,
        double partPos,
        Func<IReadOnlyList<double>, double[]> timesToTicks) : IAutomationEvaluator
    {
        public double[] Evaluate(IReadOnlyList<double> times)
        {
            var ticks = timesToTicks(times);
            for (int i = 0; i < ticks.Length; i++)
                ticks[i] -= partPos;
            return baseEvaluator.Evaluate(ticks);
        }
    }

    sealed class ConstantEvaluator(double value) : IAutomationEvaluator
    {
        public double[] Evaluate(IReadOnlyList<double> times)
        {
            var values = new double[times.Count];
            values.Fill(value);
            return values;
        }
    }
}
