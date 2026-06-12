using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation.Utils;
using TuneLab.Primitives.DataStructures;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.Timing;
using TuneLab.SDK.Voice;

namespace TuneLab.Data.Synthesis;

// 合成快照的物化器：插件在 SynthesizeNext 的同步前缀经 context.GetSnapshot 主动拉取，
// 这里在数据线程按递入的 notes/开窗区间 eager 物化，之后插件才 offload——worker 永不碰活对象。
// 接现成基础件：AnchorWindow 无变形开窗（Automation/PiecewiseAutomationSnapshot）、
// TempoManager.CreateSnapshot（不可变换算表）。
// 替换，而非同步：数据变了走活视图通知 → 插件标脏 → 下次合成拉一份全新快照。
internal static class SynthesisSnapshotFactory
{
    // 须在数据线程调用。notes 须为本 part 当前 context 的 note 代理；
    // 快照 Notes 与递入 notes 索引对齐（产物归属契约）。[startTick, endTick] 为全局 tick 开窗区间。
    public static SynthesisSnapshot Capture(MidiPart part, IReadOnlyList<ISynthesisNote> sourceNotes, double startTick, double endTick)
    {
        double partPos = part.Pos.Value;
        double relStart = startTick - partPos;
        double relEnd = endTick - partPos;

        // —— note 值树（经代理接口读值，全部触底到值类型；列表顺序即递入声明顺序）——
        var notes = new List<SynthesisNoteSnapshot>(sourceNotes.Count);
        foreach (var note in sourceNotes)
        {
            if (note is not SynthesisContext.SynthesisNoteProxy proxy)
                throw new ArgumentException("Segment notes must come from this part's synthesis context.");

            notes.Add(new SynthesisNoteSnapshot
            {
                StartPosition = note.StartPosition.Value,
                EndPosition = note.EndPosition.Value,
                Pitch = note.Pitch.Value,
                Lyric = note.Lyric.Value,
                Phonemes = note.Phonemes.Value,                 // 派生 getter 每次新建列表，无活引用
                Properties = proxy.Source.Properties.GetInfo(), // 值拷 PropertyObject
            });
        }

        // —— tempo 快照（不可变，零拷贝共享）——
        var timing = part.TempoManager.CreateSnapshot();
        Func<double, double> tickToTime = timing.ToSecond;

        // —— vibrato 参数值拷（窗口相交者）+ 包络轨开窗快照 ——
        var vibratoCaptures = new List<VibratoCapture>();
        foreach (var vibrato in part.Vibratos)
        {
            if (vibrato.Pos.Value + vibrato.Dur.Value < relStart)
                continue;

            if (vibrato.Pos.Value > relEnd)
                break;

            var affected = new Dictionary<string, double>();
            foreach (var kvp in vibrato.AffectedAutomations)
            {
                affected.Add(kvp.Key, kvp.Value);
            }
            vibratoCaptures.Add(new VibratoCapture(
                vibrato.Pos.Value, vibrato.Dur.Value, vibrato.Frequency.Value, vibrato.Amplitude.Value,
                vibrato.Phase.Value, vibrato.Attack.Value, vibrato.Release.Value, affected));
        }

        Func<double[], double[]>? envelopeSampler = null;
        if (part.Automations.TryGetValue(ConstantDefine.VibratoEnvelopeID, out var envelope))
        {
            var envelopeSnapshot = AutomationSnapshot.Capture(envelope, relStart, relEnd);
            envelopeSampler = envelopeSnapshot.GetValue;
        }

        // —— 音高双通道：Pitch = 纯用户绘制曲线开窗快照（NaN=自由）；
        //    PitchDeviation = vibrato 偏移冻结合成（基线 0，与 live GetVibratoDeviation 同一套共享算法）。 ——
        var pitch = new FrozenFinalGetter(
            PiecewiseAutomationSnapshot.Capture(part.Pitch, relStart, relEnd),
            [], envelopeSampler, partPos, tickToTime, skipNaN: true);
        var pitchDeviation = new FrozenFinalGetter(
            new ConstantValueGetter(0),
            SelectVibratos(vibratoCaptures, string.Empty),
            envelopeSampler, partPos, tickToTime, skipNaN: false);

        // —— automation：全部已声明轨按区间开窗物化（无数据对象的轨冻结为默认值常量）——
        var automations = new Map<string, IAutomationValueGetter>();
        foreach (var kvp in part.Voice.AutomationConfigs)
        {
            string key = kvp.Key;
            IAutomationValueGetter baseGetter = part.Automations.TryGetValue(key, out var automation)
                ? AutomationSnapshot.Capture(automation, relStart, relEnd)
                : new ConstantValueGetter(kvp.Value.DefaultValue);
            automations.Add(key, new FrozenFinalGetter(
                baseGetter,
                SelectVibratos(vibratoCaptures, key),
                envelopeSampler, partPos, tickToTime, skipNaN: false));
        }

        return new SynthesisSnapshot
        {
            Notes = notes,
            Timing = timing,
            Pitch = pitch,
            PitchDeviation = pitchDeviation,
            Automations = automations,
            PartProperties = part.Properties.GetInfo(),
        };
    }

    // 按目标轨解析 vibrato 振幅（音高轨用自身振幅，其余查影响表），过滤无影响者。
    static IReadOnlyList<VibratoMath.VibratoData> SelectVibratos(IReadOnlyList<VibratoCapture> captures, string automationID)
    {
        var result = new List<VibratoMath.VibratoData>();
        foreach (var capture in captures)
        {
            double amplitude = string.IsNullOrEmpty(automationID)
                ? capture.Amplitude
                : capture.AffectedAutomations.GetValueOrDefault(automationID, 0);
            if (amplitude == 0)
                continue;

            result.Add(new VibratoMath.VibratoData(
                capture.Pos, capture.Dur, capture.Frequency, amplitude,
                capture.Phase, capture.Attack, capture.Release));
        }
        return result;
    }

    sealed record VibratoCapture(
        double Pos, double Dur, double Frequency, double Amplitude,
        double Phase, double Attack, double Release,
        IReadOnlyDictionary<string, double> AffectedAutomations);

    // 最终取值的冻结合成：开窗基础快照 + vibrato 偏移（共享纯函数），查询轴为全局 tick、
    // 此处换算到 part 相对后取值。skipNaN：pitch 段间空值不叠加偏移（与 live 行为一致）。
    sealed class FrozenFinalGetter(
        IAutomationValueGetter baseGetter,
        IReadOnlyList<VibratoMath.VibratoData> vibratos,
        Func<double[], double[]>? envelopeSampler,
        double partPos,
        Func<double, double> tickToTime,
        bool skipNaN) : IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            double[] ticks = new double[times.Count];
            for (int i = 0; i < times.Count; i++)
            {
                ticks[i] = times[i] - partPos;
            }

            var values = baseGetter.GetValue(ticks);
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

    sealed class ConstantValueGetter(double value) : IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            double[] values = new double[times.Count];
            values.Fill(value);
            return values;
        }
    }
}
