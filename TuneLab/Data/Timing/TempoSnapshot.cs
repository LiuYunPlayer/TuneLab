using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.SDK;

namespace TuneLab.Data.Timing;

// ITiming 的不可变实现（宿主侧；SDK 契约面只声明 ITiming 接口，实现不进 SDK）：
// 构造时由最小真值 (Tick, Bpm) 一次推导出换算表，此后只读，可安全跨线程使用。
// 数据变更不同步进来——宿主在下次派发合成时构建一份全新快照（替换，而非同步）。
public sealed class TempoSnapshot : ITiming
{
    // 最小真值视图；某条 tempo 的实时位置用 ToSecond(Tempos[i].Tick) 取。
    public IReadOnlyList<TempoMark> Tempos => mTempos;

    // tempos 须升序（首条不必落在 0：tick 0 锚定 0 秒，首条之前含负位置按首条速度线性外推）；
    // ticksPerQuarter = 每四分音符 tick 数。
    public TempoSnapshot(IReadOnlyList<TempoMark> tempos, double ticksPerQuarter)
    {
        if (tempos.Count == 0)
            throw new ArgumentException("Tempo list cannot be empty.", nameof(tempos));

        mTempos = tempos.ToArray();
        mResolved = TempoConvert.Resolve(mTempos, ticksPerQuarter);
    }

    public double ToSecond(double tick) => TempoConvert.ToSecond(mResolved, tick);
    public double ToTick(double second) => TempoConvert.ToTick(mResolved, second);
    public double[] ToSeconds(IReadOnlyList<double> ticks) => TempoConvert.ToSeconds(mResolved, ticks);
    public double[] ToTicks(IReadOnlyList<double> seconds) => TempoConvert.ToTicks(mResolved, seconds);

    readonly TempoMark[] mTempos;
    readonly ResolvedTempoMark[] mResolved;
}
