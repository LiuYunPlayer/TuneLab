using System.Collections.Generic;
using TuneLab.Foundation.Science;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK;

namespace TuneLab.Data;

// 连续型自动化（IAutomation）的不可变窗口快照：值拷窗口内原始锚点（含无变形外扩点）与 DefaultValue。
// 与活对象共用同一插值纯函数（MonotonicHermiteInterpolation），声明区间内取值与活曲线逐点一致；
// 构造后只读、不引用任何活数据对象，worker 线程（将来 worker 进程）可安全持有。
internal sealed class AutomationSnapshot : IAutomationEvaluator
{
    // 声明区间：仅此区间内的取值有一致性保证（区间外的边缘段斜率信息不完整）。
    public double Start { get; }
    public double End { get; }

    // 须在数据线程调用：从活对象按窗口物化快照。
    public static AutomationSnapshot Capture(IAutomation automation, double start, double end)
    {
        return new AutomationSnapshot(AnchorWindow.Slice(automation.Points, start, end), automation.DefaultValue.Value, start, end);
    }

    public AutomationSnapshot(Point[] points, double defaultValue, double start, double end)
    {
        mPoints = points;
        mDefaultValue = defaultValue;
        Start = start;
        End = end;
    }

    // 查询点须升序（查询轴 = 锚点所在的 part 相对 tick 轴）。
    public double[] Evaluate(IReadOnlyList<double> points)
    {
        var values = mPoints.MonotonicHermiteInterpolation(points);

        if (mDefaultValue != 0)
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] += mDefaultValue;
            }
        }

        return values;
    }

    readonly Point[] mPoints;
    readonly double mDefaultValue;
}
