using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

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
        // RangeModified 只表"锚点几何区间变更"，不并入默认值平移（保其本义、避免 ±∞ 哨兵耦合）。
        // 默认值平移 = 整轨全区间失效，由合成失效订阅方（voice 的 VoiceSynthesisContext / effect 的 VoiceSynthesisPipeline）
        // 显式订阅 DefaultValue.Modified 处理。
        DefaultValue = new(this);
        mPoints = new(this);
        SetInfo(info);
    }

    // 相对值口径的曲线取值（锚点存的就是相对值，不叠 DefaultValue）。供构造新锚点用，见 AddLine。
    double[] GetRelativeValues(IReadOnlyList<double> ticks)
    {
        return mPoints.Convert(p => p.ToPoint()).MonotonicHermiteInterpolation(ticks);
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

    public void SetInfo(AutomationInfo info)
    {
        using var _ = MergeNotify();
        DefaultValue.SetInfo(info.DefaultValue);
        mPoints.SetInfo(info.Points.Select(p => new AnchorPoint(p)));
    }

    public void AddLine(IReadOnlyList<AnchorPoint> points, double extend)
    {
        if (points.IsEmpty())
            return;

        double start = points[0].Pos - extend;
        double end = points[points.Count - 1].Pos + extend;
        var dirty = AnchorDirtyRange.ContinuousTrack(mPoints);
        void NotifyRangeModified() => mRangeModified.Invoke(dirty.Start, dirty.End);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        dirty.Union(start, end);
        // 封边点取编辑前曲线在该处的值，用相对值口径直取：绝对值要 +DefaultValue 再由 AddPoints
        // −DefaultValue 换回来，这一趟往返足以把常数段的值抖出 1ulp，脏区的等值判定按精确值比较会因此落空。
        var y = GetRelativeValues([start, end]);
        AddPoints([new(start, y[0]), new(end, y[1])], dirty, valuesAreRelative: true);
        for (int i = mPoints.Count - 1; i >= 0; i--)
        {
            if (mPoints[i].Pos >= end)
                continue;

            if (mPoints[i].Pos <= start)
                break;

            dirty.Touch(mPoints, i);
            mPoints.RemoveAt(i);
            dirty.TouchGap(mPoints, i - 1);   // 后相位：合拢缝两侧的切线随邻居易主而变
        }
        AddPoints(points, dirty);
        dirty.CloseTails(mPoints);
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

    // valuesAreRelative：入参已是相对值（不含 DefaultValue），跳过换算——封边点等"取自当前曲线"的
    // 构造走这条，免去一次 +DefaultValue/−DefaultValue 往返（见 AddLine）。
    void AddPoints(IReadOnlyList<AnchorPoint> points, AnchorDirtyRange dirty, bool valuesAreRelative = false)
    {
        double defaultValue = valuesAreRelative ? 0 : DefaultValue;
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
                dirty.Touch(mPoints, pointIndex);   // 前相位：改写前的值与几何
                mPoints[pointIndex] = point;
                dirty.Touch(mPoints, pointIndex);
                continue;
            }

            dirty.TouchGap(mPoints, pointIndex);    // 前相位：落位缝两侧的切线将因新点插入而变
            mPoints.Insert(pointIndex + 1, point);
            dirty.Touch(mPoints, pointIndex + 1);
        }
    }

    public void InsertPoint(AnchorPoint point)
    {
        var dirty = AnchorDirtyRange.ContinuousTrack(mPoints);
        void NotifyRangeModified() => mRangeModified.Invoke(dirty.Start, dirty.End);
        Push(new UndoOnlyCommand(NotifyRangeModified));
        AddPoints([point], dirty);
        dirty.CloseTails(mPoints);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    public void DeletePoints(double start, double end)
    {
        if (start > end)
            return;

        var dirty = AnchorDirtyRange.ContinuousTrack(mPoints);
        bool hasDeletedPoint = false;
        void NotifyRangeModified() => mRangeModified.Invoke(dirty.Start, dirty.End);
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

            dirty.Touch(mPoints, i);
            mPoints.RemoveAt(i);
            dirty.TouchGap(mPoints, i - 1);   // 后相位：合拢缝
        }

        if (hasDeletedPoint)
        {
            dirty.CloseTails(mPoints);
            NotifyRangeModified();
            Push(new RedoOnlyCommand(NotifyRangeModified));
        }
    }

    public void DeletePoints(IReadOnlyList<AnchorPoint> points)
    {
        if (points.IsEmpty())
            return;

        var pointSet = points.ToHashSet();
        var dirty = AnchorDirtyRange.ContinuousTrack(mPoints);
        bool hasDeletedPoint = false;
        void NotifyRangeModified() => mRangeModified.Invoke(dirty.Start, dirty.End);
        for (int i = mPoints.Count - 1; i >= 0; i--)
        {
            if (!pointSet.Contains(mPoints[i]))
                continue;

            if (!hasDeletedPoint)
            {
                Push(new UndoOnlyCommand(NotifyRangeModified));
                hasDeletedPoint = true;
            }

            dirty.Touch(mPoints, i);
            mPoints.RemoveAt(i);
            dirty.TouchGap(mPoints, i - 1);   // 后相位：合拢缝
        }

        if (hasDeletedPoint)
        {
            dirty.CloseTails(mPoints);
            NotifyRangeModified();
            Push(new RedoOnlyCommand(NotifyRangeModified));
        }
    }

    public void MoveSelectedPoints(double offsetPos, double offsetValue)
    {
        if (offsetPos == 0 && offsetValue == 0)
            return;

        var selectedPoints = mPoints.AllSelectedItems().OrderBy(point => point.Pos).ToList();
        if (selectedPoints.IsEmpty())
            return;

        var dirty = AnchorDirtyRange.ContinuousTrack(mPoints);
        void NotifyRangeModified() => mRangeModified.Invoke(dirty.Start, dirty.End);
        Push(new UndoOnlyCommand(NotifyRangeModified));

        double defaultValue = DefaultValue;
        var selectedSet = selectedPoints.ToHashSet();
        for (int i = mPoints.Count - 1; i >= 0; i--)
        {
            if (!selectedSet.Contains(mPoints[i]))
                continue;

            dirty.Touch(mPoints, i);
            mPoints.RemoveAt(i);
            dirty.TouchGap(mPoints, i - 1);   // 后相位：合拢缝
        }
        AddPoints(selectedPoints.Select(point => new AnchorPoint(point.Pos + offsetPos, point.Value + offsetValue + defaultValue) { IsSelected = true }).ToList(), dirty);

        dirty.CloseTails(mPoints);
        NotifyRangeModified();
        Push(new RedoOnlyCommand(NotifyRangeModified));
    }

    readonly DataList<AnchorPoint> mPoints;
    readonly ActionEvent<double, double> mRangeModified = new();
    readonly IMidiPart mPart;
}
