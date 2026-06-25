using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// InstrumentSnapshot 的物化器：插件在 SynthesizeNext 同步前缀经 context.GetSnapshot 拉取，这里在数据线程 eager 物化。
// 与 voice 的 VoiceSnapshotFactory 同构但精简——note 取【满末】（Pos+Dur，不去重叠）、无 Lyric/Phonemes、
// 无 Pitch/PitchDeviation 双音高通道、无 vibrato 偏移。automation 取原始曲线开窗冻结。
//
// 全秒轴：插件面 [startTime, endTime] 与各求值器查询点均为全局秒；tick 仅在本物化器与冻结求值器内部出现。
internal static class InstrumentSnapshotFactory
{
    public static InstrumentSnapshot Capture(MidiPart part, IReadOnlyList<IInstrumentNote> sourceNotes, double startTime, double endTime)
    {
        double partPos = part.Pos.Value;

        // tempo 快照（不可变）：秒↔tick 换算 + 开窗到 part 相对 tick。
        var timing = part.TempoManager.CreateSnapshot();
        Func<IReadOnlyList<double>, double[]> timesToTicks = timing.ToTicks;
        double relStart = timing.ToTick(startTime) - partPos;
        double relEnd = timing.ToTick(endTime) - partPos;

        // note 值树（满末，经代理读值触底到值类型；列表顺序即递入声明顺序）。
        var notes = new List<InstrumentNoteSnapshot>(sourceNotes.Count);
        foreach (var note in sourceNotes)
        {
            if (note is not InstrumentSynthesisContext.InstrumentNoteProxy proxy)
                throw new ArgumentException("Segment notes must come from this part's instrument synthesis context.");

            notes.Add(new InstrumentNoteSnapshot
            {
                StartTime = note.StartTime.Value,           // 全局秒（proxy 已换算）
                EndTime = note.EndTime.Value,               // 满末（不去重叠）
                Pitch = note.Pitch.Value,
                Properties = proxy.Source.Properties.GetInfo(),
            });
        }

        // automation：已声明轨按区间开窗物化（原始曲线、无 vibrato；无数据对象的轨冻结为默认值常量）。
        var automations = new Map<string, SynthesisAutomationSnapshot>();
        foreach (var kvp in part.SoundSource.AutomationConfigs)
        {
            string key = kvp.Key.Id;
            IAutomationEvaluator baseEvaluator = part.Automations.TryGetValue(key, out var automation)
                ? AutomationSnapshot.Capture(automation, relStart, relEnd)
                : new ConstantEvaluator(kvp.Value.DefaultValue);
            automations.Add(key, new SynthesisAutomationSnapshot { Evaluator = new SecondToTickEvaluator(baseEvaluator, partPos, timesToTicks) });
        }

        return new InstrumentSnapshot
        {
            Notes = notes,
            AutomationMap = automations,
            PartProperties = part.Properties.GetInfo(),
        };
    }

    // 查询轴 = 全局秒：换算到全局 tick、减 part 偏移到相对 tick，再喂基础（相对 tick 轴）求值器。无 vibrato 叠加。
    sealed class SecondToTickEvaluator(
        IAutomationEvaluator baseEvaluator,
        double partPos,
        Func<IReadOnlyList<double>, double[]> timesToTicks) : IAutomationEvaluator
    {
        public double[] Evaluate(IReadOnlyList<double> times)
        {
            double[] globalTicks = timesToTicks(times);
            double[] ticks = new double[times.Count];
            for (int i = 0; i < times.Count; i++)
            {
                ticks[i] = globalTicks[i] - partPos;
            }
            return baseEvaluator.Evaluate(ticks);
        }
    }

    sealed class ConstantEvaluator(double value) : IAutomationEvaluator
    {
        public double[] Evaluate(IReadOnlyList<double> times)
        {
            double[] values = new double[times.Count];
            values.Fill(value);
            return values;
        }
    }
}
