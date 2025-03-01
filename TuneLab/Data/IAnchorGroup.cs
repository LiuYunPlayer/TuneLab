using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface IAnchorGroup : IDataList<AnchorPoint>
{
    double Start { get; }
    double End { get; }
    double[] GetValues(IReadOnlyList<double> ticks);
}

internal static class IAnchorGroupExtension
{
    public static double GetValue(this IAnchorGroup anchorGroup, double tick)
    {
        return anchorGroup.GetValues([tick])[0];
    }

    public static List<Point> RangeInfo(this IAnchorGroup anchorGroup, double start, double end)
    {
        List<Point> result = new List<Point>();
        if (start >= anchorGroup.End || end <= anchorGroup.Start)
            return result;

        if (start >= anchorGroup.Start)
        {
            result.Add(new Point(0, anchorGroup.GetValue(start)));
        }

        foreach (var point in anchorGroup)
        {
            if (point.Pos <= start)
                continue;

            if (point.Pos >= end)
                break;

            result.Add(new(point.Pos - start, point.Value));
        }

        if (end <= anchorGroup.End)
        {
            result.Add(new Point(end - start, anchorGroup.GetValue(end)));
        }

        return result;
    }
}
