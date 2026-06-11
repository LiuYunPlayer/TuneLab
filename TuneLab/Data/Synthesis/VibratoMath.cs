using System;
using System.Collections.Generic;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data.Synthesis;

// vibrato 偏移量的共享纯采样函数：live 侧（MidiPart 实时取值）与冻结快照侧（合成快照 getter）
// 共用同一份"参数 → 偏移"算法，杜绝两套实现漂移。
// 输入触底到值（VibratoData 纯值 record + 函数式依赖注入），不引用任何活数据对象，
// 可在 worker 线程对快照安全调用。
internal static class VibratoMath
{
    // 单个 vibrato 的纯值参数（Pos/Dur 为 part 相对 tick；Attack/Release 为秒）。
    // Amplitude 已按目标自动化轨解析（音高轨用 vibrato 自身振幅，其余轨查其影响表），由调用方选好。
    internal sealed record VibratoData(
        double Pos,
        double Dur,
        double Frequency,
        double Amplitude,
        double Phase,
        double Attack,
        double Release);

    // 计算 vibratos 在 ticks（part 相对、升序）处的偏移量之和。
    // envelopeSampler：包络轨采样（part 相对 ticks → 包络值），无包络轨传 null（视为常量 1 × 振幅）。
    // tickToTime：全局 tick → 秒（live 侧闭包 TempoManager、快照侧闭包 TempoSnapshot）。
    public static double[] GetDeviation(
        IReadOnlyList<VibratoData> vibratos,
        IReadOnlyList<double> ticks,
        Func<double[], double[]>? envelopeSampler,
        double partPos,
        Func<double, double> tickToTime)
    {
        double[] values = new double[ticks.Count];
        values.Fill(0);
        if (ticks.Count == 0)
            return values;

        double start = ticks[0];
        double end = ticks[ticks.Count - 1];
        int tickIndex = 0;
        foreach (var vibrato in vibratos)
        {
            double vibratoStart = vibrato.Pos;
            double vibratoEnd = vibrato.Pos + vibrato.Dur;
            if (vibratoEnd < start)
                continue;

            if (vibratoStart > end)
                break;

            double amplitude = vibrato.Amplitude;
            if (amplitude == 0)
                continue;

            while (tickIndex > 0 && ticks[tickIndex] > vibratoStart)
            {
                tickIndex--;
            }

            while (tickIndex < ticks.Count && ticks[tickIndex] < vibratoStart)
            {
                tickIndex++;
            }

            int offset = tickIndex;
            while (tickIndex < ticks.Count && ticks[tickIndex] <= vibratoEnd)
            {
                tickIndex++;
            }

            double[] ts = new double[tickIndex - offset];
            for (int i = 0; i < ts.Length; i++)
            {
                ts[i] = ticks[i + offset];
            }
            double[] amplitudes;
            if (envelopeSampler != null)
            {
                amplitudes = envelopeSampler(ts);
                for (int i = 0; i < amplitudes.Length; i++)
                {
                    amplitudes[i] = Math.Max(0, amplitudes[i]) * amplitude;
                }
            }
            else
            {
                amplitudes = new double[ts.Length];
                amplitudes.Fill(amplitude);
            }

            double startTime = tickToTime(partPos + vibratoStart);
            double endTime = tickToTime(partPos + vibratoEnd);
            double durTime = endTime - startTime;

            double[] times = new double[ts.Length];
            for (int i = 0; i < times.Length; i++)
            {
                times[i] = tickToTime(partPos + ts[i]) - startTime;
            }

            double attack = vibrato.Attack;
            for (int i = 0; i < times.Length; i++)
            {
                double r = times[i] / attack;
                if (r >= 1)
                    break;

                amplitudes[i] *= MathUtility.CubicInterpolation(r);
            }

            double release = vibrato.Release;
            for (int i = times.Length - 1; i >= 0; i--)
            {
                double r = (durTime - times[i]) / release;
                if (r >= 1)
                    break;

                amplitudes[i] *= MathUtility.CubicInterpolation(r);
            }

            double frequency = vibrato.Frequency;
            double phase = -vibrato.Phase * Math.PI;
            double w = 2 * Math.PI * frequency;
            for (int i = 0; i < times.Length; i++)
            {
                amplitudes[i] *= Math.Sin(w * times[i] + phase);
            }

            for (int i = 0; i < amplitudes.Length; i++)
            {
                values[i + offset] += amplitudes[i];
            }

            if (tickIndex == ticks.Count)
                break;
        }

        return values;
    }
}
