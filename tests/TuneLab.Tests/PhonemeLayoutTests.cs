using TuneLab.SDK;
using Xunit;
using Xunit.Abstractions;

namespace TuneLab.Tests;

// PhonemeLayout.Resolve（合成域共享纯函数）的回归：可用空间内乘法 / 等比分配（缩放比 = r^w）——
// 同权重等比保形、异权重指数解 r、刚性不动、极限退二级等比压；note 头(Preutterance)切拍前/拍后两域、
// 跨 note 相接借拍前料、间隙归属各自；跨拍音素本体一分为二锚 note 头处相接；不变量 = 音素绝不溢出可用空间。
public class PhonemeLayoutTests
{
    readonly ITestOutputHelper _out;
    public PhonemeLayoutTests(ITestOutputHelper o) => _out = o;

    static SynthesizedPhoneme Ph(string s, double dur, double w)
        => new() { Symbol = s, Duration = dur, StretchWeight = w };

    // preutter = note 头之前音素的占位长度（拍前发声量）。切在 lead/核边界时 = Σ前置音素时长。
    static PhonemeLayoutNote Note(double preutter, double s, double e, params SynthesizedPhoneme[] ph)
        => new() { Preutterance = preutter, FillStart = s, FillEnd = e, Phonemes = ph };

    // 二级压缩：核让到 0、辅音等比压，绝不超出 note。
    [Fact]
    public void SingleNote_SecondStage_NoOverflow()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0, 0, 0.5, Ph("a", 0.3, 0), Ph("v", 0.3, 1), Ph("b", 0.3, 0)) });
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
        var r = PhonemeLayout.Resolve(new[] { Note(0, 0, 0.3, Ph("v1", 0.1, 1), Ph("v2", 0.5, 1)) });
        _out.WriteLine($"v1={r[0][0]} v2={r[0][1]}");
        // 同权重 → factor=0.3/0.6=0.5，各乘 0.5：比例恒为 0.1:0.5=1:5
        Assert.Equal(0.05, r[0][0].Duration, 9);
        Assert.Equal(0.25, r[0][1].Duration, 9);
        Assert.True(r[0][1].End <= 0.3 + 1e-9);
    }

    // 异权重拉伸 → 解 r：缩放比 = r^w（w=2 比 w=1 拉得更多）；w=0 不动。前置 c 占位 0.1 往左堆。
    [Fact]
    public void SingleNote_DifferentWeight_SolveR()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0.1, 0, 1.0, Ph("c", 0.1, 0), Ph("v1", 0.3, 1), Ph("v2", 0.2, 2)) });
        _out.WriteLine($"c={r[0][0]} v1={r[0][1]} v2={r[0][2]}");
        // 核空间=1.0, 解 0.3·r + 0.2·r² = 1.0 → r=(-0.3+√0.89)/0.4≈1.608495
        double rr = (-0.3 + System.Math.Sqrt(0.89)) / 0.4;
        Assert.Equal(0.3 * rr, r[0][1].Duration, 9);            // v1 = 0.3·r
        Assert.Equal(0.2 * rr * rr, r[0][2].Duration, 9);       // v2 = 0.2·r²
        Assert.True(r[0][2].Duration > r[0][1].Duration);       // w=2 拉得更多
        Assert.Equal(1.0, r[0][2].End, 6);                      // 占满核空间
        Assert.Equal(-0.1, r[0][0].Start, 9);                   // 前置往左堆原长 0.1
        Assert.Equal(0.0, r[0][0].End, 9);
    }

    // 全 w=0 拉伸 → 按原长等比拉伸占满（用户确认的退化）。
    [Fact]
    public void AllRigid_Stretch_Proportional()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0, 0, 1.0, Ph("a", 0.2, 0), Ph("b", 0.2, 0)) });
        _out.WriteLine($"a={r[0][0]} b={r[0][1]}");
        Assert.Equal(0.5, r[0][0].Duration, 9);                 // 0.2 * 1.0/0.4
        Assert.Equal(0.5, r[0][1].Duration, 9);
        Assert.Equal(1.0, r[0][1].End, 9);                      // 占满，无留白
    }

    // 跨 note 相接：后 note 的拍前料并入前 note 可用空间；等权双核等比拉伸、借来的前置刚性不动。
    [Fact]
    public void CrossNote_BorrowLead_MultiCore()
    {
        // A 头0 末1.0；B 头1.0（相接）。A=[c(前置0.1) u#(w1,0.3) #n(w1,0.2)]，B=[s(前置0.4) u(w1,0)]
        var a = Note(0.1, 0, 1.0, Ph("c", 0.1, 0), Ph("u#", 0.3, 1), Ph("#n", 0.2, 1));
        var b = Note(0.4, 1.0, 2.0, Ph("s", 0.4, 0), Ph("u", 0.0, 1));
        var r = PhonemeLayout.Resolve(new[] { a, b });
        _out.WriteLine($"u#={r[0][1]} #n={r[0][2]} s(借)={r[1][0]} u={r[1][1]}");
        // 可用空间[0,1.0]，members=u#(w1,0.3)+#n(w1,0.2)+s(w0,0.4)。刚性 s=0.4，核空间=0.6，
        // 同权重核 factor=0.6/0.5=1.2：u#=0.36, #n=0.24（等比保形 0.3:0.2=3:2）
        Assert.Equal(0.36, r[0][1].Duration, 9);
        Assert.Equal(0.24, r[0][2].Duration, 9);
        Assert.Equal(0.4, r[1][0].Duration, 9);                 // 借来的前置 s 刚性不动
        Assert.Equal(1.0, r[1][0].End, 9);                      // s 末接 B 核起点
        Assert.Equal(0, r[1][0].Start - 0.6, 9);                // s 起 = 1.0-0.4
    }

    // 间隙：不相接，后 note 的拍前料自己往左堆、不被前 note 借。
    [Fact]
    public void Gap_LeadStaysWithOwner()
    {
        var a = Note(0, 0, 0.5, Ph("v", 0.3, 1));
        var b = Note(0.2, 1.0, 2.0, Ph("s", 0.2, 0), Ph("u", 0.5, 1));   // 间隙 [0.5,1.0]
        var r = PhonemeLayout.Resolve(new[] { a, b });
        _out.WriteLine($"v={r[0][0]} s={r[1][0]} u={r[1][1]}");
        Assert.Equal(0.5, r[0][0].Duration, 9);                 // A 核独占 [0,0.5]
        Assert.Equal(0.8, r[1][0].Start, 9);                    // s 自己往左堆：1.0-0.2
        Assert.Equal(1.0, r[1][0].End, 9);
    }

    // 跨拍（孤立 note）：单个弹性音素 w 的 note 头切在其内部（前 0.15 / 后 0.25）。前半按自然长往左堆、
    // 后半在本域弹性填满，两半锚 note 头(0)处拼回一整块。
    [Fact]
    public void Straddle_SinglePhoneme_SpansHead()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0.15, 0, 0.5, Ph("w", 0.4, 1)) });
        _out.WriteLine($"w={r[0][0]}");
        Assert.Equal(-0.15, r[0][0].Start, 9);                  // 前半自然长 0.15 往左堆：0-0.15
        Assert.Equal(0.5, r[0][0].End, 9);                      // 后半弹性填满本域 [0,0.5]
        Assert.True(r[0][0].Start < 0 && r[0][0].End > 0);      // 确实横跨 note 头
    }

    // 跨拍 + 借用：后 note B 的首音素 g 跨 B 头，前半借入前 note A 域压缩、后半在 B 域，
    // 两半锚 B 头(1.0)处相接拼回一块，横跨 note 头。
    [Fact]
    public void Straddle_BorrowedAcrossNotes()
    {
        // A 头0 末1.0 [v(w1,0.5)]；B 头1.0 末1.1 [g(w0,0.2)]，Preutterance=0.1 → g 跨 B 头(前0.1/后0.1)
        var a = Note(0, 0, 1.0, Ph("v", 0.5, 1));
        var b = Note(0.1, 1.0, 1.1, Ph("g", 0.2, 0));
        var r = PhonemeLayout.Resolve(new[] { a, b });
        _out.WriteLine($"v={r[0][0]} g={r[1][0]}");
        // A 域[0,1.0]：members=v(w1,0.5)+g前半(w0,0.1)。刚性 0.1，核空间 0.9，v 独核填 0.9 → v=[0,0.9]，g前半=[0.9,1.0]
        Assert.Equal(0.9, r[0][0].Duration, 9);                 // v 弹性吸收
        Assert.Equal(0.9, r[1][0].Start, 9);                    // g 前半起（A 域内压缩后）
        // B 域[1.0,1.1]：g 后半(w0,0.1) 独占 → [1.0,1.1]
        Assert.Equal(1.1, r[1][0].End, 9);
        Assert.True(r[1][0].Start < 1.0 && r[1][0].End > 1.0);  // g 横跨 B 头 1.0
    }

    // Sil 反例回归（VOCALOID5 hua|xiang）：引擎报的音素边界略跨 note 头时，**不吸头**——保留其真实分界。
    // 现象根因：旧「分界必卡 note 头」把略跨头的边界 snap 到头，引入 sub-frame 错位、被引擎帧量化放大成整帧 Sil。
    // 以 Preutterance 保真实分界即根除。此处：辅音 c(w0,0.05) 的 note 头(1.0)切点 = Preutterance 0.03 →
    // c 跨拍（前 0.03 / 后 0.02），c|v 边界应停在 1.02（离头 0.02），**不得**被吸到 1.0。
    [Fact]
    public void Sil_OffHeadBoundary_NotSnapped()
    {
        var r = PhonemeLayout.Resolve(new[] { Note(0.03, 1.0, 1.5, Ph("c", 0.05, 0), Ph("v", 0.2, 1)) });
        _out.WriteLine($"c={r[0][0]} v={r[0][1]}");
        Assert.Equal(0.97, r[0][0].Start, 9);                   // c 前半 0.03 落 note 头之前
        Assert.Equal(1.02, r[0][0].End, 9);                     // c|v 边界 = 1.02，保留离头 0.02、未吸到 1.0
        Assert.Equal(1.02, r[0][1].Start, 9);                   // v 接在 c 之后
        Assert.Equal(1.5, r[0][1].End, 9);                      // v 弹性填满 note 域
        Assert.True(r[0][0].End > 1.0 + 1e-6);                  // 关键：边界确实跨过 note 头、未被 snap
    }

    // 全刚性域拖边界的 roll 不变量（本 note 内两拍后刚性音素）：在被拖线两侧转移自然长（守恒二者之和）→ 被拖边界移动、
    // 域端点（起/末）固定、自然长有界（≤ 两者之和）。这是 INote.DragPinnedBoundary 全刚性分支所依赖的模型：无弹性核可吸收时
    // 改用 roll，避免「只改单侧 → 等比压缩下自然长发散」（越拖越钝、拆开相接音符露出畸长）。
    [Fact]
    public void AllRigid_RollConservesTotal_EndpointsFixed()
    {
        // a(0.9,w0) k(0.3,w0) 均拍后，域 [0,1.0] 过满（自然和 1.2）→ 等比压缩比 1/1.2
        var before = PhonemeLayout.Resolve(new[] { Note(0, 0, 1.0, Ph("a", 0.9, 0), Ph("k", 0.3, 0)) });
        _out.WriteLine($"before a={before[0][0]} k={before[0][1]}");
        Assert.Equal(0.75, before[0][0].End, 9);                // a|k 边界 = 0.9/1.2
        Assert.Equal(1.0, before[0][1].End, 9);                 // 域末固定

        // roll：a→k 转移自然长 0.3（守恒和恒为 1.2），a=0.6 k=0.6
        var after = PhonemeLayout.Resolve(new[] { Note(0, 0, 1.0, Ph("a", 0.6, 0), Ph("k", 0.6, 0)) });
        _out.WriteLine($"after a={after[0][0]} k={after[0][1]}");
        Assert.Equal(0.0, after[0][0].Start, 9);                // 域起固定
        Assert.Equal(0.5, after[0][0].End, 9);                  // a|k 边界左移到 0.6/1.2 = 0.5（线性跟随转移量）
        Assert.Equal(1.0, after[0][1].End, 9);                  // 域末仍固定，未受影响
    }
}
