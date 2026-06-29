using TuneLab.SDK;
using Xunit;
using Xunit.Abstractions;

namespace TuneLab.Tests;

// PhonemeLayout.Resolve（合成域共享纯函数）的回归：可用空间内乘法 / 等比分配（缩放比 = r^w）——
// 同权重等比保形、异权重指数解 r、刚性不动、极限退二级等比压；跨 note 相接借前置、间隙归属各自；
// 不变量 = 音素绝不溢出可用空间。
public class PhonemeLayoutTests
{
    readonly ITestOutputHelper _out;
    public PhonemeLayoutTests(ITestOutputHelper o) => _out = o;

    static SynthesizedPhoneme Ph(string s, double dur, double w, bool lead = false)
        => new() { Symbol = s, Duration = dur, StretchWeight = w, IsLead = lead };

    static PhonemeLayoutNote Note(double s, double e, params SynthesizedPhoneme[] ph)
        => new() { FillStart = s, FillEnd = e, Phonemes = ph };

    // 二级压缩：核让到 0、辅音等比压，绝不超出 note。
    [Fact]
    public void SingleNote_SecondStage_NoOverflow()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0, 0.5, Ph("a", 0.3, 0), Ph("v", 0.3, 1), Ph("b", 0.3, 0)) });
        _out.WriteLine($"a={r[0][0]} v={r[0][1]} b={r[0][2]}");
        Assert.True(r[0][2].End <= 0.5 + 1e-9);
        Assert.Equal(0.0, r[0][1].Duration, 9);                 // 核归 0
        Assert.Equal(0.25, r[0][0].Duration, 9);                // a 等比 0.3*0.5/0.6
        Assert.Equal(0.25, r[0][2].Duration, 9);                // b 等比
    }

    // 同权重压缩 → 等比保形：缩放比统一，相对比例不变（乘法模型的核心）。
    [Fact]
    public void SingleNote_UniformWeight_Proportional()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0, 0.3, Ph("v1", 0.1, 1), Ph("v2", 0.5, 1)) });
        _out.WriteLine($"v1={r[0][0]} v2={r[0][1]}");
        // 同权重 → factor=0.3/0.6=0.5，各乘 0.5：比例恒为 0.1:0.5=1:5
        Assert.Equal(0.05, r[0][0].Duration, 9);
        Assert.Equal(0.25, r[0][1].Duration, 9);
        Assert.True(r[0][1].End <= 0.3 + 1e-9);
    }

    // 异权重拉伸 → 解 r：缩放比 = r^w（w=2 比 w=1 拉得更多）；w=0 不动。
    [Fact]
    public void SingleNote_DifferentWeight_SolveR()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0, 1.0, Ph("c", 0.1, 0, true), Ph("v1", 0.3, 1), Ph("v2", 0.2, 2)) });
        _out.WriteLine($"c={r[0][0]} v1={r[0][1]} v2={r[0][2]}");
        // 核空间=1.0, 解 0.3·r + 0.2·r² = 1.0 → r=(-0.3+√0.89)/0.4≈1.608495
        double rr = (-0.3 + System.Math.Sqrt(0.89)) / 0.4;
        Assert.Equal(0.3 * rr, r[0][1].Duration, 9);            // v1 = 0.3·r
        Assert.Equal(0.2 * rr * rr, r[0][2].Duration, 9);       // v2 = 0.2·r²
        Assert.True(r[0][2].Duration > r[0][1].Duration);       // w=2 拉得更多
        Assert.Equal(1.0, r[0][2].End, 6);                      // 占满核空间
        Assert.Equal(-0.1, r[0][0].Start, 9);                   // lead 往左堆原长 0.1
        Assert.Equal(0.0, r[0][0].End, 9);
    }

    // 全 w=0 拉伸 → 按原长等比拉伸占满（用户确认的退化）。
    [Fact]
    public void AllRigid_Stretch_Proportional()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0, 1.0, Ph("a", 0.2, 0), Ph("b", 0.2, 0)) });
        _out.WriteLine($"a={r[0][0]} b={r[0][1]}");
        Assert.Equal(0.5, r[0][0].Duration, 9);                 // 0.2 * 1.0/0.4
        Assert.Equal(0.5, r[0][1].Duration, 9);
        Assert.Equal(1.0, r[0][1].End, 9);                      // 占满，无留白
    }

    // 跨 note 相接：后 note 的 lead 并入前 note 可用空间；等权双核等比拉伸、借来的 lead 刚性不动。
    [Fact]
    public void CrossNote_BorrowLead_MultiCore()
    {
        // A 头0 末1.0；B 头1.0（相接）。A=[c lead, u#(w1,0.3), #n(w1,0.2)]，B=[s lead 0.4, u(w1)]
        var a = Note(0, 1.0, Ph("c", 0.1, 0, true), Ph("u#", 0.3, 1), Ph("#n", 0.2, 1));
        var b = Note(1.0, 2.0, Ph("s", 0.4, 0, true), Ph("u", 0.0, 1));
        var r = PhonemeLayout.Resolve(new[] { a, b });
        _out.WriteLine($"u#={r[0][1]} #n={r[0][2]} s(借)={r[1][0]} u={r[1][1]}");
        // 可用空间[0,1.0]，members=u#(w1,0.3)+#n(w1,0.2)+s(w0,0.4)。刚性 s=0.4，核空间=0.6，
        // 同权重核 factor=0.6/0.5=1.2：u#=0.36, #n=0.24（等比保形 0.3:0.2=3:2）
        Assert.Equal(0.36, r[0][1].Duration, 9);
        Assert.Equal(0.24, r[0][2].Duration, 9);
        Assert.Equal(0.4, r[1][0].Duration, 9);                 // 借来的 lead s 刚性不动
        Assert.Equal(1.0, r[1][0].End, 9);                      // s 末接 B 核起点
        Assert.Equal(0, r[1][0].Start - 0.6, 9);                // s 起 = 1.0-0.4
    }

    // 间隙：不相接，后 note 的 lead 自己往左堆、不被前 note 借。
    [Fact]
    public void Gap_LeadStaysWithOwner()
    {
        var a = Note(0, 0.5, Ph("v", 0.3, 1));
        var b = Note(1.0, 2.0, Ph("s", 0.2, 0, true), Ph("u", 0.5, 1));   // 间隙 [0.5,1.0]
        var r = PhonemeLayout.Resolve(new[] { a, b });
        _out.WriteLine($"v={r[0][0]} s={r[1][0]} u={r[1][1]}");
        Assert.Equal(0.5, r[0][0].Duration, 9);                 // A 核独占 [0,0.5]
        Assert.Equal(0.8, r[1][0].Start, 9);                    // s 自己往左堆：1.0-0.2
        Assert.Equal(1.0, r[1][0].End, 9);
    }
}
