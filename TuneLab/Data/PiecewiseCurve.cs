using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Security;
using System.Reflection;
using System.Text;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Science;
using TuneLab.Base.Structures;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class PiecewiseCurve<T> : DataObject, IPiecewiseCurve where T : class, IAnchorGroup, new()
{
    public IActionEvent<double, double> RangeModified => mRangeModified;

    public IReadOnlyList<IAnchorGroup> AnchorGroups => mAnchorGroups;

    public PiecewiseCurve()
    {
        mAnchorGroups.Attach(this);
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
        int insertIndex = mAnchorGroups.Count;
        int removeCount = 0;
        for (int gi = 0; gi < mAnchorGroups.Count; gi++)
        {
            var anchorGroup = mAnchorGroups[gi];
            if (anchorGroup.End < start)
                continue;

            insertIndex = Math.Min(insertIndex, gi);
            if (anchorGroup.Start > end)
                break;

            removeCount++;
            if (anchorGroup.Start < start)
            {
                if (start - anchorGroup.Start <= extension)
                {
                    start = anchorGroup.Start;
                    startPoints.AddLast(anchorGroup.First());
                }
                else
                {
                    start -= extension;
                    for (int pi = 0; pi < anchorGroup.Count; pi++)
                    {
                        var point = anchorGroup[pi];
                        if (point.Pos >= start)
                        {
                            startPoints.AddLast(new AnchorPoint(start, anchorGroup.GetValue(start)));
                            break;
                        }

                        startPoints.AddLast(point);
                    }
                }
            }

            if (anchorGroup.End > end)
            {
                if (anchorGroup.End - end <= extension)
                {
                    end = anchorGroup.End;
                    endPoints.AddFirst(anchorGroup.Last());
                }
                else
                {
                    end += extension;
                    for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
                    {
                        var point = anchorGroup[pi];
                        if (point.Pos <= end)
                        {
                            endPoints.AddFirst(new AnchorPoint(end, anchorGroup.GetValue(end)));
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
            mAnchorGroups.RemoveAt(insertIndex);
        }
        mAnchorGroups.Insert(insertIndex, newLine);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void Clear(double start, double end)
    {
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        for (int gi = mAnchorGroups.Count - 1; gi >= 0; gi--)
        {
            var anchorGroup = mAnchorGroups[gi];
            if (anchorGroup.Start >= end)
                continue;

            if (anchorGroup.End <= start)
                break;

            if (anchorGroup.End <= end && anchorGroup.Start >= start)
            {
                mAnchorGroups.RemoveAt(gi);
                continue;
            }

            if (anchorGroup.End > end)
            {
                var endPoints = new System.Collections.Generic.LinkedList<AnchorPoint>();
                for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
                {
                    var point = anchorGroup[pi];
                    if (point.Pos <= end)
                    {
                        endPoints.AddFirst(new AnchorPoint(end, anchorGroup.GetValue(end)));
                        break;
                    }

                    endPoints.AddFirst(point);
                }

                var endLine = new T();
                endLine.Set(endPoints);
                mAnchorGroups.Insert(gi + 1, endLine);
            }

            if (anchorGroup.Start < start)
            {
                var newPoint = new AnchorPoint(start, anchorGroup.GetValue(start));
                for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
                {
                    var point = anchorGroup[pi];
                    if (point.Pos >= start)
                    {
                        anchorGroup.RemoveAt(pi);
                    }
                }
                anchorGroup.Add(newPoint);
            }
            else
            {
                mAnchorGroups.RemoveAt(gi);
            }
        }
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void DeleteAnchorGroupAt(int index)
    {
        var anchorGroup = mAnchorGroups[index];
        var start = anchorGroup.Start;
        var end = anchorGroup.End;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        mAnchorGroups.RemoveAt(index);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void DeletePoints(double s, double e)
    {
        for (int gi = AnchorGroups.Count - 1; gi >= 0; gi--)
        {
            var anchorGroup = AnchorGroups[gi];
            double start = anchorGroup.End;
            double end = anchorGroup.Start;
            bool hasSelectedAnchor = false;
            void NotifyRangeModified() => mRangeModified.Invoke(start, end);
            for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
            {
                if (anchorGroup[pi].Pos > e)
                    continue;

                if (anchorGroup[pi].Pos < s)
                    break;

                if (!hasSelectedAnchor)
                {
                    Push(new UndoOnlyCommand(NotifyRangeModified));
                    hasSelectedAnchor = true;
                }

                end = Math.Max(end, anchorGroup[(pi + 1).Limit(0, anchorGroup.Count - 1)].Pos);
                start = Math.Min(start, anchorGroup[(pi - 1).Limit(0, anchorGroup.Count - 1)].Pos);
                anchorGroup.RemoveAt(pi);
            }
            if (anchorGroup.IsEmpty())
                mAnchorGroups.RemoveAt(gi);

            if (hasSelectedAnchor)
            {
                NotifyRangeModified();
                Push(new RedoOnlyCommand(NotifyRangeModified));
            }
        }
    }

    public void DeleteAllSelectedAnchors()
    {
        for (int gi = AnchorGroups.Count - 1; gi >= 0; gi--)
        {
            var anchorGroup = AnchorGroups[gi];
            double start = anchorGroup.End;
            double end = anchorGroup.Start;
            bool hasSelectedAnchor = false;
            void NotifyRangeModified() => mRangeModified.Invoke(start, end);
            for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
            {
                if (anchorGroup[pi].IsSelected)
                {
                    if (!hasSelectedAnchor)
                    {
                        Push(new UndoOnlyCommand(NotifyRangeModified));
                        hasSelectedAnchor = true;
                    }

                    end = Math.Max(end, anchorGroup[(pi + 1).Limit(0, anchorGroup.Count - 1)].Pos);
                    start = Math.Min(start, anchorGroup[(pi - 1).Limit(0, anchorGroup.Count - 1)].Pos);
                    anchorGroup.RemoveAt(pi);
                }
            }
            if (anchorGroup.IsEmpty())
                mAnchorGroups.RemoveAt(gi);

            if (hasSelectedAnchor)
            {
                NotifyRangeModified();
                Push(new RedoOnlyCommand(NotifyRangeModified));
            }
        }
    }

    public void DeletePoints(IReadOnlyList<AnchorPoint> points)
    {
        if (points.IsEmpty()) 
            return;

        int flag = points.Count - 1;
        var point = points[flag];

        for (int gi = AnchorGroups.Count - 1; gi >= 0; gi--)
        {
            var anchorGroup = AnchorGroups[gi];
            double start = anchorGroup.End;
            double end = anchorGroup.Start;
            bool hasDeleteAnchor = false;
            void NotifyRangeModified() => mRangeModified.Invoke(start, end);
            for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
            {
                if (anchorGroup[pi] == point)
                {
                    if (!hasDeleteAnchor)
                    {
                        Push(new UndoOnlyCommand(NotifyRangeModified));
                        hasDeleteAnchor = true;
                    }

                    end = Math.Max(end, anchorGroup[(pi + 1).Limit(0, anchorGroup.Count - 1)].Pos);
                    start = Math.Min(start, anchorGroup[(pi - 1).Limit(0, anchorGroup.Count - 1)].Pos);
                    anchorGroup.RemoveAt(pi);

                    flag--;
                    if (flag == -1)
                        break;

                    point = points[flag];
                }
            }
            if (anchorGroup.IsEmpty())
                mAnchorGroups.RemoveAt(gi);

            if (hasDeleteAnchor)
            {
                NotifyRangeModified();
                Push(new RedoOnlyCommand(NotifyRangeModified));
            }

            if (flag == -1)
                break;
        }
    }

    public void InsertPoint(AnchorPoint point)
    {
        double start = point.Pos;
        double end = point.Pos;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        var areaID = this.GetAreaID(point.Pos);
        if (areaID.IsInGroup)
        {
            var anchorGroup = mAnchorGroups[areaID.Index];

            int pi = anchorGroup.Count - 1;
            for (; pi >= 0; pi--)
            {
                var anchor = anchorGroup[pi];
                if (anchor.Pos > point.Pos)
                    continue;

                if (anchor.Pos == point.Pos)
                    anchorGroup.RemoveAt(pi);
                else
                    pi++;

                break;
            }

            anchorGroup.Insert(pi, point);
            start = anchorGroup[(pi - 1).Limit(0, anchorGroup.Count - 1)].Pos;
            end = anchorGroup[(pi + 1).Limit(0, anchorGroup.Count - 1)].Pos;
        }
        else
        {
            var leftHasSelectedAnchor = (uint)areaID.LeftIndex < mAnchorGroups.Count && mAnchorGroups[areaID.LeftIndex].HasSelectedItem();
            var rightHasSelectedAnchor = (uint)areaID.RightIndex < mAnchorGroups.Count && mAnchorGroups[areaID.RightIndex].HasSelectedItem();
            if (leftHasSelectedAnchor)
            {
                var anchorGroup = mAnchorGroups[areaID.LeftIndex];
                anchorGroup.Add(point);
                start = anchorGroup[(anchorGroup.Count - 2).Limit(0, anchorGroup.Count - 1)].Pos;
                end = anchorGroup[(anchorGroup.Count - 1).Limit(0, anchorGroup.Count - 1)].Pos;
                if (rightHasSelectedAnchor)
                {
                    ConnectAnchorGroup(areaID.LeftIndex);
                }
            }
            else
            {
                if (rightHasSelectedAnchor)
                {
                    var anchorGroup = mAnchorGroups[areaID.RightIndex];
                    anchorGroup.Insert(0, point);
                    start = anchorGroup[0.Limit(0, anchorGroup.Count - 1)].Pos;
                    end = anchorGroup[1.Limit(0, anchorGroup.Count - 1)].Pos;
                }
                else
                {
                    var anchorGroup = new T();
                    anchorGroup.Add(point);
                    mAnchorGroups.Insert(areaID.RightIndex, anchorGroup);
                }
            }
        }

        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    void InsertPointsToGroup(T anchorGroup, IReadOnlyList<AnchorPoint> points)
    {
        if (points.IsEmpty())
            return;

        double start = anchorGroup.IsEmpty() ? double.PositiveInfinity : anchorGroup.End;
        double end = anchorGroup.IsEmpty() ? double.NegativeInfinity : anchorGroup.Start;
        bool hasDeleteAnchor = false;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);

        if (anchorGroup.IsEmpty())
        {
            start = points.ConstFirst().Pos;
            start = points.ConstLast().Pos;
            Push(new UndoOnlyCommand(NotifyRangeModified));
            anchorGroup.Set(points);
            NotifyRangeModified();
            Push(new RedoOnlyCommand(NotifyRangeModified));
            return;
        }

        int pi = anchorGroup.Count - 1;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            var anchor = points[i];
            while (pi >= 0 && anchor.Pos < anchorGroup[pi].Pos)
            {
                pi--;
            }

            void Insert(int index)
            {
                if (!hasDeleteAnchor)
                {
                    Push(new UndoOnlyCommand(NotifyRangeModified));
                    hasDeleteAnchor = true;
                }

                anchorGroup.Insert(index, anchor);
                end = Math.Max(end, anchorGroup[(index + 1).Limit(0, anchorGroup.Count - 1)].Pos);
                start = Math.Min(start, anchorGroup[(index - 1).Limit(0, anchorGroup.Count - 1)].Pos);
            }

            if (pi >= 0 && anchorGroup[pi].Pos == anchor.Pos)
            {
                anchorGroup.RemoveAt(pi);
                Insert(pi);
            }
            else
            {
                Insert(pi + 1);
            }
        }
        if (hasDeleteAnchor)
        {
            NotifyRangeModified();
            Push(new RedoOnlyCommand(NotifyRangeModified));
        }
    }

    public void ConnectAnchorGroup(int leftIndex)
    {
        if ((uint)leftIndex >= mAnchorGroups.Count - 1)
            return;

        var leftAnchorGroup = mAnchorGroups[leftIndex];
        var rightAnchorGroup = mAnchorGroups[leftIndex + 1];

        double start = leftAnchorGroup[(leftAnchorGroup.Count - 2).Limit(0, leftAnchorGroup.Count - 1)].Pos;
        double end = rightAnchorGroup[1.Limit(0, rightAnchorGroup.Count - 1)].Pos;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));

        for (int i = rightAnchorGroup.Count - 1; i >= 0; i--)
        {
            var anchor = rightAnchorGroup[0];
            rightAnchorGroup.RemoveAt(0);
            leftAnchorGroup.Add(anchor);
        }
        mAnchorGroups.RemoveAt(leftIndex + 1);

        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void MoveSelectedPoints(double offsetPos, double offsetValue)
    {
        void MoveSelectedPointsFromAnchorGroupTo(int gi, double offsetPos, double offsetValue)
        {
            double leftSide = gi == 0 ? double.NegativeInfinity : mAnchorGroups[gi - 1].End;
            double rightSide = gi == mAnchorGroups.Count - 1 ? double.PositiveInfinity : mAnchorGroups[gi + 1].Start;
            var anchorGroup = mAnchorGroups[gi];
            var selectedAnchors = new System.Collections.Generic.LinkedList<AnchorPoint>();
            double start = anchorGroup.End;
            double end = anchorGroup.Start;
            bool hasSelectedAnchor = false;
            void NotifyRangeModified() => mRangeModified.Invoke(start, end);
            for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
            {
                if (anchorGroup[pi].IsSelected)
                {
                    if (!hasSelectedAnchor)
                    {
                        Push(new UndoOnlyCommand(NotifyRangeModified));
                        hasSelectedAnchor = true;
                    }

                    end = Math.Max(end, anchorGroup[(pi + 1).Limit(0, anchorGroup.Count - 1)].Pos);
                    start = Math.Min(start, anchorGroup[(pi - 1).Limit(0, anchorGroup.Count - 1)].Pos);
                    selectedAnchors.AddFirst(anchorGroup[pi]);
                    anchorGroup.RemoveAt(pi);
                }
            }
            if (anchorGroup.IsEmpty())
                mAnchorGroups.RemoveAt(gi);

            if (hasSelectedAnchor)
            {
                NotifyRangeModified();
                Push(new RedoOnlyCommand(NotifyRangeModified));

                var selectionStart = selectedAnchors.First().Pos;
                var selectionEnd = selectedAnchors.Last().Pos;
                var movedStart = selectionStart + offsetPos;
                var movedEnd = selectionEnd + offsetPos;
                int startIndex = mAnchorGroups.Count - 1;
                int endIndex = 0;
                bool hasIntersect = false;
                bool Intersect(T ag)
                {
                    var start = ag == anchorGroup ? leftSide : ag.Start;
                    var end = ag == anchorGroup ? rightSide : ag.End;
                    return start < movedEnd && end > movedStart;
                }
                for (int i = 0; i < mAnchorGroups.Count; i++)
                {
                    if (Intersect(mAnchorGroups[i]))
                    {
                        hasIntersect = true;
                        startIndex = Math.Min(startIndex, i);
                        endIndex = Math.Max(endIndex, i);
                    }
                }
                T insertAnchorGroup = null!;
                if (hasIntersect)
                {
                    for (int i = startIndex; i < endIndex; i++)
                    {
                        ConnectAnchorGroup(startIndex);
                    }
                    insertAnchorGroup = mAnchorGroups[startIndex];
                }
                else
                {
                    int insertIndex = 0;
                    for (; insertIndex < mAnchorGroups.Count; insertIndex++)
                    {
                        if (mAnchorGroups[insertIndex].Start > movedEnd)
                            break;
                    }

                    insertAnchorGroup = new T();
                    mAnchorGroups.Insert(insertIndex, insertAnchorGroup);
                }
                InsertPointsToGroup(insertAnchorGroup, selectedAnchors.Select(point => new AnchorPoint(point.Pos + offsetPos, point.Value + offsetValue) { IsSelected = true }).ToList());
            }
        }

        if (offsetPos > 0)
        {
            var anchorGroups = mAnchorGroups.ToList();
            for (int i = anchorGroups.Count - 1; i >= 0; i--)
            {
                MoveSelectedPointsFromAnchorGroupTo(mAnchorGroups.IndexOf(anchorGroups[i]), offsetPos, offsetValue);
            }
        }
        else if (offsetPos < 0)
        {
            var anchorGroups = mAnchorGroups.ToList();
            for (int i = 0; i < anchorGroups.Count; i++)
            {
                MoveSelectedPointsFromAnchorGroupTo(mAnchorGroups.IndexOf(anchorGroups[i]), offsetPos, offsetValue);
            }
        }
        else
        {
            if (offsetValue == 0)
                return;

            foreach (var anchorGroup in mAnchorGroups)
            {
                double start = anchorGroup.End;
                double end = anchorGroup.Start;
                bool hasSelectedAnchor = false;
                void NotifyRangeModified() => mRangeModified.Invoke(start, end);
                for (int pi = anchorGroup.Count - 1; pi >= 0; pi--)
                {
                    var anchorPoint = anchorGroup[pi];
                    if (anchorPoint.IsSelected)
                    {
                        if (!hasSelectedAnchor)
                        {
                            Push(new UndoOnlyCommand(NotifyRangeModified));
                            hasSelectedAnchor = true;
                        }

                        end = Math.Max(end, anchorGroup[(pi + 1).Limit(0, anchorGroup.Count - 1)].Pos);
                        start = Math.Min(start, anchorGroup[(pi - 1).Limit(0, anchorGroup.Count - 1)].Pos);
                        anchorGroup[pi] = new AnchorPoint(anchorPoint.Pos, anchorPoint.Value + offsetValue) { IsSelected = true };
                    }
                }

                if (hasSelectedAnchor)
                {
                    NotifyRangeModified();
                    Push(new RedoOnlyCommand(NotifyRangeModified));
                }
            }
        }
    }

    public List<List<Point>> GetInfo()
    {
        return mAnchorGroups.GetInfo().Select(anchorGroup => anchorGroup.GetInfo().Select(p => p.ToPoint()).ToList()).ToList();
    }

    public double[] GetValues(IReadOnlyList<double> ticks)
    {
        double[] values = new double[ticks.Count];
        values.Fill(double.NaN);
        double start = ticks.First();
        double end = ticks.Last();
        int tickIndex = 0;
        for (int i = 0; i < mAnchorGroups.Count; i++)
        {
            var anchorGroup = mAnchorGroups[i];
            if (anchorGroup.End < start)
                continue;

            if (anchorGroup.Start > end)
                break;

            while (tickIndex < ticks.Count && ticks[tickIndex] < anchorGroup.Start)
            {
                tickIndex++;
            }

            int offset = tickIndex;
            while (tickIndex < ticks.Count && ticks[tickIndex] <= anchorGroup.End)
            {
                tickIndex++;
            }

            double[] ts = new double[tickIndex - offset];
            for (int j = 0; j < ts.Length; j++)
            {
                ts[j] = ticks[j + offset];
            }
            anchorGroup.GetValues(ts).CopyTo(values, offset);

            if (tickIndex == ticks.Count)
                break;
        }

        return values;
    }

    void IDataObject<IEnumerable<IReadOnlyCollection<Point>>>.SetInfo(IEnumerable<IReadOnlyCollection<Point>> info)
    {
        IDataObject<IEnumerable<IReadOnlyCollection<Point>>>.SetInfo(mAnchorGroups, info.Where(points => points.Count > 0).Convert(points => { var t = new T(); t.Set(points.Select(point => new AnchorPoint(point))); return t; }).ToArray());
    }

    readonly DataObjectList<T> mAnchorGroups = new();
    readonly ActionEvent<double, double> mRangeModified = new();
}

internal class PiecewiseCurve : PiecewiseCurve<AnchorGroup> { }
