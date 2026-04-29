using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Base.Structures;
using TuneLab.Base.Event;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class Automation : DataObject, IAutomation
{
    public IActionEvent<double, double> RangeModified => mRangeModified;
    public IMidiPart Part => mPart;
    public IReadOnlyList<AnchorPoint> Points => mPoints;
    public DataStruct<double> DefaultValue { get; }

    IDataProperty<double> IAutomation.DefaultValue => DefaultValue;

    public Automation(MidiPart part, AutomationInfo info)
    {
        mPart = part;
        DefaultValue = new(this); //TODO: DefaultValue改动触发RangeModified
        mPoints = new(this);
        IDataObject<AutomationInfo>.SetInfo(this, info);
    }

    public double[] GetValues(IReadOnlyList<double> ticks)
    {
        var values = mPoints.Convert(p => p.ToPoint()).MonotonicHermiteInterpolation(ticks);

        double defaultValue = DefaultValue;
        if (defaultValue != 0)
        {
            for (int i = 0; i < ticks.Count; i++)
            {
                values[i] += defaultValue;
            }
        }

        return values;
    }

    public AutomationInfo GetInfo()
    {
        return new()
        {
            DefaultValue = DefaultValue,
            Points = mPoints.GetInfo().Select(p => p.ToPoint()).ToList(),
        };
    }

    void IDataObject<AutomationInfo>.SetInfo(AutomationInfo info)
    {
        IDataObject<AutomationInfo>.SetInfo(DefaultValue, info.DefaultValue);
        IDataObject<AutomationInfo>.SetInfo(mPoints, info.Points.Select(p => new AnchorPoint(p)));
    }

    public void AddLine(IReadOnlyList<AnchorPoint> points, double extend)
    {
        if (points.IsEmpty())
            return;

        // FIXME: 不同插值影响的范围不一样
        double start = points[0].Pos - extend;
        double end = points[points.Count - 1].Pos + extend;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        var y = GetValues([start, end]);
        AddPoints([new(start, y[0]), new(end, y[1])]);
        for (int i = mPoints.Count - 1; i >= 0; i--)
        {
            if (mPoints[i].Pos >= end)
                continue;

            if (mPoints[i].Pos <= start)
                break;

            mPoints.RemoveAt(i);
        }
        AddPoints(points);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void Clear(double start, double end, double extend)
    {
        if (start > end)
            return;

        double defaultValue = DefaultValue;
        if (start == end)
        {
            AddLine([new(start, defaultValue)], extend);
            return;
        }

        AddLine([new(start, defaultValue), new(end, defaultValue)], extend);
    }

    public void AddPoints(IReadOnlyList<AnchorPoint> points)
    {
        double defaultValue = DefaultValue;
        if (defaultValue != 0)
        {
            var ps = new AnchorPoint[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                ps[i] = new(points[i].Pos, points[i].Value - defaultValue) { IsSelected = points[i].IsSelected };
            }
            points = ps;
        }

        int pointIndex = mPoints.Count - 1;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            var point = points[i];
            while (pointIndex >= 0 && mPoints[pointIndex].Pos > point.Pos)
            {
                pointIndex--;
            }

            if (pointIndex >= 0 && mPoints[pointIndex].Pos == point.Pos)
            {
                mPoints[pointIndex] = point;
                continue;
            }

            mPoints.Insert(pointIndex + 1, point);
        }
    }

    public void InsertPoint(AnchorPoint point)
    {
        double start = point.Pos;
        double end = point.Pos;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));

        int insertIndex = mPoints.Count;
        for (int i = 0; i < mPoints.Count; i++)
        {
            if (mPoints[i].Pos >= point.Pos)
            {
                insertIndex = i;
                break;
            }
        }

        if (insertIndex > 0)
            start = mPoints[insertIndex - 1].Pos;
        if (insertIndex < mPoints.Count)
            end = mPoints[insertIndex].Pos;

        AddPoints([point]);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void DeletePoints(double start, double end)
    {
        if (start > end)
            return;

        double rangeStart = end;
        double rangeEnd = start;
        bool hasDeletedPoint = false;
        void NotifyRangeModified() => mRangeModified.Invoke(rangeStart, rangeEnd);
        for (int i = mPoints.Count - 1; i >= 0; i--)
        {
            if (mPoints[i].Pos > end)
                continue;

            if (mPoints[i].Pos < start)
                break;

            if (!hasDeletedPoint)
            {
                Push(new UndoOnlyCommand(NotifyRangeModified));
                hasDeletedPoint = true;
            }

            rangeStart = Math.Min(rangeStart, i > 0 ? mPoints[i - 1].Pos : mPoints[i].Pos);
            rangeEnd = Math.Max(rangeEnd, i + 1 < mPoints.Count ? mPoints[i + 1].Pos : mPoints[i].Pos);
            mPoints.RemoveAt(i);
        }

        if (hasDeletedPoint)
        {
            NotifyRangeModified();
            Push(new RedoOnlyCommand(NotifyRangeModified));
        }
    }

    public void DeletePoints(IReadOnlyList<AnchorPoint> points)
    {
        if (points.IsEmpty())
            return;

        double start = points.Min(point => point.Pos);
        double end = points.Max(point => point.Pos);
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        mPoints.Remove(points);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void MoveSelectedPoints(double offsetPos, double offsetValue)
    {
        if (offsetPos == 0 && offsetValue == 0)
            return;

        var selectedPoints = mPoints.AllSelectedItems().OrderBy(point => point.Pos).ToList();
        if (selectedPoints.IsEmpty())
            return;

        double start = Math.Min(selectedPoints.First().Pos, selectedPoints.First().Pos + offsetPos);
        double end = Math.Max(selectedPoints.Last().Pos, selectedPoints.Last().Pos + offsetPos);
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));

        double defaultValue = DefaultValue;
        mPoints.Remove(selectedPoints);
        AddPoints(selectedPoints.Select(point => new AnchorPoint(point.Pos + offsetPos, point.Value + offsetValue + defaultValue) { IsSelected = true }).ToList());

        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    readonly DataList<AnchorPoint> mPoints;
    readonly ActionEvent<double, double> mRangeModified = new();
    readonly IMidiPart mPart;
}
