using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TuneLab.Data;

/// <summary>
/// 锚点编辑的脏区累加器。曲线为 Monotonic Hermite 插值：锚点 i 的切线由 i−1/i/i+1 三点决定，
/// 故单个锚点的增/删/改会波及 [i−2, i+2] 锚点跨度内的曲线段，报脏须按此半径外扩。
/// 邻居几何是时点敏感的：删除须在移除前入账（否则旧邻居关系丢失）、插入须在落位后入账。
/// 连续轨在首/末锚点之外按端点值平延至无穷，端点被触碰时该侧平延段取值可能整体改变，
/// 保守外扩到 ±∞（宁多勿漏）；分段轨组外无值，钳制在组端。
/// </summary>
internal class AnchorDirtyRange
{
    public static AnchorDirtyRange ContinuousTrack() => new(true);
    public static AnchorDirtyRange PiecewiseGroup() => new(false);

    public double Start => mStart;
    public double End => mEnd;
    public bool IsEmpty => mStart > mEnd;

    public void Union(double start, double end)
    {
        mStart = Math.Min(mStart, start);
        mEnd = Math.Max(mEnd, end);
    }

    public void Touch(IReadOnlyList<AnchorPoint> anchors, int index)
    {
        Debug.Assert((uint)index < anchors.Count, "Touched anchor index out of range.");
        int last = anchors.Count - 1;
        double start = mInfiniteTails && index == 0 ? double.NegativeInfinity : anchors[Math.Max(index - 2, 0)].Pos;
        double end = mInfiniteTails && index == last ? double.PositiveInfinity : anchors[Math.Min(index + 2, last)].Pos;
        Union(start, end);
    }

    AnchorDirtyRange(bool infiniteTails) { mInfiniteTails = infiniteTails; }

    readonly bool mInfiniteTails;
    double mStart = double.PositiveInfinity;
    double mEnd = double.NegativeInfinity;
}
