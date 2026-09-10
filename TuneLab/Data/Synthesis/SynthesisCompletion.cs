using System;
using System.Collections.Generic;

namespace TuneLab.Data.Synthesis;

// 「把整个工程的合成跑完，然后如实说链尾上是什么」——命令面导出音频要的那件事。
//
// 【为什么导出前必须先跑这一步】AudioEngine.ExportMaster 拉的是 AudioGraph 此刻的数据，不等任何人。
// 界面上的导出之所以行得通，是因为用户看着状态带自己等到全绿才按下去——那个「等」是人做的，不在代码里。
// 命令面照搬就会在合成还没跑完时静默产出静音段，而回报仍说导出成功。
//
// 【为什么要自己派活】编辑器里派活的是 Editor 那个 50ms 定时器（SynthesisNext）；headless 进程里没有它，
// 不自己派就永远等不到。故 DriveOnce 做的是与 SynthesisNext 同样的事，只是窗口取全轴（要的是「全都合成完」，
// 不是「播放头附近优先」）。编辑器里与那个定时器并存无害：两者都在数据线程上串行，各自派活前都重数一遍
// 在飞数，故合起来也不会越过 EffectTaskGate.Limit。
internal static class SynthesisCompletion
{
    // 一次驱动：给空闲管线派活，并回报此刻还剩多少。Done = 没有在飞的、也没有待合成的。
    // 必须在数据线程（编辑器的 UI 线程 / headless 的泵线程）上调用。
    public static SynthesisTick DriveOnce(IProject project)
    {
        int limit = EffectTaskGate.Limit;
        int busy = 0;
        int pending = 0;
        var idle = new List<ISynthesisPipeline>();

        foreach (var track in project.Tracks)
        {
            foreach (var part in track.Parts)
            {
                if (part is not MidiPart midiPart)
                    continue;

                var pipeline = midiPart.SynthesisPipeline;
                if (pipeline == null)
                    continue;

                if (pipeline.IsBusy)
                {
                    busy++;
                    continue;
                }

                // 批量编辑收口前不派活（同 SynthesisNext：对中间态做无用功）。但它仍然算「没干完」——
                // 收不了口就该一直等到超时如实报告，而不是当作合成完了去导出。
                if (midiPart.IsSynthesisBatching)
                {
                    pending++;
                    continue;
                }

                if (pipeline.PeekNext(double.MinValue, double.MaxValue) is null)
                    continue;

                pending++;
                idle.Add(pipeline);
            }
        }

        foreach (var pipeline in idle)
        {
            if (busy >= limit)
                break;

            // 与 peek 同一窗口（全轴）：插件据此确定性重导出 peek 报出的同一块。
            pipeline.Dispatch(double.MinValue, double.MaxValue);
            busy++;
        }

        return new SynthesisTick(busy == 0 && pending == 0, busy, pending);
    }

    // 合成落定之后，链尾的事实：哪些范围是【无声】的（引擎自报失败）、哪些是【降级】的
    // （effect 某级失败、放的是未经处理的音频）。
    //
    // 【为什么可以直接扫 Failed 段而不做区间代数】状态带是画家算法的平铺列表，但 Failed 只从
    // 「活动会话声称」那一层来，而那一层恒在最上（见 SynthesisDisplaySegment 的 z 序说明），
    // 故没有任何东西能盖住它。唯一可能与它重叠的同层邻居是 Synthesizing，而本函数只在合成落定后调用，
    // 那时不会再有 Synthesizing。
    public static SynthesisFacts Inspect(IProject project)
    {
        var silent = new List<SynthesisFlaw>();
        var degraded = new List<SynthesisFlaw>();

        int trackIndex = 0;
        foreach (var track in project.Tracks)
        {
            trackIndex++;
            int partIndex = 0;
            foreach (var part in track.Parts)
            {
                partIndex++;
                if (part is not MidiPart midiPart)
                    continue;

                // 定位与 project status / preset 那一族同一套：1-based 轨号 + part 号。
                string where = string.Format("track {0} part {1}", trackIndex, partIndex);
                var name = part.Name.Value;
                if (!string.IsNullOrEmpty(name))
                    where += string.Format(" (\"{0}\")", name);

                foreach (var segment in midiPart.GetSynthesisStatus())
                {
                    if (segment.EndTime <= segment.StartTime)
                        continue;

                    if (segment.State == SynthesisDisplayState.Failed)
                        silent.Add(new SynthesisFlaw(where, segment.StartTime, segment.EndTime, segment.Message));
                    else if (segment.State == SynthesisDisplayState.Degraded)
                        degraded.Add(new SynthesisFlaw(where, segment.StartTime, segment.EndTime, segment.Message));
                }
            }
        }

        return new SynthesisFacts(silent, degraded);
    }
}

internal readonly record struct SynthesisTick(bool Done, int Busy, int Pending);

// 一处「链尾不是应有结果」的范围（时间单位=秒，与状态带同一口径）。
internal readonly record struct SynthesisFlaw(string Where, double StartTime, double EndTime, string? Message);

// Silent=会被导成静音的范围（引擎自报失败）；Degraded=导得出声音但不是应有结果（effect 降级 passthrough）。
// 两者后果不同故分开：前者默认拦住导出，后者只警告。
internal readonly record struct SynthesisFacts(IReadOnlyList<SynthesisFlaw> Silent, IReadOnlyList<SynthesisFlaw> Degraded);
