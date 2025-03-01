using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal class Automation : DataObject, IAutomation
{
    public IActionEvent<double, double> RangeModified => mRangeModified;
    public IPart Part => mPart;
    public IReadOnlyList<AnchorPoint> Points => mPoints;
    public DataStruct<double> DefaultValue { get; }

    IDataProperty<double> IAutomation.DefaultValue => DefaultValue;

    public Automation(IPart part, AutomationInfo info)
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
                ps[i] = new(points[i].Pos, points[i].Value - defaultValue);
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

    public void DeletePoints(IReadOnlyList<AnchorPoint> points)
    {
        mPoints.Remove(points);
    }

    readonly DataList<AnchorPoint> mPoints;
    readonly ActionEvent<double, double> mRangeModified = new();
    readonly IPart mPart;
}
