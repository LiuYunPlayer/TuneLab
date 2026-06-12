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
        var frozenTicks = snapshot.ToTicks(seconds);
        for (int i = 0; i < seconds.Length; i++)
            Assert.Equal(liveTicks[i], frozenTicks[i]);
    }

    [Fact]
    public void Snapshot_CopiesMinimalMarks()
    {
        var manager = MakeManager((0, 120), (1920, 60));
        var snapshot = manager.CreateSnapshot();

        // 快照只暴露最小真值 (Tick, Bpm)；秒/换算系数是构造内推导的私有派生值，经换算结果验证。
        Assert.Equal(2, snapshot.Tempos.Count);
        Assert.Equal(0.0, snapshot.Tempos[0].Tick);
        Assert.Equal(120.0, snapshot.Tempos[0].Bpm);
        Assert.Equal(1920.0, snapshot.Tempos[1].Tick);
        Assert.Equal(60.0, snapshot.Tempos[1].Bpm);
        Assert.Equal(2.0, snapshot.ToSecond(1920));
    }

    [Fact]
    public void Snapshot_ConstructibleFromMinimalMarks()
    {
        // SDK 侧自包含构造路径（即将来插件进程从序列化 marks 重建快照的形态）。
        var snapshot = new TempoSnapshot([new TempoMark(0, 120), new TempoMark(1920, 60)], 480);
        Assert.Equal(2.0, snapshot.ToSecond(1920));
        Assert.Equal(2400.0, snapshot.ToTick(3.0));
    }

    [Fact]
    public void FirstMarkNotAtZero_ExtrapolatesWithFirstBpm()
    {
        // 首条 mark 不必落在 tick 0：tick 0 锚定 0 秒，首条之前（含负位置）按首条速度外推。
        var snapshot = new TempoSnapshot([new TempoMark(960, 120), new TempoMark(1920, 60)], 480);

        Assert.Equal(0.0, snapshot.ToSecond(0));
        Assert.Equal(0.5, snapshot.ToSecond(480));     // 外推区：960 tick/s
        Assert.Equal(-0.5, snapshot.ToSecond(-480));
        Assert.Equal(1.0, snapshot.ToSecond(960));
        Assert.Equal(2.0, snapshot.ToSecond(1920));    // 960→1920 @120BPM：+1s
        Assert.Equal(3.0, snapshot.ToSecond(2400));    // 其后 60BPM：480 tick = 1s

        Assert.Equal(0.0, snapshot.ToTick(0.0));
        Assert.Equal(-480.0, snapshot.ToTick(-0.5));
        Assert.Equal(2400.0, snapshot.ToTick(3.0));
    }

    [Fact]
    public void Edit_RefreshesConversion()
    {
        // live 换算是惰性缓存的快照，任何编辑（经 Modified 通知）都须失效重建。
        var manager = MakeManager((0, 120));
        Assert.Equal(0.5, manager.GetTime(480));

        manager.SetBpm(0, 60);
        Assert.Equal(1.0, manager.GetTime(480));

        manager.AddTempo(960, 120);
        Assert.Equal(2.0, manager.GetTime(960));    // 60 BPM 段：960 tick = 2s
        Assert.Equal(2.5, manager.GetTime(1440));   // 其后 120 BPM：480 tick = 0.5s

        var snapshot = manager.CreateSnapshot();
        Assert.Equal(manager.GetTime(1234.5), snapshot.ToSecond(1234.5));
    }

    [Fact]
    public void Scalar_EqualsBatch()
    {
        var manager = MakeManager((0, 120), (1920, 61.3));
        var snapshot = manager.CreateSnapshot();

        double[] ticks = [-5, 0, 100.5, 1920, 4000];
        var batch = snapshot.ToSeconds(ticks);
        for (int i = 0; i < ticks.Length; i++)
            Assert.Equal(batch[i], snapshot.ToSecond(ticks[i]));
    }

    [Fact]
    public void RoundTrip_TickToSecondToTick()
    {
        var snapshot = MakeManager((0, 120), (960, 89.7), (1920, 60)).CreateSnapshot();
        double[] ticks = [-100, 0, 1, 480, 960, 1500, 1920, 100000];
        foreach (var tick in ticks)
            Assert.Equal(tick, snapshot.ToTick(snapshot.ToSecond(tick)), 6);
    }
}
