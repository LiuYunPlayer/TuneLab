using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 分段型自动化（IPiecewiseAutomation，如 pitch）的不可变窗口快照：逐组值拷窗口内原始锚点（含无变形外扩点）。
// 段内与活对象共用同一插值纯函数，段间返回 NaN（IEEE"非数"表空，与活曲线一致）；
// 声明区间内取值与活曲线逐点一致。构造后只读、不引用任何活数据对象。
internal sealed class PiecewiseAutomationSnapshot : IAutomationEvaluator
{
    // 声明区间：仅此区间内的取值有一致性保证。
    public double Start { get; }
    public double End { get; }

    // 须在数据线程调用：从活对象按窗口物化快照。
    public static PiecewiseAutomationSnapshot Capture(IPiecewiseAutomation curve, double start, double end)
    {
        var groups = new List<Point[]>();
        foreach (var anchorGroup in curve.AnchorGroups)
        {
            if (anchorGroup.End < start)
                continue;

            if (anchorGroup.Start > end)
                break;

            var slice = AnchorWindow.Slice(anchorGroup, start, end);
            if (slice.Length > 0)
                groups.Add(slice);
        }
        return new PiecewiseAutomationSnapshot(groups.ToArray(), start, end);
    }

    public PiecewiseAutomationSnapshot(Point[][] groups, double start, double end)
    {
        mGroups = groups;
        Start = start;
        End = end;
    }

    // 查询点须升序（查询轴 = 锚点所在的 part 相对 tick 轴）。组覆盖范围按切片首末锚点判定——在声明区间内与活曲线的组边界判定等价。
    public double[] Evaluate(IReadOnlyList<double> points)
    {
        double[] values = new double[points.Count];
        values.Fill(double.NaN);

        int tickIndex = 0;
        foreach (var group in mGroups)
        {
            double groupStart = group[0].X;
            double groupEnd = group[group.Length - 1].X;

            while (tickIndex < points.Count && points[tickIndex] < groupStart)
            {
                tickIndex++;
            }

            int offset = tickIndex;
            while (tickIndex < points.Count && points[tickIndex] <= groupEnd)
            {
                tickIndex++;
            }

            if (tickIndex == offset)
                continue;

            double[] ts = new double[tickIndex - offset];
            for (int j = 0; j < ts.Length; j++)
            {
                ts[j] = points[j + offset];
            }
            group.MonotonicHermiteInterpolation(ts).CopyTo(values, offset);

            if (tickIndex == points.Count)
                break;
        }

        return values;
    }

    readonly Point[][] mGroups;
}
