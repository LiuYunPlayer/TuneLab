using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal interface IPiecewiseAutomation : IDataObject<IEnumerable<IReadOnlyCollection<Point>>>
{
    IActionEvent<double, double> RangeModified { get; }
    IReadOnlyList<IAnchorGroup> AnchorGroups { get; }
    double[] GetValues(IReadOnlyList<double> ticks);
    void AddLine(IReadOnlyList<AnchorPoint> points, double extend);
    void Clear(double start, double end);
    void InsertPoint(AnchorPoint point);
    void DeletePoints(double start, double end);
    void DeletePoints(IReadOnlyList<AnchorPoint> points);
    void DeleteAllSelectedAnchors();
    void DeleteAnchorGroupAt(int index);
    void ConnectAnchorGroup(int leftIndex);
    void MoveSelectedPoints(double offsetPos, double offsetValue);

    new List<List<Point>> GetInfo();

    IEnumerable<IReadOnlyCollection<Point>> IReadOnlyDataObject<IEnumerable<IReadOnlyCollection<Point>>>.GetInfo() => GetInfo();
}

internal static class IPiecewiseAutomationExtension
{
    public struct AreaID(int index)
    {
        public static implicit operator AreaID(int index)
        {
            return new AreaID(index);
        }

        public int Index => index;
        public int LeftIndex => -2 - index;
        public int RightIndex => -1 - index;
        public bool IsInGroup => index >= 0;
    }

    public static double GetValue(this IPiecewiseAutomation piecewiseAutomation, double x)
    {
        return piecewiseAutomation.GetValues([x])[0];
    }

    public static List<List<Point>> RangeInfo(this IPiecewiseAutomation piecewiseAutomation, double start, double end)
    {
        var result = new List<List<Point>>();
        foreach (var anchorGroup in piecewiseAutomation.AnchorGroups)
        {
            if (anchorGroup.End <= start)
                continue;

            if (anchorGroup.Start >= end)
                break;

            result.Add(anchorGroup.RangeInfo(start, end));
        }
        return result;
    }

    public static void AddLine(this IPiecewiseAutomation piecewiseAutomation, IReadOnlyList<Point> points, double extend)
    {
        piecewiseAutomation.AddLine(points.Convert(p => new AnchorPoint(p)), extend);
    }

    public static AreaID GetAreaID(this IPiecewiseAutomation piecewiseAutomation, double pos)
    {
        int i = 0;
        for (; i < piecewiseAutomation.AnchorGroups.Count; i++)
        {
            var anchorGroup = piecewiseAutomation.AnchorGroups[i];
            if (pos < anchorGroup.Start)
                return -1 - i;

            if (pos <= anchorGroup.End)
                return i;
        }

        return -1 - i;
    }

    public static List<(List<Point>, IAnchorGroup)> TakeAllSelectedAnchors(this IPiecewiseAutomation piecewiseAutomation)
    {
        var result = new List<(List<Point>, IAnchorGroup)>();
        for (int gi = piecewiseAutomation.AnchorGroups.Count - 1; gi >= 0; gi--)
        {
            var anchorGroup = piecewiseAutomation.AnchorGroups[gi];
            for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
            {
                if (anchorGroup[pi].IsSelected)
                    anchorGroup.RemoveAt(pi);
            }

            if (anchorGroup.IsEmpty())
                piecewiseAutomation.DeleteAnchorGroupAt(gi);
        }
        return result;
    }

    public static void SelectAllAnchors(this IPiecewiseAutomation piecewiseAutomation)
    {
        foreach (var anchorGroup in piecewiseAutomation.AnchorGroups)
        {
            anchorGroup.SelectAllItems();
        }
    }

    public static void DeselectAllAnchors(this IPiecewiseAutomation piecewiseAutomation)
    {
        foreach (var anchorGroup in piecewiseAutomation.AnchorGroups)
        {
            anchorGroup.DeselectAllItems();
        }
    }
}
