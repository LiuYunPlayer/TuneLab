using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Science;

namespace TuneLab.GUI;

internal class ScalableScrollAxis : IScrollAxis
{
    public event Action? AxisChanged;
    public double PivotPos { get => mPivotPos; set { _SetPivotPos(value); TryNotify(); } }
    public double Factor { get => mFactor; set { _SetFactor(value); TryNotify(); } }
    public double ContentSize { get => mContentSize; set { _SetContentSize(value); TryNotify(); } }
    public double ViewLength { get => mViewLength; set { _SetViewLength(value); TryNotify(); } }
    public double ViewOffset { get => SmallEndHideLength; set { _SetViewOffset(value); TryNotify(); } }
    public double PivotCoor { get => mPivotCoor; set => MovePivotToCoor(value); }
    public double ViewPivotRatio { get; set; } = 0;
    public double ContentLength => ContentSize * Factor;
    public double LargeEndHideLength => (ContentSize - PivotPos) * Factor - ViewLength + PivotCoor;
    public double SmallEndHideLength => PivotPos * Factor - PivotCoor;
    public double MinVisiblePos => Coor2Pos(0);
    public double MaxVisiblePos => Coor2Pos(ViewLength);

    public double Pos2Coor(double pos)
    {
        return PivotCoor + (pos - PivotPos) * Factor;
    }
    public double Coor2Pos(double coor)
    {
        return PivotPos + (coor - PivotCoor) / Factor;
    }
    public void Move(double coor) { _Move(coor); TryNotify(); }
    public void MovePivotToCoor(double coor) { _MovePivotToCoor(coor); TryNotify(); }
    public void MovePosToCoor(double pos, double coor) { _Move(Pos2Coor(pos) - coor); TryNotify(); }

    double mPivotPos = 0;
    double mFactor = 1;
    double mContentSize = 1;
    double mPivotCoor = 0;
    double mViewLength = 0;

    void _SetContentSize(double size)
    {
        if (size == ContentSize)
            return;

        mContentSize = size;
        _Move(0);
    }
    void _SetViewLength(double length)
    {
        if (ViewLength == length)
            return;

        double moveDistance = ViewPivotRatio * (ViewLength - length);
        mViewLength = length;
        mChanged = true;

        if (moveDistance != 0)
            _Move(moveDistance);

        if (LargeEndHideLength < 0)
            _Move(LargeEndHideLength);
    }
    void _Move(double pixel)
    {
        var nowSmallEndHideLength = SmallEndHideLength;
        var newSmallEndHideLength = (nowSmallEndHideLength + pixel).Limit(0, Math.Max(0, ContentLength - ViewLength));

        if (newSmallEndHideLength == nowSmallEndHideLength)
            return;

        mPivotCoor += nowSmallEndHideLength - newSmallEndHideLength;
        mChanged = true;
    }
    void _MovePivotToCoor(double coor)
    {
        _Move(PivotCoor - coor);
    }
    void _SetViewOffset(double offset)
    {
        _Move(offset - SmallEndHideLength);
    }
    void _SetPivotPos(double pos)
    {
        double coor = Pos2Coor(pos);
        mPivotPos = pos;
        mPivotCoor = coor;
    }
    void _SetFactor(double factor)
    {
        if (Factor == factor)
            return;

        mFactor = factor;
        _Move(0);
        mChanged = true;
    }

    bool mChanged = false;
    void TryNotify()
    {
        if (mChanged)
            AxisChanged?.Invoke();

        mChanged = false;
    }
}
