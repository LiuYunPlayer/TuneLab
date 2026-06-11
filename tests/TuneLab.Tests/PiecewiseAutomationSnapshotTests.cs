using TuneLab.Data;
using TuneLab.Primitives.DataStructures;
using Xunit;

namespace TuneLab.Tests;

// 分段型自动化快照 vs 活 PiecewiseCurve：窗口内逐点全等，含段间 NaN（xunit 的 double 全等
// 比较按 Equals 语义，NaN == NaN 通过）。覆盖窗口切组、压组边界锚点、纯空隙窗几种情形。
public class PiecewiseAutomationSnapshotTests
{
    static PiecewiseCurve MakeCurve()
    {
        var curve = new PiecewiseCurve();
        curve.SetInfo(
        [
            new List<Point> { new(0, 1), new(100, 2), new(150, 1.5), new(200, 1), new(300, 2.5) },
            new List<Point> { new(500, 0), new(600, 3), new(700, 2), new(800, 2.2), new(900, 1) },
        ]);
        return curve;
    }

    static double[] QueryGrid(double start, double end, double step = 13.7)
    {
        var ts = new List<double> { start };
        for (double t = start + step; t < end; t += step)
            ts.Add(t);
        ts.Add(end);
        return ts.ToArray();
    }

    [Theory]
    [InlineData(120, 850)]   // 两组都被窗口切开
    [InlineData(300, 500)]   // 边界恰压两组的边缘锚点，窗内全是空隙
    [InlineData(350, 450)]   // 窗口整个落在段间空隙：全 NaN
    [InlineData(0, 900)]     // 全覆盖
    [InlineData(-100, 1200)] // 越出曲线两端
    public void Snapshot_MatchesLiveCurve_InsideWindow(double start, double end)
    {
        var curve = MakeCurve();
        var snapshot = PiecewiseAutomationSnapshot.Capture(curve, start, end);

        var ts = QueryGrid(start, end);
        var expected = curve.GetValues(ts);
        var actual = snapshot.GetValue(ts);
        for (int i = 0; i < ts.Length; i++)
            Assert.Equal(expected[i], actual[i]);   // 全等；NaN 位置须同为 NaN
    }

    [Fact]
    public void Snapshot_GapIsNaN_AnchorsHaveValues()
    {
        var curve = MakeCurve();
        var snapshot = PiecewiseAutomationSnapshot.Capture(curve, 250, 550);

        var values = snapshot.GetValue([300, 400, 500]);
        Assert.Equal(2.5, values[0]);            // 组 1 末锚点
        Assert.True(double.IsNaN(values[1]));    // 段间空隙
        Assert.Equal(0.0, values[2]);            // 组 2 首锚点
    }
}
