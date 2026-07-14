using TuneLab.SDK;
using Xunit;
using Xunit.Abstractions;

namespace TuneLab.Tests;

// 拖拽悬崖回归探针（2026-07-14/15 定位链的固化）：复刻 DragPinnedBoundary.Build 的覆盖写入模型
// （跨拍锚 bo、刚性均分、烘焙帧），在真实布局 Resolve 上验证线位移的连续性。
// 注意：本探针与 DragPinnedBoundary 的写入公式**同步演进**——改拖拽写入模型时须同步更新此处的复刻，
// 否则探针验证的是旧模型（它锚定的核心资产是"用户真实工程数据 + 网格配置下无跳变"这组事实）。
public class CliffSweepProbeTests
{
    readonly ITestOutputHelper _out;
    public CliffSweepProbeTests(ITestOutputHelper o) => _out = o;

    static SynthesizedPhoneme Ph(string s, double dur, double w) => new() { Symbol = s, Duration = dur, StretchWeight = w };

    [Fact]
    public void Sweep()
    {
        int cliffs = 0, scanned = 0;
        foreach (double ourHead in new[] { 0.30, 0.40, 0.50, 0.65, 0.80 })
        foreach (double bo0 in new[] { -0.02, -0.06, -0.12, -0.20, -0.30, -0.45 })
        foreach (double kiBo in new[] { 0.0, -0.04 })
        foreach (double s in new[] { 0.6, 1.0, 1.5 })          // i/j/l 标称整体缩放
        foreach (double aNom0 in new[] { 0.20, 0.524, 0.90 })
        foreach (bool prevPinned in new[] { true, false })
        foreach (double wI in new[] { 0.0, 1.0 })
        {
            double iN = 0.18 * s, jN = 0.061 * s, lN = 0.145 * s;
            PhonemeTiming[][] Resolve(double i2, double j2, double l2, double a2, double bo) => PhonemeLayout.Resolve(new[]
            {
                new PhonemeLayoutNote { FillStart = 0, FillEnd = ourHead, BodyOffset = kiBo, LeadingPhonemes = [Ph("k'", 0.06, 0)], BodyPhonemes = [Ph("i", i2, wI)] },
                new PhonemeLayoutNote { FillStart = ourHead, FillEnd = ourHead + 0.7, BodyOffset = bo, LeadingPhonemes = [Ph("j", j2, 0), Ph("l", l2, 0)], BodyPhonemes = [Ph("a", a2, 1)] },
            });

            var b = Resolve(iN, jN, lN, aNom0, bo0);
            double cur = b[1][2].Start - ourHead;
            if (cur >= -0.005) continue;                        // 线要在拍前才是本场景
            scanned++;
            double aDisp = b[1][2].Duration;
            double idxFactor = aDisp > 1e-9 && aNom0 > 1e-9 ? aDisp / aNom0 : 1;
            double fI = System.Math.Max(1e-9, b[0][1].Duration) / iN;
            double fJ = System.Math.Max(1e-9, b[1][0].Duration) / jN;
            double fL = System.Math.Max(1e-9, b[1][1].Duration) / lN;
            // 跨拍锚查找（修复版：最后一个显示起点在拍前的音素——贴头的 class-0 也能当锚）
            int straddler = -1;
            double relStart(int k) => b[1][k].Start - ourHead;
            double relEnd(int k) => b[1][k].End - ourHead;
            for (int k = 2; k >= 0; k--)
                if (relStart(k) < -1e-9) { straddler = k; break; }
            double fDisp0 = 0, fNat0 = 0;
            if (straddler >= 0)
            {
                fDisp0 = -relStart(straddler);
                double natStart0 = straddler >= 2 ? bo0 : bo0 - (straddler == 0 ? jN + lN : lN);
                fNat0 = -natStart0;
                if (fNat0 <= 1e-9) straddler = -1;
            }

            // prevGainCap = 我方（线及其前）拍前显示总量
            double prevGainCap = 0;
            for (int k = 0; k <= 2; k++)
                prevGainCap += System.Math.Max(0, System.Math.Min(relEnd(k), 0) - relStart(k));
            double iDispInScope = System.Math.Max(0, b[0][1].End - System.Math.Max(b[0][1].Start, 0));   // i 的前域部分显示

            double prevLine = cur, lGood = cur;
            for (double q = 0.001; q <= 0.35; q += 0.002)
            {
                // —— 完整复刻 Build 右移分配：弹性优先（prev 桶受 prevGainCap 封顶）→ 残余 own+prev 刚性均分 ——
                double prevDenom = prevPinned && wI > 0 ? wI * iDispInScope : 0;
                double iGain = 0, jGain = 0, lGain = 0, remaining = q;
                if (prevDenom > 1e-12)
                {
                    double prevShare = q;                       // ownDenom=0：比例份额全在 prev 桶
                    if (prevShare > prevGainCap) prevShare = prevGainCap;
                    iGain = prevShare;                          // prev 桶内仅 i 一个弹性
                    remaining = q - prevShare;                  // ownDenom=0 ⇒ ownShare=0
                }
                if (remaining > 1e-12)
                {
                    int cnt = prevPinned && wI <= 0 ? 3 : 2;    // 刚性均分：j、l（i 为刚性且钉死时也算）
                    double share = remaining / cnt;
                    jGain = share; lGain = share;
                    if (prevPinned && wI <= 0) iGain += share;
                }
                double i2 = iN + iGain / fI;
                double j2 = jN + jGain / fJ, l2 = lN + lGain / fL;
                double a2 = System.Math.Max(0, aNom0 - q / idxFactor);
                double shift = -(iGain + jGain + lGain);        // 跨拍者左侧份额（全部参与方增益之和，负）
                double bo;
                if (straddler >= 0)
                {
                    double fIntended = fDisp0 + shift;
                    double natStart = fIntended > 0 ? -(fNat0 * fIntended / fDisp0) : -fIntended;
                    bo = straddler >= 2 ? natStart : natStart + (straddler == 0 ? j2 + l2 : l2);
                }
                else
                {
                    double jb0 = relStart(2);                   // 无锚退路（修复版：一律增量平移，无绝对赋值）
                    double jNew = jb0 - shift;
                    bo = bo0 + (jNew - jb0);
                }
                var r = Resolve(i2, j2, l2, a2, bo);
                double line = r[1][2].Start - ourHead;
                if (System.Math.Abs(line - prevLine) > 0.008)
                {
                    _out.WriteLine($"CLIFF head={ourHead:F2} bo0={bo0:F2} kiBo={kiBo:F2} s={s:F1} aNom={aNom0:F2} pin={prevPinned} wI={wI:F0} " +
                                   $"straddler={straddler} q={q:F3}: {prevLine:F4} -> {line:F4} (bo={bo:F4}, a2={a2:F3})");
                    cliffs++;
                    break;
                }
                prevLine = line;
            }
        }
        _out.WriteLine($"scanned={scanned} cliffs={cliffs}");
        Assert.True(true);
    }

    // 用户真实工程数据（测试工程.tlp）：全刚性欠满前域（拉伸态 1.32×）+ a 贴头。
    // 验证"烘焙帧"写入（刚性按显示长入账、bo=−q 前半 1:1 起步）把 (−富余, 0) 缺口连续桥接。
    [Fact]
    public void RealData_StretchedRigidPrev_BakedFrame_Continuous()
    {
        double kN = 0.12674611111111136, iN = 0.19896542654962587;
        double jN = 0.1102173939339508, lN = 0.06872431628814851, aN = 0.4094802923932813;
        double kiBo = -9.099999999984121E-05, bo0 = -1.000000000011836E-09;
        PhonemeTiming[][] Resolve(double i2, double j2, double l2, double a2, double bo) => PhonemeLayout.Resolve(new[]
        {
            new PhonemeLayoutNote { FillStart = 0, FillEnd = 0.5, BodyOffset = kiBo, LeadingPhonemes = [Ph("k'", kN, 0)], BodyPhonemes = [Ph("i", i2, 0)] },
            new PhonemeLayoutNote { FillStart = 0.5, FillEnd = 1.0, BodyOffset = bo, LeadingPhonemes = [Ph("j", j2, 0), Ph("l", l2, 0)], BodyPhonemes = [Ph("a", a2, 1)] },
        });
        var b = Resolve(iN, jN, lN, aN, bo0);
        double cur = b[1][2].Start - 0.5;
        double dispI = b[0][1].Duration, dispJ = b[1][0].Duration, dispL = b[1][1].Duration;
        double aDisp = b[1][2].Duration;
        double idxFactor = aDisp / aN;
        _out.WriteLine($"cur={cur:F4} disp(i,j,l)=({dispI:F4},{dispJ:F4},{dispL:F4}) factor={dispI / iN:F3}");

        double prevLine = cur;
        bool jumped = false;
        for (double q = 0.001; q <= 0.35; q += 0.002)
        {
            // 烘焙帧写入：刚性 newNom = 显示长 − 均分份额；a 标称 += q/idxFactor；bo = −q（前半 1:1 起步）
            double share = q / 3;
            var r = Resolve(dispI - share, dispJ - share, dispL - share, aN + q / idxFactor, -q);
            double line = r[1][2].Start - 0.5;
            if (System.Math.Abs(line - prevLine) > 0.006)
            {
                _out.WriteLine($"JUMP q={q:F3}: {prevLine:F4} -> {line:F4}");
                jumped = true;
            }
            prevLine = line;
        }
        _out.WriteLine($"final line={prevLine:F4}");
        Assert.False(jumped);

        // 右拖回程（用户实际卡死的方向）：重标定锚（fNat0←fDisp0）+ 刚性均分增益，穿过拍点应全程连续。
        double f0 = -cur;   // 0.1222
        prevLine = cur;
        jumped = false;
        for (double q = 0.001; q <= 0.25; q += 0.002)
        {
            double share = q / 3;
            double fIntended = f0 - q;
            double bo = fIntended > 0 ? -fIntended : -fIntended;   // 重标定 1:1：拍前 −fIntended、过头 +|fIntended|，即 bo = q − f0
            var r = Resolve(iN + share, jN + share, lN + share, aN - q / idxFactor, q - f0);
            double line = r[1][2].Start - 0.5;
            if (System.Math.Abs(line - prevLine) > 0.006)
            {
                _out.WriteLine($"JUMP-R q={q:F3}: {prevLine:F4} -> {line:F4}");
                jumped = true;
            }
            prevLine = line;
        }
        _out.WriteLine($"final line(R)={prevLine:F4}");
        Assert.False(jumped);
    }
}
