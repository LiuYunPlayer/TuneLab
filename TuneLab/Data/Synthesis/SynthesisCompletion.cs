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
// 不自己派就永远等不到。故 DriveOnce 做的是与 SynthesisNext 同样的事，只是默认窗口取全轴（要的是
//「全都合成完」，不是「播放头附近优先」）。编辑器里与那个定时器并存无害：两者都在数据线程上串行，
// 各自派活前都重数一遍在飞数，故合起来也不会越过 EffectTaskGate.Limit。
internal static class SynthesisCompletion
{
    // 一次驱动：给范围内的空闲管线派活，并回报此刻范围内还剩多少。Done = 范围内没有在飞的、也没有待合成的。
    // 必须在数据线程（编辑器的 UI 线程 / headless 的泵线程）上调用。
    public static SynthesisTick DriveOnce(IProject project, SynthesisScope scope) => Scan(project, scope, dispatch: true);

    // 只数不派：同一套判据，但一个活儿都不派出去。状态查询用它——「查一下还剩多少」不该顺手开工，
    // 否则读命令会变成写命令。headless 里因此恒报 pending（那边没有任何人派活），那是事实而非缺陷。
    public static SynthesisTick Survey(IProject project, SynthesisScope scope) => Scan(project, scope, dispatch: false);

    static SynthesisTick Scan(IProject project, SynthesisScope scope, bool dispatch)
    {
        int limit = EffectTaskGate.Limit;
        // 【为什么要两套计数】派活预算与完成判据是两件事，加了范围过滤后就不能再共用一个变量：
        //   · inFlight 必须数【全工程】的在飞数——EffectTaskGate.Limit 是全局闸门，只数范围内的话，
        //     范围外正在跑的活儿看不见，这一轮就会越过上限派出去，资源失控。
        //   · busy/pending 只数【范围内】的——否则范围外有活儿在跑就永远判不出 Done，等到超时为止，
        //     而那个超时无从解释（用户要的那一段其实早就好了）。
        int inFlight = 0;
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

                // 在飞数先记账，再谈范围：范围外的活儿照样占着全局闸门的名额。
                bool isBusy = pipeline.IsBusy;
                if (isBusy)
                    inFlight++;

                if (!scope.Covers(track, part))
                    continue;

                if (isBusy)
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

                if (pipeline.PeekNext(scope.StartTime, scope.EndTime) is null)
                    continue;

                pending++;
                idle.Add(pipeline);
            }
        }

        if (dispatch)
        {
            foreach (var pipeline in idle)
            {
                if (inFlight >= limit)
                    break;

                // 与 peek 同一窗口：插件据此确定性重导出 peek 报出的同一块。
                pipeline.Dispatch(scope.StartTime, scope.EndTime);
                inFlight++;
                busy++;
            }
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
    public static SynthesisFacts Inspect(IProject project, SynthesisScope scope)
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

                if (!scope.Covers(track, part))
                    continue;

                string where = Where(trackIndex, partIndex, part);

                foreach (var segment in midiPart.GetSynthesisStatus())
                {
                    if (segment.EndTime <= segment.StartTime)
                        continue;

                    // 只报与范围相交的段。段的范围照原样报、不按窗口裁剪：裁出来的边界不是引擎说过的事，
                    // 报它等于编造一个「失败范围」。
                    if (segment.EndTime <= scope.StartTime || segment.StartTime >= scope.EndTime)
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

    // 范围内逐 part 的状态带内容，原样转述（不做区间代数、不合并、不裁剪——那是画家算法的平铺列表，
    // 谁在上面由 z 序决定，见 SynthesisDisplaySegment）。`project synthesis-status` 的数据源。
    public static IReadOnlyList<SynthesisPartStatus> Snapshot(IProject project, SynthesisScope scope)
    {
        var list = new List<SynthesisPartStatus>();

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

                if (!scope.Covers(track, part))
                    continue;

                var segments = new List<SynthesisDisplaySegment>();
                foreach (var segment in midiPart.GetSynthesisStatus())
                {
                    if (segment.EndTime <= segment.StartTime)
                        continue;
                    if (segment.EndTime <= scope.StartTime || segment.StartTime >= scope.EndTime)
                        continue;
                    segments.Add(segment);
                }

                // HasPipeline=false 即「这个 part 压根不会被合成」——没有音源，或它的扩展没装上。
                // 那与「有音源、只是还没轮到它」是两回事，回报必须分得开。
                list.Add(new SynthesisPartStatus(Where(trackIndex, partIndex, part), midiPart.SynthesisPipeline != null, segments));
            }
        }

        return list;
    }

    // 定位与 project status / preset 那一族同一套：1-based 轨号 + part 号（含音频 part，故与
    // get_project_overview 报的编号对得上），有名字就带上。
    static string Where(int trackIndex, int partIndex, IPart part)
    {
        string where = string.Format("track {0} part {1}", trackIndex, partIndex);
        var name = part.Name.Value;
        if (!string.IsNullOrEmpty(name))
            where += string.Format(" (\"{0}\")", name);
        return where;
    }
}

// 一次合成驱动 / 查询的作用范围：时间窗 × 轨 / part 过滤。All = 全曲全轴。
//
// 过滤持有的是【对象引用】而非 1-based 编号：编号只在命令的参数面上存在，一进来就解析成对象，
// 之后的每一层都不必再关心"第几个"，也就不可能在某一层数错（音频 part 算不算一个、等等）。
internal readonly record struct SynthesisScope(ITrack? Track, IPart? Part, double StartTime, double EndTime)
{
    public static readonly SynthesisScope All = new(null, null, double.MinValue, double.MaxValue);

    public bool Covers(ITrack track, IPart part)
        => (Track is null || ReferenceEquals(track, Track))
        && (Part is null || ReferenceEquals(part, Part));
}

// 一个 part 此刻的状态带内容。HasPipeline=false 时 Segments 恒空，但那不是「还没开始」，见 Snapshot。
internal readonly record struct SynthesisPartStatus(string Where, bool HasPipeline, IReadOnlyList<SynthesisDisplaySegment> Segments);

internal readonly record struct SynthesisTick(bool Done, int Busy, int Pending);

// 一处「链尾不是应有结果」的范围（时间单位=秒，与状态带同一口径）。
internal readonly record struct SynthesisFlaw(string Where, double StartTime, double EndTime, string? Message);

// Silent=会被导成静音的范围（引擎自报失败）；Degraded=导得出声音但不是应有结果（effect 降级 passthrough）。
// 两者后果不同故分开：前者默认拦住导出，后者只警告。
internal readonly record struct SynthesisFacts(IReadOnlyList<SynthesisFlaw> Silent, IReadOnlyList<SynthesisFlaw> Degraded);
