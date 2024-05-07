using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;

namespace TuneLab.Data;

internal interface IAnchorLine : IDataList<Point>
{
    double Start { get; }
    double End { get; }
    double[] GetValues(IReadOnlyList<double> ticks);
}

internal static class IAnchorLineExtension
{
    public static double GetValue(this IAnchorLine anchorLine, double tick)
    {
        return anchorLine.GetValues([tick])[0];
    }

    public static List<Point> RangeInfo(this IAnchorLine anchorLine, double start, double end)
    {
        List<Point> result = new List<Point>();
        if (start >= anchorLine.End || end <= anchorLine.Start)
            return result;

        if (start >= anchorLine.Start)
        {
            result.Add(new Point(0, anchorLine.GetValue(start)));
        }

        foreach (var point in anchorLine)
        {
            if (point.X <= start)
                continue;

            if (point.X >= end)
                break;

            result.Add(new(point.X - start, point.Y));
        }

        if (end <= anchorLine.End)
        {
            result.Add(new Point(end - start, anchorLine.GetValue(end)));
        }

        return result;
    }
}
