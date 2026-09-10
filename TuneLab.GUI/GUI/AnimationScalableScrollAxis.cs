using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Animation;
using TuneLab.Foundation;

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

    // 把 [minPos, maxPos] 整段挪进视野并居中：**放不下才缩小到刚好容纳（留一点边距），永不替用户放大**。
    //
    // 【为什么这条不算"把视口捞回来"】被否掉的是以鼠标为轴心的连续缩放手势（外部没有鼠标），
    // 而这里的缩放只是"让它进视野"的后果——目标本来就装得下时一格都不动。放大则从不做：
    // 把用户的缩放猛拉到一个音符上，反而让他找不着北。
// 返回**挪完之后**视野里的范围（见函数末尾：动画期间读 MinVisiblePos 读到的是挪之前的）。
    public (double Min, double Max) AnimateReveal(double minPos, double maxPos, double marginRatio = 0.08, double millisec = 200)
    {
        if (ViewLength <= 0)
            return (MinVisiblePos, MaxVisiblePos);   // 视图还没布局出来，此刻没有"视野"可言

        if (maxPos < minPos)
            (minPos, maxPos) = (maxPos, minPos);

        double span = (maxPos - minPos) * (1 + 2 * marginRatio);
        if (span > 0 && span * Factor > ViewLength)
            ScaleLevel = ScaleLevelAtMost(ViewLength / span);   // setter 自带上下限钳制：连最小档都装不下时就停在最小档

        AnimateMovePosToCoor((minPos + maxPos) / 2, ViewLength / 2, millisec);

        // 【为什么不直接读 MinVisiblePos】上一句是**动画**，此刻轴还停在原处，读到的会是挪之前的视野——
        // 回报里说"现在看得见什么"却报出挪之前的范围，是一句会误导调用方的话。故这里按落点算：
        // 缩放在上面已经落定（那一句不是动画），剩下的只是平移，而平移的钳制与 _Move 里那句同源
        //（小端隐藏长度限在 [0, 内容长度 - 视野长度]），故两处不会分叉。
        double visibleSize = ViewLength / Factor;
        double min = ((minPos + maxPos) / 2 - visibleSize / 2).Limit(0, Math.Max(0, ContentSize - visibleSize));
        return (min, min + visibleSize);
    }

    // 求"factor 不超过给定值的最大档位"。二分而不是解析求逆：ScaleLevel2Factor 是 virtual 的，
    // 各轴各有自己的指数式，逐个抄一遍反函数就是逐个多一处会算错的地方。它在各轴上都单调增
    //（档位越高越放大），二分因此成立。
    double ScaleLevelAtMost(double factor)
    {
        // 基类的档位上下限默认是 ±double.MaxValue（"没有限制"），二分不能在那上面取中点。
        // 未声明限制的轴退到一个够宽的窗口：默认换算是 2^level，±64 档已覆盖任何可能的 factor。
        double low = Math.Max(MinScaleLevel, -64), high = Math.Min(MaxScaleLevel, 64);
        if (ScaleLevel2Factor(low) >= factor)
            return low;

        for (int i = 0; i < 60; i++)
        {
            double mid = (low + high) / 2;
            if (ScaleLevel2Factor(mid) <= factor)
                low = mid;
            else
                high = mid;
        }
        return low;
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
