using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Animation;
using TuneLab.Base.Science;

namespace TuneLab.GUI;

internal class AnimationScalableScrollAxis : ScalableScrollAxis
{
    public bool IsMoveAnimating => mMoveAnimationController.IsPlaying;
    public double ScaleLevel { get => mScaleLevel; set { mScaleLevel = value.Limit(MinScaleLevel, MaxScaleLevel); Factor = ScaleLevel2Factor(ScaleLevel); } }
    public AnimationScalableScrollAxis()
    {
        mScaleAnimationController.ValueChanged += (value) => { ScaleLevel = value; };
        mMoveAnimationController.ValueChanged += (value) => { PivotCoor = value; };
    }

    public void AnimateScale(double pivot, double offset, double millisec = 150, IAnimationCurve? curve = null)
    {
        var start = mScaleAnimationController.IsPlaying ? mScaleAnimationController.Destination : ScaleLevel;

        double destination = (start + offset).Limit(MinScaleLevel, MaxScaleLevel);
        if (start == destination)
            return;

        double pivotPosition = PivotCoor;
        PivotPos = pivot;
        mMoveAnimationController.Translate(PivotCoor - pivotPosition);
        mScaleAnimationController.SetFromTo(start, destination, millisec, curve);
    }

    public void AnimateRun(double speed, double millisec = double.PositiveInfinity)
    {
        double pivotCoor = PivotCoor;
        mMoveAnimationController.Play(millisec, new AnimationPath(x => x * speed / 1000 + pivotCoor));
    }

    public void AnimateMove(double offset, double millisec = 150, IAnimationCurve? curve = null)
    {
        double start = mMoveAnimationController.IsPlaying ? mMoveAnimationController.Destination : PivotCoor;

        double destination = PivotPos * Factor - (PivotPos * Factor - (start + offset)).Limit(0, Math.Max(0, ContentLength - ViewLength));
        if (start == destination)
            return;

        mMoveAnimationController.SetFromTo(PivotCoor, destination, millisec, curve);
    }

    public void AnimateMovePosToCoor(double pos, double coor, double millisec = 150, IAnimationCurve? curve = null)
    {
        StopMoveAnimation();
        AnimateMove(coor - Pos2Coor(pos), millisec, curve);
    }

    public void StopMoveAnimation()
    {
        if (!mMoveAnimationController.IsPlaying)
            return;

        mMoveAnimationController.Stop();
    }

    protected virtual double ScaleLevel2Factor(double level)
    {
        return Math.Pow(2, level);
    }

    protected virtual double MaxScaleLevel => double.MaxValue;
    protected virtual double MinScaleLevel => double.MinValue;

    double mScaleLevel = 0;
    readonly AnimationController mScaleAnimationController = new();
    readonly AnimationController mMoveAnimationController = new();
}
