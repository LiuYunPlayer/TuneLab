using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// 合成快照的物化器：插件在 SynthesizeNext 的同步前缀经 context.GetSnapshot 主动拉取，
// 这里在数据线程按递入的 notes/开窗区间 eager 物化，之后插件才 offload——worker 永不碰活对象。
// 接现成基础件：AnchorWindow 无变形开窗（Automation/PiecewiseAutomationSnapshot）、
// TempoManager.CreateSnapshot（不可变换算表）。
// 替换，而非同步：数据变了走活视图通知 → 插件标脏 → 下次合成拉一份全新快照。
//
// 全秒轴：插件面 [startTime, endTime] 与各求值器查询点均为全局秒；tick 仅在本物化器与冻结
// 求值器内部出现（经 tempo 快照换算）。snapshot 不携带 Timing——插件不碰 tick。
internal static class VoiceSynthesisSnapshotFactory
{
    // 须在数据线程调用。notes 须为本 part 当前 context 的 note 代理；
    // 快照 Notes 与递入 notes 索引对齐（产物归属契约）。[startTime, endTime] 为全局秒开窗区间。
    public static VoiceSynthesisSnapshot Capture(MidiPart part, IEnumerable<IVoiceSynthesisNote> sourceNotes, double startTime, double endTime)
    {
        double partPos = part.Pos.Value;

        // —— tempo 快照（不可变，零拷贝共享）：秒↔tick 换算 + 开窗到 part 相对 tick ——
        var timing = part.TempoManager.CreateSnapshot();
        Func<double, double> tickToTime = timing.ToSecond;
        Func<IReadOnlyList<double>, double[]> timesToTicks = timing.ToTicks;
        double relStart = timing.ToTick(startTime) - partPos;
        double relEnd = timing.ToTick(endTime) - partPos;

        // —— note 值树（经代理接口读值，全部触底到值类型；列表顺序即递入声明顺序）——
        var notes = new List<VoiceSynthesisNoteSnapshot>();
        foreach (var note in sourceNotes)
        {
            if (note is not VoiceSynthesisContext.VoiceNoteProxy proxy)
                throw new ArgumentException("Segment notes must come from this part's synthesis context.");

            notes.Add(new VoiceSynthesisNoteSnapshot
            {
                StartTime = note.StartTime.Value,               // 全局秒（note proxy 已换算）
                EndTime = note.EndTime.Value,                   // 有效末（去重叠，单声部音频口径；宿主独占音素布局）
                Pitch = note.Pitch.Value,
                Lyric = note.Lyric.Value,
                IsContinuation = note.IsContinuation,           // 生效延续（相接链）；宿主独占判据，作稳定标志暴露
                Phonemes = CapturePhonemes(proxy.Source),       // 钉死音素：几何描述符 + per-phoneme 属性值快照
                Properties = proxy.Source.Properties.GetInfo(), // 值拷 PropertyObject
            });
        }

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
            envelopeSampler = envelopeSnapshot.Evaluate;
        }

        // —— 音高双通道：Pitch = 纯用户绘制曲线开窗快照（NaN=自由）；
        //    PitchDeviation = vibrato 偏移冻结合成（基线 0，与 live GetVibratoDeviation 同一套共享算法）。 ——
        var pitch = new SynthesisAutomationSnapshot { Evaluator = new FrozenFinalEvaluator(
            PiecewiseAutomationSnapshot.Capture(part.Pitch, relStart, relEnd),
            [], envelopeSampler, partPos, tickToTime, timesToTicks, skipNaN: true) };
        var pitchDeviation = new SynthesisAutomationSnapshot { Evaluator = new FrozenFinalEvaluator(
            new ConstantEvaluator(0),
            SelectVibratos(vibratoCaptures, string.Empty),
            envelopeSampler, partPos, tickToTime, timesToTicks, skipNaN: false) };

        // —— automation：全部已声明轨按区间开窗物化（无数据对象的轨冻结为默认值常量）——
        var automations = new Map<string, SynthesisAutomationSnapshot>();
        foreach (var kvp in part.SoundSource.AutomationConfigs)
        {
            string key = kvp.Key.Id;
            IAutomationEvaluator baseEvaluator = part.Automations.TryGetValue(key, out var automation)
                ? AutomationSnapshot.Capture(automation, relStart, relEnd)
                : new ConstantEvaluator(kvp.Value.DefaultValue);
            automations.Add(key, new SynthesisAutomationSnapshot { Evaluator = new FrozenFinalEvaluator(
                baseEvaluator,
                SelectVibratos(vibratoCaptures, key),
                envelopeSampler, partPos, tickToTime, timesToTicks, skipNaN: false) });
        }

        return new VoiceSynthesisSnapshot
        {
            Notes = notes,
            Pitch = pitch,
            PitchDeviation = pitchDeviation,
            Automations = automations,
            PartProperties = part.Properties.GetInfo(),
        };
    }

    // 钉死音素物化：几何字段平铺 + per-phoneme 属性值快照。属性 lazy——未编辑过的音素走 HasProperties 闸门直接取
    // PropertyObject.Empty、不触发物化。非钉死（引擎 G2P）note 的 Phonemes 为空。
    static IReadOnlyList<VoiceSynthesisPhonemeSnapshot> CapturePhonemes(INote note)
    {
        int n = note.Phonemes.Count;
        var result = new VoiceSynthesisPhonemeSnapshot[n];
        for (int i = 0; i < n; i++)
        {
            var p = note.Phonemes[i];
            result[i] = new VoiceSynthesisPhonemeSnapshot(
                p.Symbol.Value, p.Duration.Value, p.StretchWeight.Value, p.IsLead.Value,
                p.HasProperties ? p.Properties.GetInfo() : PropertyObject.Empty);
        }
        return result;
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

    // 最终求值的冻结合成：开窗基础快照 + vibrato 偏移（共享纯函数）。查询轴 = 全局秒，
    // 此处经 tempo 快照换算到全局 tick、再减 part 偏移到相对 tick 求值。
    // skipNaN：pitch 段间空值不叠加偏移（与 live 行为一致）。
    sealed class FrozenFinalEvaluator(
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
