using TuneLab.Data;
using TuneLab.Data.Timing;
using TuneLab.SDK.Format.DataInfo;
using Xunit;

namespace TuneLab.Tests;

// tick鈫旂鎹㈢畻锛歭ive TempoManager 涓庡喕缁?TempoSnapshot 鍏辩敤 TempoConvert 鍞竴浠界函鍑芥暟锛?
// 姝ゅ鏂█涓よ€呭鍚屼竴 tempo 琛ㄩ€愮偣鍏ㄧ瓑锛堥槻涓ゅ瀹炵幇婕傜Щ鐨勫洖褰掗敋锛? 鎹㈢畻璇箟鏈韩鐨勫凡鐭ュ€笺€?
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
        // 120 BPM锛氭瘡绉?2 鎷?= 960 tick/s锛?80 tick锛堜竴鎷嶏級= 0.5s銆?
        var manager = MakeManager((0, 120));
        Assert.Equal(0.0, manager.GetTime(0));
        Assert.Equal(0.5, manager.GetTime(480));
        Assert.Equal(-0.5, manager.GetTime(-480));   // 璐熶綅缃寜棣栨潯閫熷害绾挎€у鎺?
        Assert.Equal(480.0, manager.GetTick(0.5));
    }

    [Fact]
    public void KnownValues_MultiTempo()
    {
        // 0~1920 tick 璧?120 BPM锛?60 tick/s锛岃€楁椂 2s锛夛紝鍏跺悗 60 BPM锛?80 tick/s锛夈€?
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
            Assert.Equal(live[i], frozen[i]);   // 鍏ㄧ瓑锛屼笉甯﹀宸?

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

        // 蹇収鍙毚闇叉渶灏忕湡鍊?(Tick, Bpm)锛涚/鎹㈢畻绯绘暟鏄瀯閫犲唴鎺ㄥ鐨勭鏈夋淳鐢熷€硷紝缁忔崲绠楃粨鏋滈獙璇併€?
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
        // SDK 渚ц嚜鍖呭惈鏋勯€犺矾寰勶紙鍗冲皢鏉ユ彃浠惰繘绋嬩粠搴忓垪鍖?marks 閲嶅缓蹇収鐨勫舰鎬侊級銆?
        var snapshot = new TempoSnapshot([new TempoMark(0, 120), new TempoMark(1920, 60)], 480);
        Assert.Equal(2.0, snapshot.ToSecond(1920));
        Assert.Equal(2400.0, snapshot.ToTick(3.0));
    }

    [Fact]
    public void FirstMarkNotAtZero_ExtrapolatesWithFirstBpm()
    {
        // 棣栨潯 mark 涓嶅繀钀藉湪 tick 0锛歵ick 0 閿氬畾 0 绉掞紝棣栨潯涔嬪墠锛堝惈璐熶綅缃級鎸夐鏉￠€熷害澶栨帹銆?
        var snapshot = new TempoSnapshot([new TempoMark(960, 120), new TempoMark(1920, 60)], 480);

        Assert.Equal(0.0, snapshot.ToSecond(0));
        Assert.Equal(0.5, snapshot.ToSecond(480));     // 澶栨帹鍖猴細960 tick/s
        Assert.Equal(-0.5, snapshot.ToSecond(-480));
        Assert.Equal(1.0, snapshot.ToSecond(960));
        Assert.Equal(2.0, snapshot.ToSecond(1920));    // 960鈫?920 @120BPM锛?1s
        Assert.Equal(3.0, snapshot.ToSecond(2400));    // 鍏跺悗 60BPM锛?80 tick = 1s

        Assert.Equal(0.0, snapshot.ToTick(0.0));
        Assert.Equal(-480.0, snapshot.ToTick(-0.5));
        Assert.Equal(2400.0, snapshot.ToTick(3.0));
    }

    [Fact]
    public void Edit_RefreshesConversion()
    {
        // live 鎹㈢畻鏄儼鎬х紦瀛樼殑蹇収锛屼换浣曠紪杈戯紙缁?Modified 閫氱煡锛夐兘椤诲け鏁堥噸寤恒€?
        var manager = MakeManager((0, 120));
        Assert.Equal(0.5, manager.GetTime(480));

        manager.SetBpm(0, 60);
        Assert.Equal(1.0, manager.GetTime(480));

        manager.AddTempo(960, 120);
        Assert.Equal(2.0, manager.GetTime(960));    // 60 BPM 娈碉細960 tick = 2s
        Assert.Equal(2.5, manager.GetTime(1440));   // 鍏跺悗 120 BPM锛?80 tick = 0.5s

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
