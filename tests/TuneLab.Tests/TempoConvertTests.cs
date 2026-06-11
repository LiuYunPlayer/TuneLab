using TuneLab.Data;
using TuneLab.SDK.Base.Timing;
using TuneLab.SDK.Format.DataInfo;
using Xunit;

namespace TuneLab.Tests;

// tick↔秒换算：live TempoManager 与冻结 TempoSnapshot 共用 TempoConvert 唯一份纯函数，
// 此处断言两者对同一 tempo 表逐点全等（防两套实现漂移的回归锚）+ 换算语义本身的已知值。
public class TempoConvertTests
{
    static TempoManager MakeManager(params (double pos, double bpm)[] tempos)
    {
        var infos = tempos.Select(t => new TempoInfo { Pos = t.pos, Bpm = t.bpm }).ToList();
        return new TempoManager(null!, infos);
    }

    [Fact]
    public void KnownValues_SingleTempo120()
    {
        // 120 BPM：每秒 2 拍 = 960 tick/s；480 tick（一拍）= 0.5s。
        var manager = MakeManager((0, 120));
        Assert.Equal(0.0, manager.GetTime(0));
        Assert.Equal(0.5, manager.GetTime(480));
        Assert.Equal(-0.5, manager.GetTime(-480));   // 负位置按首条速度线性外推
        Assert.Equal(480.0, manager.GetTick(0.5));
    }

    [Fact]
    public void KnownValues_MultiTempo()
    {
        // 0~1920 tick 走 120 BPM（960 tick/s，耗时 2s），其后 60 BPM（480 tick/s）。
        var manager = MakeManager((0, 120), (1920, 60));
        Assert.Equal(2.0, manager.GetTime(1920));
        Assert.Equal(3.0, manager.GetTime(2400));
        Assert.Equal(2400.0, manager.GetTick(3.0));
    }

    [Fact]
    public void Snapshot_MatchesLive_Elementwise()
    {
        var manager = MakeManager((0, 120), (960, 89.7), (1920, 60), (5000, 233));
        var snapshot = manager.CreateSnapshot();

        double[] ticks = [-1000, -1, 0, 1, 479.5, 960, 961, 1919.99, 1920, 3333.25, 5000, 99999];
        var live = manager.GetTimes(ticks);
        var frozen = snapshot.ToSeconds(ticks);
        for (int i = 0; i < ticks.Length; i++)
            Assert.Equal(live[i], frozen[i]);   // 全等，不带容差

        double[] seconds = [-2, 0, 0.1, 1.999, 2, 7.3, 100];
        var liveTicks = manager.GetTicks(seconds);
        var frozenTicks = snapshot.ToTick(seconds);
        for (int i = 0; i < seconds.Length; i++)
            Assert.Equal(liveTicks[i], frozenTicks[i]);
    }

    [Fact]
    public void Snapshot_CopiesMarkValues()
    {
        var manager = MakeManager((0, 120), (1920, 60));
        var snapshot = manager.CreateSnapshot();

        Assert.Equal(2, snapshot.Tempos.Count);
        Assert.Equal(0.0, snapshot.Tempos[0].Tick);
        Assert.Equal(120.0, snapshot.Tempos[0].Bpm);
        Assert.Equal(960.0, snapshot.Tempos[0].TicksPerSecond);
        Assert.Equal(1920.0, snapshot.Tempos[1].Tick);
        Assert.Equal(2.0, snapshot.Tempos[1].Seconds);
    }

    [Fact]
    public void Scalar_EqualsBatch()
    {
        var manager = MakeManager((0, 120), (1920, 61.3));
        var snapshot = manager.CreateSnapshot();

        double[] ticks = [-5, 0, 100.5, 1920, 4000];
        var batch = snapshot.ToSeconds(ticks);
        for (int i = 0; i < ticks.Length; i++)
            Assert.Equal(batch[i], snapshot.ToSeconds(ticks[i]));
    }

    [Fact]
    public void RoundTrip_TickToSecondsToTick()
    {
        var snapshot = MakeManager((0, 120), (960, 89.7), (1920, 60)).CreateSnapshot();
        double[] ticks = [-100, 0, 1, 480, 960, 1500, 1920, 100000];
        foreach (var tick in ticks)
            Assert.Equal(tick, snapshot.ToTick(snapshot.ToSeconds(tick)), 6);
    }
}
