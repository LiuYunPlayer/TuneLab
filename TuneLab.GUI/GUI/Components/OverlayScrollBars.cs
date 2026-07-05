using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using AvaloniaEdit;
using System;
using System.Collections.Generic;
using TuneLab.Animation;

namespace TuneLab.GUI.Components;

// 给任意「由 ScrollViewer 支撑滚动」的控件挂统一浮层滚动条：经 AdornerLayer 把 ScrollBar 叠在宿主上、由宿主
// 隧道代管输入（adorner 拿不到自身指针事件），并可选平滑滚轮。支持横 / 竖。宿主可是 TextEditor / TextBox
// （模板内部有 ScrollViewer）或 ScrollViewer 本身。原生条请由调用方设为 Hidden。
//
// 适配层 Axis：把宿主的 ScrollViewer 适配成 IScrollAxis（Offset 设即滚、Viewport/Extent 为视口 / 内容），
// 变化经 ScrollViewer.ScrollChanged + 宿主 SizeChanged 通知重绘。
internal sealed class OverlayScrollBars
{
    // cursorElement：悬停手柄时强制箭头光标的元素——i-beam 文本控件传其文本区（TextEditor.TextArea）或控件本身
    // （TextBox），否则手柄上仍显 i-beam；箭头默认的控件（如 ScrollViewer）传 null。
    public OverlayScrollBars(Control host, bool horizontal, bool vertical, bool smoothWheel = true, InputElement? cursorElement = null)
    {
        mHost = host;
        mCursorElement = cursorElement;

        if (vertical)
        {
            mVerticalAxis = new Axis(this, Orientation.Vertical);
            mVertical = new ScrollBar(mVerticalAxis, Orientation.Vertical);
            mVertical.AttachInput(host);
        }
        if (horizontal)
        {
            mHorizontalAxis = new Axis(this, Orientation.Horizontal);
            mHorizontal = new ScrollBar(mHorizontalAxis, Orientation.Horizontal);
            mHorizontal.AttachInput(host);
        }

        if (smoothWheel)
        {
            mWheel = new WheelAnimation(this);
            host.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        }
        // 冒泡相位（晚于内容控件）+ 每帧强制，才能压过 AvaloniaEdit 按帧设的 i-beam。
        if (cursorElement != null)
            host.AddHandler(InputElement.PointerMovedEvent, OnHostPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);

        host.SizeChanged += (s, e) => RaiseAxisChanged();
        // AvaloniaEdit 内容 / 排版变化（打字增行、初次撑开 Extent）经 VisualLinesChanged 通知——比 ScrollViewer.
        // ScrollChanged 可靠（后者初次撑开 Extent 时不一定触发，会致首帧判「内容未超出」后再不重算、手柄不显）。
        if (host is TextEditor editor)
            editor.TextArea.TextView.VisualLinesChanged += (s, e) => RaiseAxisChanged();

        host.AttachedToVisualTree += OnAttached;
        host.DetachedFromVisualTree += OnDetached;
    }

    ScrollViewer? ScrollViewer => mScrollViewer ??= (mHost as ScrollViewer) ?? mHost.FindDescendantOfType<ScrollViewer>();

    void RaiseAxisChanged()
    {
        mVerticalAxis?.Raise();
        mHorizontalAxis?.Raise();
    }

    void UpdateAdornerVisibility()
    {
        bool visible = mHost.IsEffectivelyVisible;
        if (mVertical != null)
            mVertical.IsVisible = visible;
        if (mHorizontal != null)
            mHorizontal.IsVisible = visible;
    }

    void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var sv = ScrollViewer;
        if (sv != null && !mScrollHooked)
        {
            sv.ScrollChanged += OnScrollChanged;   // 偏移 / 内容 / 视口任一变都会触发
            mScrollHooked = true;
        }

        var layer = AdornerLayer.GetAdornerLayer(mHost);
        if (layer == null)
            return;

        AddAdorner(layer, mVertical);
        AddAdorner(layer, mHorizontal);
        HookVisibilityChain();   // 宿主不可见（如侧栏切走 tab、祖先 IsVisible=false）时滚动条跟着隐，不残留
        RaiseAxisChanged();
    }

    void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnhookVisibilityChain();
        RemoveAdorner(mVertical);
        RemoveAdorner(mHorizontal);
    }

    // 有效可见性只由「宿主→根」这条链上各元素的 IsVisible 决定；订阅整条链的 IsVisibleProperty，任一变化即重算。
    // （不用 IsEffectivelyVisible 的 EffectiveViewportChanged 信号——实测其显隐切换不可靠。）
    void HookVisibilityChain()
    {
        UnhookVisibilityChain();
        for (Visual? v = mHost; v != null; v = v.GetVisualParent())
            mVisibilitySubs.Add(v.GetObservable(Visual.IsVisibleProperty).Subscribe(new AnonymousObserver<bool>(_ => UpdateAdornerVisibility())));
    }

    void UnhookVisibilityChain()
    {
        foreach (var sub in mVisibilitySubs)
            sub.Dispose();
        mVisibilitySubs.Clear();
    }

    void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => RaiseAxisChanged();

    void AddAdorner(AdornerLayer layer, ScrollBar? bar)
    {
        if (bar == null || layer.Children.Contains(bar))
            return;

        AdornerLayer.SetAdornedElement(bar, mHost);
        layer.Children.Add(bar);
    }

    void RemoveAdorner(ScrollBar? bar)
    {
        if (bar != null)
            (bar.Parent as AdornerLayer)?.Children.Remove(bar);
    }

    // 悬停手柄强制箭头光标：adorner hit-test 关（输入由宿主代管），故光标由手柄下方元素（文本区的 i-beam）决定。
    // 进入手柄时存下原光标、并每帧设箭头（压过内容按帧设的 i-beam）；离开时还原。cursorElement 须传"指针实际
    // 悬停的最下层元素"（如 TextEditor.TextArea.TextView），设其父级无效。
    void OnHostPointerMoved(object? sender, PointerEventArgs e)
    {
        if (mCursorElement == null)
            return;

        var p = e.GetPosition(mHost);
        bool over = (mVertical?.HitTest(p) ?? false) || (mHorizontal?.HitTest(p) ?? false);
        if (over)
        {
            if (!mOverThumb)
            {
                mSavedCursor = mCursorElement.Cursor;
                mOverThumb = true;
            }
            mCursorElement.Cursor = ArrowCursor;   // 每帧强制，压过内容的 i-beam
        }
        else if (mOverThumb)
        {
            mOverThumb = false;
            mCursorElement.Cursor = mSavedCursor;
        }
    }

    // 平滑滚轮：隧道拦截原生逐行跳滚，指数缓动偏移；shift+滚轮走横向（若挂了横向条）、否则纵向。无可滚内容放行冒泡。
    void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var sv = ScrollViewer;
        if (sv == null)
            return;

        bool horizontal = (e.KeyModifiers & KeyModifiers.Shift) != 0 && mHorizontal != null;
        double max = horizontal
            ? Math.Max(0, sv.Extent.Width - sv.Viewport.Width)
            : Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        if (max <= 0)
            return;   // 无可滚内容：放行冒泡（外层容器可接管）

        double curBase = mWheelAnimating && mWheelHorizontal == horizontal
            ? mWheelTarget
            : (horizontal ? sv.Offset.X : sv.Offset.Y);
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        mWheelTarget = Math.Clamp(curBase - delta * WheelStep, 0, max);
        mWheelHorizontal = horizontal;
        mLastWheelMs = double.NaN;
        mWheelAnimating = true;
        AnimationManager.SharedManager.StartAnimation(mWheel!);
        e.Handled = true;
    }

    void TickWheel(double millisec)
    {
        var sv = ScrollViewer;
        if (sv == null)
        {
            mWheelAnimating = false;
            AnimationManager.SharedManager.StopAnimation(mWheel!);
            return;
        }

        double dt = double.IsNaN(mLastWheelMs) ? 16 : Math.Max(0, millisec - mLastWheelMs);
        mLastWheelMs = millisec;

        double cur = mWheelHorizontal ? sv.Offset.X : sv.Offset.Y;
        double k = 1 - Math.Exp(-dt / WheelTau);
        double next = cur + (mWheelTarget - cur) * k;
        if (Math.Abs(next - mWheelTarget) < 0.5)
            next = mWheelTarget;

        if (next != cur)
            sv.Offset = mWheelHorizontal ? new Vector(next, sv.Offset.Y) : new Vector(sv.Offset.X, next);

        if (next == mWheelTarget)
        {
            mWheelAnimating = false;
            AnimationManager.SharedManager.StopAnimation(mWheel!);
        }
    }

    sealed class WheelAnimation(OverlayScrollBars owner) : IAnimation
    {
        public void Update(double millisec) => owner.TickWheel(millisec);
    }

    // 宿主 ScrollViewer 的单方向 IScrollAxis 适配；变化由外层经 Raise() 通知。
    sealed class Axis(OverlayScrollBars owner, Orientation orientation) : IScrollAxis
    {
        public event Action? AxisChanged;
        public void Raise() => AxisChanged?.Invoke();

        public double ViewLength
        {
            get
            {
                var sv = owner.ScrollViewer;
                return sv == null ? 0 : (orientation == Orientation.Vertical ? sv.Viewport.Height : sv.Viewport.Width);
            }
            set { }
        }

        public double ContentLength
        {
            get
            {
                var sv = owner.ScrollViewer;
                return sv == null ? 0 : (orientation == Orientation.Vertical ? sv.Extent.Height : sv.Extent.Width);
            }
        }

        public double ViewOffset
        {
            get
            {
                var sv = owner.ScrollViewer;
                return sv == null ? 0 : (orientation == Orientation.Vertical ? sv.Offset.Y : sv.Offset.X);
            }
            set
            {
                var sv = owner.ScrollViewer;
                if (sv == null)
                    return;
                sv.Offset = orientation == Orientation.Vertical ? new Vector(sv.Offset.X, value) : new Vector(value, sv.Offset.Y);
            }
        }
    }

    sealed class AnonymousObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    readonly Control mHost;
    readonly InputElement? mCursorElement;
    readonly ScrollBar? mVertical;
    readonly ScrollBar? mHorizontal;
    readonly Axis? mVerticalAxis;
    readonly Axis? mHorizontalAxis;
    ScrollViewer? mScrollViewer;
    bool mScrollHooked;
    readonly List<IDisposable> mVisibilitySubs = new();

    bool mOverThumb;
    Cursor? mSavedCursor;

    readonly WheelAnimation? mWheel;
    bool mWheelAnimating;
    bool mWheelHorizontal;
    double mWheelTarget;
    double mLastWheelMs = double.NaN;

    static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    const double WheelStep = 50;   // 每格滚轮位移（px）
    const double WheelTau = 60;    // 缓动时间常数（ms）
}
