using System;
using System.Collections.Generic;

namespace TuneLab.UI;

// 参数面板堆的高度分配（纯函数，不碰控件）。每块面板记的是"想要的高度"：放得下就各按其高，余下归钢琴窗；
// 放不下就展开着的面板按比例一起压缩，每块至少保留 minHeight（想要的本就更矮则按它自己的）；连这些都放不下，
// 就把各自的下限也按比例分。收起的面板只占标题栏（这里给 0）。
// 压缩只影响显示、不改记录——窗口变大时各面板自动长回想要的高度。
internal static class ParameterPanelLayout
{
    public readonly record struct Panel(double DesiredHeight, bool IsCollapsed);

    // available：可分给各面板内容区的总高（参数层总高减去全部标题栏）。
    public static double[] Solve(double available, IReadOnlyList<Panel> panels, double minHeight)
    {
        var result = new double[panels.Count];
        if (available <= 0)
            return result;

        var want = new double[panels.Count];
        var floor = new double[panels.Count];
        double totalWant = 0, totalFloor = 0;
        for (int i = 0; i < panels.Count; i++)
        {
            if (panels[i].IsCollapsed)
                continue;
            want[i] = Math.Max(0, panels[i].DesiredHeight);
            floor[i] = Math.Min(minHeight, want[i]);
            totalWant += want[i];
            totalFloor += floor[i];
        }

        if (totalWant <= available)
            return want;

        if (totalFloor >= available)
        {
            for (int i = 0; i < panels.Count; i++)
                result[i] = totalFloor > 0 ? floor[i] * available / totalFloor : 0;
            return result;
        }

        // 按比例压缩；压到下限以下的钉在下限，余量再按比例分给其余面板，直到没有新的面板触底。
        var pinned = new bool[panels.Count];
        while (true)
        {
            double remaining = available, freeWant = 0;
            for (int i = 0; i < panels.Count; i++)
            {
                if (pinned[i])
                    remaining -= floor[i];
                else
                    freeWant += want[i];
            }

            bool changed = false;
            for (int i = 0; i < panels.Count; i++)
            {
                if (pinned[i] || want[i] <= 0)
                    continue;
                if (want[i] * remaining / freeWant < floor[i])
                {
                    pinned[i] = true;
                    changed = true;
                }
            }
            if (changed)
                continue;

            for (int i = 0; i < panels.Count; i++)
                result[i] = pinned[i] ? floor[i] : freeWant > 0 ? want[i] * remaining / freeWant : 0;
            return result;
        }
    }
}
