using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Structures;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class AnchorLineGroup<T> : DataObject, IAnchorLineGroup where T : class, IAnchorLine, new()
{
    public IActionEvent<double, double> RangeModified => mRangeModified;

    public IReadOnlyList<IAnchorLine> AnchorLines => mAnchorLines;

    public AnchorLineGroup()
    {
        mAnchorLines.Attach(this);
    }

    public void AddLine(IReadOnlyList<AnchorPoint> points, double extension)
    {
        if (points.IsEmpty())
            return;

        double start = points[0].Pos;
        double end = points[points.Count - 1].Pos;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        var startPoints = new System.Collections.Generic.LinkedList<AnchorPoint>();
        var endPoints = new System.Collections.Generic.LinkedList<AnchorPoint>();
        int insertIndex = mAnchorLines.Count;
        int removeCount = 0;
        for (int gi = 0; gi < mAnchorLines.Count; gi++)
        {
            var anchorLine = mAnchorLines[gi];
            if (anchorLine.End < start)
                continue;

            insertIndex = Math.Min(insertIndex, gi);
            if (anchorLine.Start > end)
                break;

            removeCount++;
            if (anchorLine.Start < start)
            {
                if (start - anchorLine.Start <= extension)
                {
                    start = anchorLine.Start;
                    startPoints.AddLast(anchorLine.First());
                }
                else
                {
                    start -= extension;
                    for (int pi = 0; pi < anchorLine.Count; pi++)
                    {
                        var point = anchorLine[pi];
                        if (point.Pos >= start)
                        {
                            startPoints.AddLast(new AnchorPoint(start, anchorLine.GetValue(start)));
                            break;
                        }

                        startPoints.AddLast(point);
                    }
                }
            }

            if (anchorLine.End > end)
            {
                if (anchorLine.End - end <= extension)
                {
                    end = anchorLine.End;
                    endPoints.AddFirst(anchorLine.Last());
                }
                else
                {
                    end += extension;
                    for (int pi = anchorLine.Count - 1; pi >= 0; pi--)
                    {
                        var point = anchorLine[pi];
                        if (point.Pos <= end)
                        {
                            endPoints.AddFirst(new AnchorPoint(end, anchorLine.GetValue(end)));
                            break;
                        }

                        endPoints.AddFirst(point);
                    }
                }
            }
        }
        var newLine = new T();
        newLine.Set(startPoints.Concat(points).Concat(endPoints));
        for (int i = 0; i < removeCount; i++)
        {
            mAnchorLines.RemoveAt(insertIndex);
        }
        mAnchorLines.Insert(insertIndex, newLine);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void Clear(double start, double end)
    {
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        for (int gi = mAnchorLines.Count - 1; gi >= 0; gi--)
        {
            var anchorLine = mAnchorLines[gi];
            if (anchorLine.Start >= end)
                continue;

            if (anchorLine.End <= start)
                break;

            if (anchorLine.End <= end && anchorLine.Start >= start)
            {
                mAnchorLines.RemoveAt(gi);
                continue;
            }

            if (anchorLine.End > end)
            {
                var endPoints = new System.Collections.Generic.LinkedList<AnchorPoint>();
                for (int pi = anchorLine.Count - 1; pi >= 0; pi--)
                {
                    var point = anchorLine[pi];
                    if (point.Pos <= end)
                    {
                        endPoints.AddFirst(new AnchorPoint(end, anchorLine.GetValue(end)));
                        break;
                    }

                    endPoints.AddFirst(point);
                }

                var endLine = new T();
                endLine.Set(endPoints);
                mAnchorLines.Insert(gi + 1, endLine);
            }

            if (anchorLine.Start < start)
            {
                var newPoint = new AnchorPoint(start, anchorLine.GetValue(start));
                for (int pi = anchorLine.Count - 1; pi >= 0; pi--)
                {
                    var point = anchorLine[pi];
                    if (point.Pos >= start)
                    {
                        anchorLine.RemoveAt(pi);
                    }
                }
                anchorLine.Add(newPoint);
            }
            else
            {
                mAnchorLines.RemoveAt(gi);
            }
        }
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public List<List<Point>> GetInfo()
    {
        return mAnchorLines.GetInfo().Select(line => line.GetInfo().Select(p => p.ToPoint()).ToList()).ToList();
    }

    public double[] GetValues(IReadOnlyList<double> ticks)
    {
        double[] values = new double[ticks.Count];
        values.Fill(double.NaN);
        double start = ticks.First();
        double end = ticks.Last();
        int tickIndex = 0;
        for (int i = 0; i < mAnchorLines.Count; i++)
        {
            var anchorLine = mAnchorLines[i];
            if (anchorLine.End < start)
                continue;

            if (anchorLine.Start > end)
                break;

            while (tickIndex < ticks.Count && ticks[tickIndex] < anchorLine.Start)
            {
                tickIndex++;
            }

            int offset = tickIndex;
            while (tickIndex < ticks.Count && ticks[tickIndex] <= anchorLine.End)
            {
                tickIndex++;
            }

            double[] ts = new double[tickIndex - offset];
            for (int j = 0; j < ts.Length; j++)
            {
                ts[j] = ticks[j + offset];
            }
            anchorLine.GetValues(ts).CopyTo(values, offset);

            if (tickIndex == ticks.Count)
                break;
        }

        return values;
    }

    void IDataObject<IEnumerable<IReadOnlyCollection<Point>>>.SetInfo(IEnumerable<IReadOnlyCollection<Point>> info)
    {
        IDataObject<IEnumerable<IReadOnlyCollection<Point>>>.SetInfo(mAnchorLines, info.Where(points => points.Count > 1).Convert(points => { var t = new T(); t.Set(points.Select(point => new AnchorPoint(point))); return t; }).ToArray());
    }

    readonly DataObjectList<T> mAnchorLines = new();
    readonly ActionEvent<double, double> mRangeModified = new();
}

internal class AnchorLineGroup : AnchorLineGroup<AnchorLine> { }
