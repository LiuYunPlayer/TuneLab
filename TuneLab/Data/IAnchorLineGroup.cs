using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Structures;

namespace TuneLab.Data;

internal interface IAnchorLineGroup : IDataObject<IEnumerable<IReadOnlyCollection<Point>>>
{
    IActionEvent<double, double> RangeModified { get; }
    IReadOnlyList<IAnchorLine> AnchorLines { get; }
    double[] GetValues(IReadOnlyList<double> ticks);
    void AddLine(IReadOnlyList<AnchorPoint> points, double extend);
    void Clear(double start, double end);
    new List<List<Point>> GetInfo();

    IEnumerable<IReadOnlyCollection<Point>> IReadOnlyDataObject<IEnumerable<IReadOnlyCollection<Point>>>.GetInfo() => GetInfo();
}

internal static class IAnchorLineGroupExtension
{
    public static double GetValue(this IAnchorLineGroup anchorLineGroup, double x)
    {
        return anchorLineGroup.GetValues([x])[0];
    }

    public static List<List<Point>> RangeInfo(this IAnchorLineGroup anchorLineGroup, double start, double end)
    {
        List<List<Point>> result = new List<List<Point>>();
        foreach (var anchorLine in anchorLineGroup.AnchorLines)
        {
            if (anchorLine.End <= start)
                continue;

            if (anchorLine.Start >= end)
                break;

            result.Add(anchorLine.RangeInfo(start, end));
        }
        return result;
    }

    public static void AddLine(this IAnchorLineGroup anchorLineGroup, IReadOnlyList<Point> points, double extend)
    {
        anchorLineGroup.AddLine(points.Convert(p => new AnchorPoint(p)), extend);
    }
}
