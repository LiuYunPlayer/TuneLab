using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// effect 合成快照物化器（镜像 VoiceSynthesisSnapshotFactory 的判例，砍掉 note——effect 无 note 面）：
// 数据线程 eager 物化参数值 + 自动化冻结求值器；音频不在此列（Input.Read 自物化）。
// 连续轨的冻结求值 = 曲线开窗快照 + vibrato 偏移（振幅按本 effect 槽位的目标轨现场解析，
// AffectedEffectAutomations 键 = 槽位 + 轨 id；与 voice 侧同一套共享纯函数与包络语义）。
// 接现成基础件：AutomationSnapshot / PiecewiseAutomationSnapshot 无变形开窗、TempoManager.CreateSnapshot。
internal static class EffectSynthesisSnapshotFactory
{
    // 须在数据线程调用；[startTime, endTime] 为全局秒开窗区间。
    public static EffectSynthesisSnapshot Capture(MidiPart part, IEffect effect, double startTime, double endTime)
    {
        double partPos = part.Pos.Value;
        var timing = part.TempoManager.CreateSnapshot();
        Func<double, double> tickToTime = timing.ToSecond;
        Func<IReadOnlyList<double>, double[]> timesToTicks = timing.ToTicks;
        double relStart = timing.ToTick(startTime) - partPos;
        double relEnd = timing.ToTick(endTime) - partPos;

        // —— vibrato 参数值拷（窗口相交者，含本 effect 的影响表切片，按实例 id 匹配）+ 包络轨开窗快照 ——
        var vibratoCaptures = new List<VibratoCapture>();
        foreach (var vibrato in part.Vibratos)
        {
            if (vibrato.Pos.Value + vibrato.Dur.Value < relStart)
                continue;

            if (vibrato.Pos.Value > relEnd)
                break;

            Dictionary<string, double>? affected = null;
            foreach (var kvp in vibrato.AffectedEffectAutomations)
            {
                if (kvp.Key.EffectId == effect.Id)
                    (affected ??= new()).Add(kvp.Key.Id, kvp.Value);
            }
            if (affected == null)
                continue;

            vibratoCaptures.Add(new VibratoCapture(
                vibrato.Pos.Value, vibrato.Dur.Value, vibrato.Frequency.Value,
                vibrato.Phase.Value, vibrato.Attack.Value, vibrato.Release.Value, affected));
        }

        Func<double[], double[]>? envelopeSampler = null;
        if (vibratoCaptures.Count > 0 && part.Automations.TryGetValue(ConstantDefine.VibratoEnvelopeID, out var envelope))
        {
            var envelopeSnapshot = AutomationSnapshot.Capture(envelope, relStart, relEnd);
            envelopeSampler = ticks => envelopeSnapshot.Evaluate(ticks);
        }

        // 全部已声明轨按区间开窗物化（连续无数据 → 默认值常量；分段无数据 → NaN 常量）。
        var automations = new Map<string, SynthesisAutomationSnapshot>();
        foreach (var kvp in effect.AutomationConfigs)
        {
            string key = kvp.Key.Id;
            IAutomationEvaluator baseEvaluator;
            IReadOnlyList<VibratoMath.VibratoData> vibratos;
            if (kvp.Value.IsPiecewise)
            {
                baseEvaluator = effect.PiecewiseAutomations.TryGetValue(key, out var piecewise)
                    ? PiecewiseAutomationSnapshot.Capture(piecewise, relStart, relEnd)
                    : new ConstantAutomationEvaluator(double.NaN);
                vibratos = Array.Empty<VibratoMath.VibratoData>();   // 分段轨无 automation-vibrato 概念
            }
            else
            {
                baseEvaluator = effect.Automations.TryGetValue(key, out var automation)
                    ? AutomationSnapshot.Capture(automation, relStart, relEnd)
                    : new ConstantAutomationEvaluator(kvp.Value.DefaultValue);
                vibratos = SelectVibratos(vibratoCaptures, key);
            }
            // 最外层套标度量化（vibrato/envelope 之后）：离散 scale ⇒ 插件读到的最终值处处落格；线性 scale 仅钳位。
            automations.Add(key, new SynthesisAutomationSnapshot
            {
                Evaluator = new ScaleQuantizingEvaluator(new FrozenFinalAutomationEvaluator(
                    baseEvaluator, vibratos, envelopeSampler, partPos, tickToTime, timesToTicks, skipNaN: false), kvp.Value.Scale),
            });
        }

        return new EffectSynthesisSnapshot
        {
            Properties = effect.Properties.GetInfo(),
            Automations = automations,
        };
    }

    // 按目标轨解析 vibrato 振幅（查本 effect 的影响表切片），过滤无影响者。
    static IReadOnlyList<VibratoMath.VibratoData> SelectVibratos(IReadOnlyList<VibratoCapture> captures, string automationID)
    {
        List<VibratoMath.VibratoData>? result = null;
        foreach (var capture in captures)
        {
            if (!capture.AffectedAutomations.TryGetValue(automationID, out double amplitude) || amplitude == 0)
                continue;

            (result ??= new()).Add(new VibratoMath.VibratoData(
                capture.Pos, capture.Dur, capture.Frequency, amplitude,
                capture.Phase, capture.Attack, capture.Release));
        }
        return result ?? (IReadOnlyList<VibratoMath.VibratoData>)Array.Empty<VibratoMath.VibratoData>();
    }

    sealed record VibratoCapture(
        double Pos, double Dur, double Frequency,
        double Phase, double Attack, double Release,
        IReadOnlyDictionary<string, double> AffectedAutomations);
}
