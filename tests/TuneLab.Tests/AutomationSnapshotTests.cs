using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 连续型自动化快照：开窗规则（闭区间内锚点 + 每侧开区间外至多两个）取出的子集，
// 在窗口内的单调 Hermite 取值须与全锚点列逐点全等（快照与活曲线共用同一插值纯函数，
// 故唯一可能的漂移源就是开窗截断——此处把它钉死）。
public class AutomationSnapshotTests
{
    static readonly AnchorPoint[] Anchors =
        [new(0, 0), new(100, 2), new(200, 1), new(300, 4), new(400, 3),
         new(500, 3), new(600, 5), new(700, 2), new(800, 4), new(900, 1)];

    // 全锚点列直接插值 + DefaultValue —— 与 live Automation.GetValues 同一公式。
    static double[] Truth(IReadOnlyList<AnchorPoint> anchors, double defaultValue, IReadOnlyList<double> ts)
    {
        var values = anchors.Select(p => p.ToPoint()).ToList().MonotonicHermiteInterpolation(ts);
        if (defaultValue != 0)
        {
            for (int i = 0; i < values.Length; i++)
                values[i] += defaultValue;
        }
        return values;
    }

    static double[] QueryGrid(double start, double end, double step = 7.3)
    {
        var ts = new List<double> { start };
        for (double t = start + step; t < end; t += step)
            ts.Add(t);
        ts.Add(end);
        return ts.ToArray();
    }

    [Fact]
    public void IndicesFor_BoundaryInsideSegment_TakesTwoOutside()
    {
        // 250 落在段 (200,300) 内：左侧开区间外取 200、100；650 落在 (600,700) 内：右侧取 700、800。
        Assert.Equal((1, 8), AnchorWindow.IndicesFor(Anchors, 250, 650));
    }

    [Fact]
    public void IndicesFor_BoundaryOnAnchor_TakesOneMoreOutside()
    {
        // 边界恰为锚点时，该锚点自身计入"开区间外两个"之一：左侧取 200、100；右侧取 600、700。
        Assert.Equal((1, 7), AnchorWindow.IndicesFor(Anchors, 200, 600));
    }

    [Fact]
    public void IndicesFor_WindowBeyondCurveEnds_ClampsToAll()
    {
        Assert.Equal((0, 9), AnchorWindow.IndicesFor(Anchors, -500, 5000));
    }

    [Fact]
    public void IndicesFor_EmptyList_ReturnsEmptyRange()
    {
        var (first, last) = AnchorWindow.IndicesFor([], 0, 100);
        Assert.True(first > last);
    }

    [Theory]
    [InlineData(250, 650)]    // 边界在段内
    [InlineData(200, 600)]    // 边界压锚点
    [InlineData(0, 900)]      // 全覆盖、边界压首尾
    [InlineData(-500, 1500)]  // 越出曲线两端（平延区）
    [InlineData(120, 130)]    // 窄窗，段内无锚点
    [InlineData(100, 100)]    // 点窗，恰压锚点
    public void Snapshot_MatchesFullCurve_InsideWindow(double start, double end)
    {
        var snapshot = new AutomationSnapshot(AnchorWindow.Slice(Anchors, start, end), 0, start, end);
        var ts = QueryGrid(start, end);
        var expected = Truth(Anchors, 0, ts);
        var actual = snapshot.Evaluate(ts);
        for (int i = 0; i < ts.Length; i++)
            Assert.Equal(expected[i], actual[i]);   // 全等，不带容差
    }

    [Fact]
    public void Snapshot_AppliesDefaultValueOffset()
    {
        const double defaultValue = 5.5;
        var snapshot = new AutomationSnapshot(AnchorWindow.Slice(Anchors, 250, 650), defaultValue, 250, 650);
        var ts = QueryGrid(250, 650);
        var expected = Truth(Anchors, defaultValue, ts);
        var actual = snapshot.Evaluate(ts);
        for (int i = 0; i < ts.Length; i++)
            Assert.Equal(expected[i], actual[i]);
    }

    [Fact]
    public void Snapshot_FewAnchors()
    {
        // 0 / 1 / 2 个锚点的退化路径（插值函数内部回退线性/常值）。
        foreach (var anchors in new[] { System.Array.Empty<AnchorPoint>(), [new AnchorPoint(50, 3)], [new AnchorPoint(50, 3), new AnchorPoint(150, 7)] })
        {
            var snapshot = new AutomationSnapshot(AnchorWindow.Slice(anchors, 0, 200), 0, 0, 200);
            var ts = QueryGrid(0, 200);
            var expected = Truth(anchors, 0, ts);
            var actual = snapshot.Evaluate(ts);
            for (int i = 0; i < ts.Length; i++)
                Assert.Equal(expected[i], actual[i]);
        }
    }

    [Fact]
    public void MarginOfOne_IsInsufficient()
    {
        // 反向论证开窗规则的"两个"下限：只外扩一个锚点时，边缘子段（窗口边界到首个窗内锚点之间）
        // 的左端斜率因缺更外一个锚点被算成 0，取值偏离全曲线。用单调递增 y 保证该斜率非零。
        AnchorPoint[] ascending =
            [new(0, 0), new(100, 1), new(200, 2), new(300, 4), new(400, 5),
             new(500, 5.5), new(600, 6), new(700, 6.5), new(800, 7), new(900, 8)];

        // 窗口 (250, 650)：正确子集是 [100..800]；只外扩一个 = [200..700]。
        var oneMargin = ascending.Skip(2).Take(6).Select(p => p.ToPoint()).ToArray();
        var snapshot = new AutomationSnapshot(oneMargin, 0, 250, 650);

        var ts = QueryGrid(250, 650);
        var expected = Truth(ascending, 0, ts);
        var actual = snapshot.Evaluate(ts);
        bool anyDiff = false;
        for (int i = 0; i < ts.Length; i++)
            anyDiff |= expected[i] != actual[i];
        Assert.True(anyDiff);
    }
}
