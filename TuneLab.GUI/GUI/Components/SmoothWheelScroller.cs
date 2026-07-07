using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using TuneLab.Animation;

namespace TuneLab.GUI.Components;

// 给任意由 ScrollViewer 支撑滚动的宿主挂平滑滚轮：隧道拦截原生逐格跳滚，指数缓动逼近目标偏移。
// 与滚动条视觉解耦——无论条是 AdornerLayer 浮层（OverlayScrollBars）还是树内叠放（如自造下拉弹层）都可复用。
// shift+滚轮走横向（须 allowHorizontal）、否则纵向；无可滚内容则放行冒泡让外层容器接管。
internal sealed class SmoothWheelScroller
{
    public SmoothWheelScroller(Control host, Func<ScrollViewer?> scrollViewer, bool allowHorizontal = false)
    {
        mScrollViewer = scrollViewer;
        mAllowHorizontal = allowHorizontal;
        mAnimation = new WheelAnimation(this);
        host.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var sv = mScrollViewer();
        if (sv == null)
            return;

        // 隧道相位下外层 ScrollViewer 会先于内层触发：若事件源之上还夹着更靠内的 ScrollViewer（如展开的二级菜单
        // 自己的滚动区），让位给它，避免"鼠标在子菜单里却滚了父菜单"。
        if (e.Source is Visual v)
        {
            var nearest = (v as ScrollViewer) ?? v.FindAncestorOfType<ScrollViewer>();
            if (nearest != null && !ReferenceEquals(nearest, sv))
                return;
        }

        bool horizontal = (e.KeyModifiers & KeyModifiers.Shift) != 0 && mAllowHorizontal;
        double max = horizontal
            ? Math.Max(0, sv.Extent.Width - sv.Viewport.Width)
            : Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        if (max <= 0)
            return;   // 无可滚内容：放行冒泡（外层容器可接管）

        double curBase = mAnimating && mHorizontal == horizontal
            ? mTarget
            : (horizontal ? sv.Offset.X : sv.Offset.Y);
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        mTarget = Math.Clamp(curBase - delta * WheelStep, 0, max);
        mHorizontal = horizontal;
        mLastMs = double.NaN;
        mAnimating = true;
        AnimationManager.SharedManager.StartAnimation(mAnimation);
        e.Handled = true;
    }

    void Tick(double millisec)
    {
        var sv = mScrollViewer();
        if (sv == null)
        {
            mAnimating = false;
            AnimationManager.SharedManager.StopAnimation(mAnimation);
            return;
        }

        double dt = double.IsNaN(mLastMs) ? 16 : Math.Max(0, millisec - mLastMs);
        mLastMs = millisec;

        double cur = mHorizontal ? sv.Offset.X : sv.Offset.Y;
        double k = 1 - Math.Exp(-dt / WheelTau);
        double next = cur + (mTarget - cur) * k;
        if (Math.Abs(next - mTarget) < 0.5)
            next = mTarget;

        if (next != cur)
            sv.Offset = mHorizontal ? new Vector(next, sv.Offset.Y) : new Vector(sv.Offset.X, next);

        if (next == mTarget)
        {
            mAnimating = false;
            AnimationManager.SharedManager.StopAnimation(mAnimation);
        }
    }

    sealed class WheelAnimation(SmoothWheelScroller owner) : IAnimation
    {
        public void Update(double millisec) => owner.Tick(millisec);
    }

    readonly Func<ScrollViewer?> mScrollViewer;
    readonly bool mAllowHorizontal;
    readonly WheelAnimation mAnimation;
    bool mAnimating;
    bool mHorizontal;
    double mTarget;
    double mLastMs = double.NaN;

    const double WheelStep = 50;   // 每格滚轮位移（px）
    const double WheelTau = 60;    // 缓动时间常数（ms）
}
