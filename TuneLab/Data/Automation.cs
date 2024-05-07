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
    public IReadOnlyList<Point> Points => mPoints;
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
        var values = mPoints.MonotonicHermiteInterpolation(ticks);

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
            Points = mPoints.GetInfo(),
        };
    }

    void IDataObject<AutomationInfo>.SetInfo(AutomationInfo info)
    {
        IDataObject<AutomationInfo>.SetInfo(DefaultValue, info.DefaultValue);
        IDataObject<AutomationInfo>.SetInfo(mPoints, info.Points);
    }

    public void AddLine(IReadOnlyList<Point> points, double extend)
    {
        if (points.IsEmpty())
            return;

        // FIXME: 不同插值影响的范围不一样
        double start = points[0].X - extend;
        double end = points[points.Count - 1].X + extend;
        void NotifyRangeModified() => mRangeModified.Invoke(start, end);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        var y = GetValues([start, end]);
        AddPoints([new(start, y[0]), new(end, y[1])]);
        for (int i = mPoints.Count - 1; i >= 0; i--)
        {
            if (mPoints[i].X >= end)
                continue;

            if (mPoints[i].X <= start)
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

    void AddPoints(IReadOnlyList<Point> points)
    {
        double defaultValue = DefaultValue;
        if (defaultValue != 0)
        {
            var ps = new Point[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                ps[i] = new(points[i].X, points[i].Y - defaultValue);
            }
            points = ps;
        }

        int pointIndex = mPoints.Count - 1;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            var point = points[i];
            while (pointIndex >= 0 && mPoints[pointIndex].X > point.X)
            {
                pointIndex--;
            }

            if (pointIndex >= 0 && mPoints[pointIndex].X == point.X)
            {
                mPoints[pointIndex] = point;
                continue;
            }

            mPoints.Insert(pointIndex + 1, point);
        }
    }

    void RemovePoints(IReadOnlyList<Point> points)
    {
        mPoints.Remove(points);
    }

    readonly DataList<Point> mPoints;
    readonly ActionEvent<double, double> mRangeModified = new();
    readonly IMidiPart mPart;
}
