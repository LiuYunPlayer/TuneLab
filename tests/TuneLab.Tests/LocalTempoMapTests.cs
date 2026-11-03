using TuneLab.Data.Timing;
using Xunit;

namespace TuneLab.Tests;

// 画布与合成侧共用的速度映射：局部 tick（相对原点）↔ 全局秒。
public class LocalTempoMapTests
{
    // 120 BPM 到 1920 tick，之后 60 BPM（480 tick / 拍）。
    static readonly TempoSnapshot Tempo = new([new TempoMark(0, 120), new TempoMark(1920, 60)], 480);

    [Fact]
    public void LocalTickIsRelativeToTheOrigin_SecondsStayGlobal()
    {
        var map = new LocalTempoMap(Tempo, 960);
        Assert.Equal(Tempo.ToSecond(960), map.PosToTime(0), 9);      // 局部 0 = 原点
        Assert.Equal(Tempo.ToSecond(2400), map.PosToTime(1440), 9);  // 跨过速度变化处照样按工程速度表算
        Assert.Equal(1440, map.TimeToPos(Tempo.ToSecond(2400)), 9);
    }

    // 拆分后右半的原点仍在原处：它起点之前的数据局部 tick 为负，照样能换算。
    [Fact]
    public void NegativeLocalTicksWork()
    {
        var map = new LocalTempoMap(Tempo, 1920);
        Assert.Equal(Tempo.ToSecond(1440), map.PosToTime(-480), 9);
        Assert.Equal(-480, map.TimeToPos(Tempo.ToSecond(1440)), 9);
    }

    // 合成侧只给 part 原点的一份映射，音符级原点由插件用 StartTime 换出——与画布侧以音符起点为原点的映射逐点一致。
    [Fact]
    public void ContextMapPlusStartTimeOriginMatchesTheCanvasNoteMap()
    {
        double partPos = 480, noteStartTick = 1680;               // 音符起点跨在速度变化（1920）之前
        var context = new LocalTempoMap(Tempo, partPos);
        var canvas = new LocalTempoMap(Tempo, noteStartTick);
        double startTime = Tempo.ToSecond(noteStartTick);          // 音符代理的 StartTime 就是这样换来的
        double origin = context.TimeToPos(startTime);
        foreach (double pos in new[] { -240.0, 0, 120, 960 })      // 跨过速度变化处、也含负的局部 tick
            Assert.Equal(canvas.PosToTime(pos), context.PosToTime(origin + pos), 9);
    }
}
