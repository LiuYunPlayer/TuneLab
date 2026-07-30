using TuneLab.Data;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 锚点编辑脏区口径：Monotonic Hermite 下单锚点变动最远波及 [i−2, i+2] 锚点跨度，切线恒 0 的
// 邻居（极值/平台端/端点）与本锚点解耦、那一侧收窄到该邻居；连续轨的端点平延则独立由端点
// (Pos, Value) 前后比较决定，分段轨钳制组端且不收窄。AnchorDirtyRange 是所有编辑入口共用的
// 报脏 helper，这里钉死其外扩语义；再对 Automation / PiecewiseAutomation 的编辑入口做端到端
// RangeModified 校验（含"空轨画一笔曾报 ±∞ 全脏"、"移动选中锚点落入空白区曾报倒置区间"两处回归）。
public class AnchorDirtyRangeTests
{
    // 单调递增（value = i）：内部锚点都不是极值，切线全非 0，用于测不收窄的基准口径。
    static AnchorPoint[] MakeAnchors(params double[] positions)
    {
        var anchors = new AnchorPoint[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            anchors[i] = new(positions[i], i);
        return anchors;
    }

    static AnchorPoint[] MakeAnchors((double Pos, double Value)[] points)
    {
        var anchors = new AnchorPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
            anchors[i] = new(points[i].Pos, points[i].Value);
        return anchors;
    }

    [Fact]
    public void Touch_Interior_ExtendsTwoAnchorsEachSide()
    {
        var anchors = MakeAnchors(0, 100, 200, 300, 400, 500, 600);
        var dirty = AnchorDirtyRange.ContinuousTrack(anchors);
        dirty.Touch(anchors, 3);
        Assert.Equal(100, dirty.Start);
        Assert.Equal(500, dirty.End);
    }

    [Fact]
    public void Touch_NeighborIsExtremum_NarrowsToOneAnchor()
    {
        // P2/P4 是局部极值（两侧割线异号）⇒ 切线恒 0，与 P3 解耦：动 P3 不改变它们的切线，
        // 故 [P1,P2] 与 [P4,P5] 之外的曲线纹丝不动，两侧各只需外扩一个锚点。
        var anchors = MakeAnchors([(0, 0), (100, 1), (200, 5), (300, 3), (400, 5), (500, 1), (600, 0)]);
        var dirty = AnchorDirtyRange.ContinuousTrack(anchors);
        dirty.Touch(anchors, 3);
        Assert.Equal(200, dirty.Start);
        Assert.Equal(400, dirty.End);
    }

    [Fact]
    public void Touch_NeighborIsPlateauEnd_NarrowsToOneAnchor()
    {
        // 平台端（一侧割线为 0）同样被钳零切线——收窄判据是 kk <= 0，不止极值。
        var anchors = MakeAnchors([(0, 0), (100, 2), (200, 2), (300, 3), (400, 4), (500, 5)]);
        var dirty = AnchorDirtyRange.ContinuousTrack(anchors);
        dirty.Touch(anchors, 3);
        Assert.Equal(200, dirty.Start);   // P2 与 P1 等值 ⇒ 切线 0 ⇒ 左侧收窄
        Assert.Equal(500, dirty.End);     // 右侧无极值，仍外扩两个
    }

    [Fact]
    public void Touch_TwoPhases_UnionDefeatsNarrowing_WhenSlopeBecomesZero()
    {
        // 收窄要求"前后都恒 0"。这里 P4 变动前不是极值、变动后成了极值：只按变动后判会收窄到 P4，
        // 漏掉 [P4,P5]——而它的切线由非零变 0，曲线确实变了。两相位各自入账、取并即可堵住。
        var before = MakeAnchors([(0, 0), (100, 1), (200, 2), (300, 3), (400, 4), (500, 5)]);
        var after = MakeAnchors([(0, 0), (100, 1), (200, 2), (300, 9), (400, 4), (500, 5)]);

        var narrowedOnly = AnchorDirtyRange.ContinuousTrack(after);
        narrowedOnly.Touch(after, 3);
        Assert.Equal(400, narrowedOnly.End);   // 只看后相位：收窄到 P4

        var dirty = AnchorDirtyRange.ContinuousTrack(before);
        dirty.Touch(before, 3);                // 前相位：P4 尚非极值 ⇒ 不收窄
        dirty.Touch(after, 3);
        Assert.Equal(100, dirty.Start);
        Assert.Equal(500, dirty.End);
    }

    [Fact]
    public void Touch_ContinuousEndpoint_NoLongerExtendsToInfinity()
    {
        // 端点外扩不再是几何规则：Touch 只管锚点跨度（端点切线恒 0，故就近钳在端锚点），
        // 该侧平延是否失效改由 CloseTails 按端点值判定。
        var anchors = MakeAnchors(0, 100, 200, 300);
        var dirty = AnchorDirtyRange.ContinuousTrack(anchors);
        dirty.Touch(anchors, 0);
        Assert.Equal(0, dirty.Start);   // 左侧无更外的锚点，就近钳在自己身上
        Assert.Equal(200, dirty.End);   // 右邻 P1 切线非 0 ⇒ 不收窄，仍外扩两个
    }

    [Fact]
    public void CloseTails_ValueChanged_ExtendsToInfinity()
    {
        var before = MakeAnchors([(0, 0), (100, 1), (200, 2)]);
        var after = MakeAnchors([(0, 7), (100, 1), (200, 2)]);
        var dirty = AnchorDirtyRange.ContinuousTrack(before);
        dirty.Touch(after, 0);
        dirty.CloseTails(after);
        Assert.Equal(double.NegativeInfinity, dirty.Start);
        Assert.Equal(100, dirty.End);
    }

    [Fact]
    public void CloseTails_ValueUnchanged_KeepsFinite()
    {
        // 首锚点只右移、值不变：左侧平延取值处处照旧，只脏位移覆盖的那一段。
        var before = MakeAnchors([(0, 0), (100, 1), (200, 2)]);
        var after = MakeAnchors([(50, 0), (100, 1), (200, 2)]);
        var dirty = AnchorDirtyRange.ContinuousTrack(before);
        dirty.CloseTails(after);
        Assert.Equal(0, dirty.Start);
        Assert.Equal(50, dirty.End);
    }

    [Fact]
    public void Touch_ContinuousNearEndButNotEndpoint_ClampsToEndAnchor()
    {
        // 触碰次端锚点不改变端点值，平延段不失效，只钳到端锚点。
        var anchors = MakeAnchors(0, 100, 200, 300, 400);
        var dirty = AnchorDirtyRange.ContinuousTrack(anchors);
        dirty.Touch(anchors, 1);
        Assert.Equal(0, dirty.Start);
        Assert.Equal(300, dirty.End);
    }

    [Fact]
    public void Touch_Piecewise_NeverNarrows()
    {
        // 分段轨的编辑入口尚未补齐前后相位纪律，故不参与收窄——恒 ±2，宁多勿漏。
        var anchors = MakeAnchors([(0, 0), (100, 1), (200, 5), (300, 3), (400, 5), (500, 1), (600, 0)]);
        var dirty = AnchorDirtyRange.PiecewiseGroup();
        dirty.Touch(anchors, 3);
        Assert.Equal(100, dirty.Start);
        Assert.Equal(500, dirty.End);
    }

    // —— Automation（连续轨）编辑入口端到端 ——

    static Automation MakeAutomation(out List<(double Start, double End)> ranges, double defaultValue = 0)
    {
        var automation = new Automation(null!, new AutomationInfo() { DefaultValue = defaultValue });
        var collected = new List<(double, double)>();
        automation.RangeModified.Subscribe((start, end) => collected.Add((start, end)));
        ranges = collected;
        return automation;
    }

    [Fact]
    public void AutomationAddLine_OnEmptyTrack_ReportsOnlyStrokeRange()
    {
        // 回归：空轨画第一笔时，封边锚点插入瞬间既是首锚点又是末锚点，几何规则双向外扩到 ±∞，
        // 整轨被判全脏。封边点取的正是编辑前该处的曲线值，两侧平延取值根本没变，不该报无穷。
        var automation = MakeAutomation(out var ranges);

        automation.AddLine(MakeAnchors(1000, 1100, 1200), 10);

        var range = Assert.Single(ranges);
        Assert.Equal(990, range.Start);
        Assert.Equal(1210, range.End);
    }

    [Fact]
    public void Touch_NeighborIsEqualValued_NarrowsToSelf()
    {
        // 等值相邻 ⇒ 两点之间恒为常数，本点怎么动都传不进去 ⇒ 该侧一寸都不脏，边界收回本点。
        var anchors = MakeAnchors([(0, 3), (100, 3), (200, 3), (300, 8), (400, 9)]);
        var dirty = AnchorDirtyRange.ContinuousTrack(anchors);
        dirty.Touch(anchors, 2);
        Assert.Equal(200, dirty.Start);   // 左邻 P1 与本点等值 ⇒ 收回自己
        Assert.Equal(400, dirty.End);     // 右侧不等值、P3 切线非 0 ⇒ 仍外扩两个
    }

    [Fact]
    public void AutomationAddLine_BetweenEqualValuedAnchors_ReportsOnlyStrokeRange()
    {
        // 回归：轨上只有两个等值锚点（水平线），在它们之间画一小段——两侧残留的仍是同值常数段，
        // 一寸没变，脏区不该扩到那两个锚点之间的全部。
        var automation = MakeAutomation(out var ranges);
        automation.AddLine(MakeAnchors([(100, 0), (900, 0)]), 0);
        ranges.Clear();

        automation.AddLine(MakeAnchors([(400, 5), (500, 5)]), 10);

        var range = Assert.Single(ranges);
        Assert.Equal(390, range.Start);
        Assert.Equal(510, range.End);
    }

    [Fact]
    public void AutomationAddLine_BetweenEqualValuedAnchors_NonZeroConstant_ReportsOnlyStrokeRange()
    {
        // 同上，但常数非 0：封边点的值经 Hermite 插值算出（F1+F2 理论恒 1，浮点未必），
        // 等值判定是精确比较，此例守着这条往返不掉精度。
        var automation = MakeAutomation(out var ranges);
        automation.AddLine(MakeAnchors([(100, 7.5), (900, 7.5)]), 0);
        ranges.Clear();

        automation.AddLine(MakeAnchors([(400, 5), (500, 5)]), 10);

        var range = Assert.Single(ranges);
        Assert.Equal(390, range.Start);
        Assert.Equal(510, range.End);
    }

    [Fact]
    public void AutomationAddLine_BetweenEqualValuedAnchors_NonZeroDefaultValue_ReportsOnlyStrokeRange()
    {
        // 第二处浮点噪声源：DefaultValue 非 0 时，封边点的值若走 +DefaultValue 再 −DefaultValue 的
        // 往返就会抖出 1ulp。此例守着 AddLine 直取相对值这条路径。
        var automation = MakeAutomation(out var ranges, defaultValue: 3.2);
        automation.AddLine(MakeAnchors([(100, 7.5), (900, 7.5)]), 0);
        ranges.Clear();

        automation.AddLine(MakeAnchors([(400, 5), (500, 5)]), 10);

        var range = Assert.Single(ranges);
        Assert.Equal(390, range.Start);
        Assert.Equal(510, range.End);
    }

    [Fact]
    public void AutomationAddLine_BetweenDifferentValuedAnchors_StillExtends()
    {
        // 反向守卫：两锚点不等值 ⇒ 其间不是常数段，画一笔确实会改写邻段曲线，必须照常外扩。
        var automation = MakeAutomation(out var ranges);
        automation.AddLine(MakeAnchors([(100, 0), (900, 9)]), 0);
        ranges.Clear();

        automation.AddLine(MakeAnchors([(400, 5), (500, 5)]), 10);

        var range = Assert.Single(ranges);
        Assert.True(range.Start < 390, "Stroke between non-equal anchors must still dirty the neighbouring span.");
    }

    [Fact]
    public void AutomationAddLine_ChangingHeadValue_ExtendsToInfinity()
    {
        // 反向守卫：笔画确实改写了首锚点的值 ⇒ 左侧平延整段失效，必须报到 −∞。
        var automation = MakeAutomation(out var ranges);
        automation.AddLine(MakeAnchors([(1000, 1), (1100, 2), (1200, 3)]), 0);
        ranges.Clear();

        automation.AddLine(MakeAnchors([(1000, 9), (1050, 9)]), 0);

        var range = Assert.Single(ranges);
        Assert.Equal(double.NegativeInfinity, range.Start);
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
