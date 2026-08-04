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
// shift+滚轮与触控板横滑走横向（须 allowHorizontal）、否则纵向；无可滚内容则放行冒泡让外层容器接管。
internal sealed class SmoothWheelScroller
{
    // horizontalOnly：宿主**只有横轴**可滚（如一行 tab 条）。此时普通滚轮就驱动横轴，不必按 shift。
    // 不给这个开关的话，纵轴无可滚内容会让事件直接放行——横向条就完全滚不动了。
    // 做成显式 opt-in 而非"纵轴滚不动就自动转横轴"：后者会让既有调用方（如只有横向内容的文本框）
    // 悄悄改变行为，而这里是宿主自己清楚"我就是一根横条"。
    public SmoothWheelScroller(Control host, Func<ScrollViewer?> scrollViewer, bool allowHorizontal = false, bool horizontalOnly = false)
    {
        mScrollViewer = scrollViewer;
        mAllowHorizontal = allowHorizontal;
        mHorizontalOnly = horizontalOnly;
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

        // 轴判定：横条恒走横轴；否则横向分量占优（触控板双指横滑 / 倾斜滚轮）走横轴，剩下的按 shift 走。
        // 纵向宿主收到纯横滑必须放行——旧实现在 Delta.Y==0 时拿 Delta.X 当纵向量用，横滑会把竖列表滚起来。
        bool horizontalGesture = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y);
        bool horizontal = mHorizontalOnly || horizontalGesture || ((e.KeyModifiers & KeyModifiers.Shift) != 0 && mAllowHorizontal);
        if (horizontal && !mHorizontalOnly && !mAllowHorizontal)
            return;   // 宿主没有横轴：不消费横滑，放行冒泡

        double max = horizontal
            ? Math.Max(0, sv.Extent.Width - sv.Viewport.Width)
            : Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        if (max <= 0)
            return;   // 无可滚内容：放行冒泡（外层容器可接管）

        double curBase = mAnimating && mHorizontal == horizontal
            ? mTarget
            : (horizontal ? sv.Offset.X : sv.Offset.Y);
        double delta = horizontalGesture ? e.Delta.X : (e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X);
        if (delta == 0)
            return;

        // 边界放行（嵌套滚动链）：内容虽可滚，但已在滚轮方向的端点还继续滚 → 不消费，让事件冒泡给
        // 外层容器接管，用户无需把鼠标移出内层滚动区。delta>0=向上(趋 0)、delta<0=向下(趋 max)。
        // （原先此处对任何 max>0 都无条件 Handled，导致内层滚到底后外层滚不动——见工具块嵌套问题。）
        const double edge = 0.5;
        if ((delta > 0 && curBase <= edge) || (delta < 0 && curBase >= max - edge))
            return;

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
    readonly bool mHorizontalOnly;
    readonly WheelAnimation mAnimation;
    bool mAnimating;
    bool mHorizontal;
    double mTarget;
    double mLastMs = double.NaN;

    const double WheelStep = 50;   // 每格滚轮位移（px）
    const double WheelTau = 60;    // 缓动时间常数（ms）
}
