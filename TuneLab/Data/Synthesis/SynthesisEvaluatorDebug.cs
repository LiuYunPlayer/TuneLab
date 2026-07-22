using System.Collections.Generic;
using System.Diagnostics;

namespace TuneLab.Data.Synthesis;

// 合成域 IAutomationEvaluator 契约的 DEBUG 守卫：把两处「静默取错值」变成开发期立即失败。
// Release 经 [Conditional("DEBUG")] 在调用点整体剔除（含实参求值）、零开销——正合 Render 热路径。
// ① 查询点须非降序：插值用只进不退的前进游标（见 MathUtility.LinearInterpolation），乱序会取到游标已越过的错值。
// ② 查询点须落在快照冻结窗口 [start,end] 内：窗外无一致性保证。典型成因是插件在 dur 模型之前 GetSnapshot、
//    尚不知辅音时长，低估了所需采样范围——契约上应保守传大窗（冻的是控制点、请求大方也便宜）。
internal static class SynthesisEvaluatorDebug
{
    [Conditional("DEBUG")]
    public static void AssertAscending(IReadOnlyList<double> points)
    {
        for (int i = 1; i < points.Count; i++)
            Debug.Assert(points[i] >= points[i - 1],
                "IAutomationEvaluator.Evaluate requires non-descending query points; unordered input silently yields wrong values (forward-cursor interpolation).");
    }

    // 须在 AssertAscending 之后调用：查询点已确认非降序，故首尾即全体极值，只比两端即可（免整列扫描）。
    [Conditional("DEBUG")]
    public static void AssertWithinWindow(IReadOnlyList<double> points, double start, double end)
    {
        if (points.Count == 0)
            return;

        const double tolerance = 1e-3;   // 只吸收秒→tick 换算的浮点边界噪声；真实的窗口低估会偏差整段辅音时长、远超此值
        Debug.Assert(points[0] >= start - tolerance && points[points.Count - 1] <= end + tolerance,
            "IAutomationEvaluator.Evaluate query point is outside the snapshot's frozen window; widen the [startTime, endTime] passed to GetSnapshot to cover every sampling point (the freeze holds control-points, so requesting generously is cheap).");
    }
}
