using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using TuneLab.Animation;

namespace TuneLab.GUI.Components;

// view 层的"光标靠近边缘才显示"助手：把某控件（通常 ScrollBar）在光标靠近指定边时淡入、离开后延时淡出。
// 与被控控件彻底解耦——谁需要这种交互（钢琴窗/编排区），就在自己的 view 里 new 一个；普通文本框/列表不需要，
// 直接常驻即可。只旁听 source 的指针移动（隧道阶段、绝不消费/捕获），据 target 当前布局位置算光标到其"远边"
// （纵向=右、横向=底）的距离，动画调 target.Opacity。因只读位置、只改透明度，不碰命中/捕获，故无任何副作用。
internal sealed class EdgeProximityReveal
{
    public EdgeProximityReveal(Control target, InputElement source, Orientation orientation)
    {
        mTarget = target;
        mSource = source;
        mOrientation = orientation;
        mTarget.Opacity = 0;

        mFade = new FadeAnimation(this);
        mShowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShowDelayMs) };
        mShowTimer.Tick += OnShowTimerTick;
        mHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HideDelayMs) };
        mHideTimer.Tick += OnHideTimerTick;

        source.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        source.AddHandler(InputElement.PointerExitedEvent, OnPointerExited,
            RoutingStrategies.Direct | RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(mSource);
        var origin = mTarget.TranslatePoint(new Point(0, 0), mSource) ?? default;
        double farEdge = mOrientation == Orientation.Vertical ? origin.X + mTarget.Bounds.Width : origin.Y + mTarget.Bounds.Height;
        double cross = mOrientation == Orientation.Vertical ? p.X : p.Y;
        double dist = farEdge - cross;   // 正 = 在边内侧、越大越远离边
        SetNear(dist >= 0 && dist <= ProximityThreshold);
    }

    void OnPointerExited(object? sender, PointerEventArgs e) => SetNear(false);

    void SetNear(bool near)
    {
        if (near == mNear)
            return;

        mNear = near;
        if (near)
        {
            mHideTimer.Stop();
            if (mCurrentOpacity > 0.001)
                SetTargetOpacity(1);          // 未完全淡出：直接恢复，不再走 dwell
            else if (!mShowTimer.IsEnabled)
                mShowTimer.Start();           // 进入临界区须停留一段时间才浮现
        }
        else
        {
            mShowTimer.Stop();                // 取消待显示
            if (mCurrentOpacity > 0.001 || mTargetOpacity > 0.001)
            {
                if (!mHideTimer.IsEnabled)
                    mHideTimer.Start();       // 保持当前可见、延时后再淡出
            }
        }
    }

    void OnShowTimerTick(object? sender, EventArgs e)
    {
        mShowTimer.Stop();
        if (mNear)
            SetTargetOpacity(1);
    }

    void OnHideTimerTick(object? sender, EventArgs e)
    {
        mHideTimer.Stop();
        SetTargetOpacity(0);
    }

    void SetTargetOpacity(double target)
    {
        if (target == mTargetOpacity)
            return;

        mTargetOpacity = target;
        mLastFadeMs = double.NaN;
        AnimationManager.SharedManager.StartAnimation(mFade);
    }

    void TickFade(double millisec)
    {
        double dt = double.IsNaN(mLastFadeMs) ? FrameMs : Math.Max(0, millisec - mLastFadeMs);
        mLastFadeMs = millisec;

        double k = 1 - Math.Exp(-dt / FadeTau);
        double next = mCurrentOpacity + (mTargetOpacity - mCurrentOpacity) * k;
        if (Math.Abs(next - mTargetOpacity) < 0.01)
            next = mTargetOpacity;

        if (next != mCurrentOpacity)
        {
            mCurrentOpacity = next;
            mTarget.Opacity = next;
        }

        if (mCurrentOpacity == mTargetOpacity)
            AnimationManager.SharedManager.StopAnimation(mFade);
    }

    sealed class FadeAnimation(EdgeProximityReveal owner) : IAnimation
    {
        public void Update(double millisec) => owner.TickFade(millisec);
    }

    readonly Control mTarget;
    readonly InputElement mSource;
    readonly Orientation mOrientation;
    readonly FadeAnimation mFade;
    readonly DispatcherTimer mShowTimer;
    readonly DispatcherTimer mHideTimer;

    bool mNear;
    double mCurrentOpacity;
    double mTargetOpacity;
    double mLastFadeMs = double.NaN;

    const double ProximityThreshold = 36;   // 光标距边缘多近才浮现（px）
    const double ShowDelayMs = 150;         // 进入临界区停留多久才浮现
    const double HideDelayMs = 1500;        // 离开后延时多久才淡出
    const double FadeTau = 55;              // 淡入淡出时间常数（ms）
    const double FrameMs = 16;
}
