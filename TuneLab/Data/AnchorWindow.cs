using System;
using System.Collections.Generic;
using TuneLab.Primitives.DataStructures;

namespace TuneLab.Data;

// 锚点列的"无变形开窗"：取窗口 [start, end] 闭区间内的锚点 + 每侧开区间 (start, end) 之外
// 最近的至多两个锚点（窗口边界恰为锚点时，该锚点自身计入这两个之一），不足两个则取到曲线头/尾。
//
// 该子集是窗口内单调 Hermite 取值与全锚点列逐点一致的最小充分集：
// 查询所落段两端锚点的斜率各依赖再向外一个锚点，外扩两个恰好补齐；
// 查询恰落在锚点上时取值退化为该锚点的值、不依赖斜率，故压边界的一侧只需外扩一个。
internal static class AnchorWindow
{
    // 返回子集的闭索引区间 [First, Last]；空列表或空窗返回 First > Last。
    public static (int First, int Last) IndicesFor(IReadOnlyList<AnchorPoint> points, double start, double end)
    {
        int count = points.Count;
        if (count == 0 || start > end)
            return (0, -1);

        // k：最后一个 Pos <= start 的索引（无则 -1）
        int lo = 0, hi = count - 1, k = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].Pos <= start) { k = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        // m：第一个 Pos >= end 的索引（无则 count）
        lo = 0; hi = count - 1;
        int m = count;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].Pos >= end) { m = mid; hi = mid - 1; }
            else lo = mid + 1;
        }

        return (Math.Max(0, k - 1), Math.Min(count - 1, m + 1));
    }

    // 值拷子集为纯 Point 数组（快照用：不携带任何活对象引用）。
    public static Point[] Slice(IReadOnlyList<AnchorPoint> points, double start, double end)
    {
        var (first, last) = IndicesFor(points, start, end);
        if (first > last)
            return [];

        var result = new Point[last - first + 1];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = points[first + i].ToPoint();
        }
        return result;
    }
}
