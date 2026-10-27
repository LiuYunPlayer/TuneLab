using System.Linq;
using TuneLab.UI;
using Xunit;
using Panel = TuneLab.UI.ParameterPanelLayout.Panel;

namespace TuneLab.Tests;

// 参数面板堆的高度分配：记的是"想要的高度"，放不下才按比例压缩、只影响显示。
public class ParameterPanelLayoutTests
{
    const double Min = 60;

    static double[] Solve(double available, params Panel[] panels)
        => ParameterPanelLayout.Solve(available, panels, Min);

    [Fact]
    public void FitsAsWanted()
    {
        Assert.Equal([200, 100], Solve(500, new(200, false), new(100, false)));
    }

    [Fact]
    public void CollapsedPanelsTakeNoContentHeight()
    {
        Assert.Equal([200, 0], Solve(500, new(200, false), new(300, true)));
    }

    // 放不下：按想要的比例一起压，总和正好填满。
    [Fact]
    public void CompressesProportionally()
    {
        var heights = Solve(300, new(400, false), new(200, false));
        Assert.Equal(200, heights[0], 6);
        Assert.Equal(100, heights[1], 6);
    }

    // 压到下限以下的钉在下限，余量给其余面板。
    [Fact]
    public void KeepsTheMinimumAndRedistributes()
    {
        var heights = Solve(240, new(1000, false), new(100, false));
        Assert.Equal(Min, heights[1], 6);
        Assert.Equal(180, heights[0], 6);
    }

    // 想要的本就比下限矮（含拖到 0 的）：不会被抬到下限。
    [Fact]
    public void ShortPanelsAreNotRaisedToTheMinimum()
    {
        var heights = Solve(100, new(500, false), new(0, false), new(20, false));
        Assert.Equal(0, heights[1], 6);
        Assert.Equal(20, heights[2], 6);
        Assert.Equal(80, heights[0], 6);
    }

    // 连下限都放不下：下限按比例分。
    [Fact]
    public void SharesTheMinimumsWhenEvenThoseDoNotFit()
    {
        var heights = Solve(60, new(300, false), new(300, false), new(300, false));
        Assert.All(heights, h => Assert.Equal(20, h, 6));
        Assert.Equal(60, heights.Sum(), 6);
    }
}
