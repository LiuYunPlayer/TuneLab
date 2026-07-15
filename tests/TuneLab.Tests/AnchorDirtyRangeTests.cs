using TuneLab.Data;
using Xunit;

namespace TuneLab.Tests;

// 锚点编辑脏区口径：Monotonic Hermite 下单锚点变动波及 [i−2, i+2] 锚点跨度，
// 连续轨端点外平延至无穷、分段轨钳制组端。AnchorDirtyRange 是所有编辑入口共用的
// 报脏 helper，这里钉死其外扩语义；再对 PiecewiseAutomation 的编辑入口做端到端
// RangeModified 校验（含"移动选中锚点落入空白区生成新组"曾报倒置区间的回归）。
public class AnchorDirtyRangeTests
{
    static AnchorPoint[] MakeAnchors(params double[] positions)
    {
        var anchors = new AnchorPoint[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            anchors[i] = new(positions[i], i);
        return anchors;
    }

    [Fact]
    public void Touch_Interior_ExtendsTwoAnchorsEachSide()
    {
        var anchors = MakeAnchors(0, 100, 200, 300, 400, 500, 600);
        var dirty = AnchorDirtyRange.ContinuousTrack();
        dirty.Touch(anchors, 3);
        Assert.Equal(100, dirty.Start);
        Assert.Equal(500, dirty.End);
    }

    [Fact]
    public void Touch_ContinuousEndpoint_ExtendsToInfinity()
    {
        // 连续轨首/末锚点之外按端点值平延，端点被触碰时该侧整段可能变值。
        var anchors = MakeAnchors(0, 100, 200, 300);
        var dirty = AnchorDirtyRange.ContinuousTrack();
        dirty.Touch(anchors, 0);
        Assert.Equal(double.NegativeInfinity, dirty.Start);
        Assert.Equal(200, dirty.End);

        dirty = AnchorDirtyRange.ContinuousTrack();
        dirty.Touch(anchors, 3);
        Assert.Equal(100, dirty.Start);
        Assert.Equal(double.PositiveInfinity, dirty.End);
    }

    [Fact]
    public void Touch_ContinuousNearEndButNotEndpoint_ClampsToEndAnchor()
    {
        // 触碰次端锚点不改变端点值，平延段不失效，只钳到端锚点。
        var anchors = MakeAnchors(0, 100, 200, 300, 400);
        var dirty = AnchorDirtyRange.ContinuousTrack();
        dirty.Touch(anchors, 1);
        Assert.Equal(0, dirty.Start);
        Assert.Equal(300, dirty.End);
    }

    [Fact]
    public void Touch_PiecewiseEndpoint_ClampsToGroupEnds()
    {
        // 分段轨组外无值，端点触碰钳制在组端。
        var anchors = MakeAnchors(0, 100, 200, 300);
        var dirty = AnchorDirtyRange.PiecewiseGroup();
        dirty.Touch(anchors, 0);
        Assert.Equal(0, dirty.Start);
        Assert.Equal(200, dirty.End);

        dirty.Touch(anchors, 3);
        Assert.Equal(0, dirty.Start);
        Assert.Equal(300, dirty.End);
    }

    [Fact]
    public void Union_Accumulates()
    {
        var dirty = AnchorDirtyRange.PiecewiseGroup();
        Assert.True(dirty.IsEmpty);
        dirty.Union(100, 200);
        dirty.Union(50, 150);
        Assert.False(dirty.IsEmpty);
        Assert.Equal(50, dirty.Start);
        Assert.Equal(200, dirty.End);
    }

    // —— PiecewiseAutomation 编辑入口端到端 ——

    static PiecewiseAutomation MakePiecewise(out List<(double Start, double End)> ranges)
    {
        var automation = new PiecewiseAutomation();
        var collected = new List<(double, double)>();
        automation.RangeModified.Subscribe((start, end) => collected.Add((start, end)));
        ranges = collected;
        return automation;
    }

    [Fact]
    public void PiecewiseInsertPoint_InGroup_ExtendsTwoAnchorsEachSide()
    {
        var automation = MakePiecewise(out var ranges);
        automation.AddLine(MakeAnchors(0, 100, 200, 300, 400), 0);
        ranges.Clear();

        automation.InsertPoint(new(250, 0));

        var range = Assert.Single(ranges);
        Assert.Equal(100, range.Start);
        Assert.Equal(400, range.End);
    }

    [Fact]
    public void PiecewiseVerticalMove_ExtendsTwoAnchorsEachSide()
    {
        // 纵向平移（offsetPos == 0）单锚点：值变同样波及邻居切线，脏区不得为零宽。
        var automation = MakePiecewise(out var ranges);
        automation.AddLine(MakeAnchors(0, 100, 200, 300, 400), 0);
        automation.AnchorGroups[0][2].IsSelected = true;
        ranges.Clear();

        automation.MoveSelectedPoints(0, 5);

        var range = Assert.Single(ranges);
        Assert.Equal(0, range.Start);
        Assert.Equal(400, range.End);
    }

    [Fact]
    public void PiecewiseMoveToEmptyArea_ReportsValidRanges()
    {
        // 回归：选中锚点移入空白区生成新组时，曾因笔误报出 (lastPos, −∞) 倒置区间，
        // 消费方相交判定必落空、目标区整段不失效。
        var automation = MakePiecewise(out var ranges);
        automation.AddLine(MakeAnchors(0, 100, 200), 0);
        automation.AddLine(MakeAnchors(1000, 1100), 0);
        foreach (var point in automation.AnchorGroups[0])
            point.IsSelected = true;
        ranges.Clear();

        automation.MoveSelectedPoints(2000, 0);

        Assert.Equal(2, ranges.Count);
        Assert.All(ranges, range => Assert.True(range.Start <= range.End, "RangeModified must not report an inverted range."));
        // 摘除相位覆盖原位置、落位相位覆盖新位置。
        Assert.Equal((0, 200), ranges[0]);
        Assert.Equal((2000, 2200), ranges[1]);
    }
}
