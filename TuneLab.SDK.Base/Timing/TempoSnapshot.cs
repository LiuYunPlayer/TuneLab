namespace TuneLab.SDK.Base.Timing;

// ITiming 的不可变实现：构造时由最小真值 (Tick, Bpm) 一次推导出换算表，此后只读，
// 可安全跨线程（将来跨进程）使用。数据变更不同步进来——宿主在下次派发合成时构建一份全新快照（替换，而非同步）。
public sealed class TempoSnapshot : ITiming
{
    // 最小真值视图；某条 tempo 的实时位置用 ToSeconds(Tempos[i].Tick) 取。
    public IReadOnlyList<TempoMark> Tempos => mTempos;

    // tempos 须升序且首条 Tick = 0（负位置按首条速度线性外推）；ticksPerQuarter = 每四分音符 tick 数。
    public TempoSnapshot(IReadOnlyList<TempoMark> tempos, double ticksPerQuarter)
    {
        if (tempos.Count == 0)
            throw new ArgumentException("Tempo list cannot be empty.", nameof(tempos));

        mTempos = tempos.ToArray();
        mResolved = TempoConvert.Resolve(mTempos, ticksPerQuarter);
    }

    public double ToSeconds(double tick) => TempoConvert.ToSeconds(mResolved, tick);
    public double ToTick(double seconds) => TempoConvert.ToTick(mResolved, seconds);
    public double[] ToSeconds(IReadOnlyList<double> ticks) => TempoConvert.ToSeconds(mResolved, ticks);
    public double[] ToTick(IReadOnlyList<double> seconds) => TempoConvert.ToTicks(mResolved, seconds);

    readonly TempoMark[] mTempos;
    readonly ResolvedTempoMark[] mResolved;
}
