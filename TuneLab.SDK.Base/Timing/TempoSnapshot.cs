namespace TuneLab.SDK.Base.Timing;

// ITiming 的不可变实现：构造时把 tempo 表物化为纯值快照，此后只读，可安全跨线程（将来跨进程）使用。
// 数据变更不同步进来——宿主在下次派发合成时捕获一份全新快照（替换，而非同步）。
public sealed class TempoSnapshot : ITiming
{
    public IReadOnlyList<TempoMark> Tempos => mTempos;

    public TempoSnapshot(IEnumerable<ITempoMark> tempos)
    {
        mTempos = tempos.Select(t => new TempoMark(t.Tick, t.Seconds, t.Bpm, t.TicksPerSecond)).ToArray();
        if (mTempos.Length == 0)
            throw new ArgumentException("Tempo list cannot be empty.", nameof(tempos));
    }

    public double ToSeconds(double tick) => TempoConvert.ToSeconds(mTempos, tick);
    public double ToTick(double seconds) => TempoConvert.ToTick(mTempos, seconds);
    public double[] ToSeconds(IReadOnlyList<double> ticks) => TempoConvert.ToSeconds(mTempos, ticks);
    public double[] ToTick(IReadOnlyList<double> seconds) => TempoConvert.ToTicks(mTempos, seconds);

    readonly TempoMark[] mTempos;
}
