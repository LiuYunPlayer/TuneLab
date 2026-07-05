using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using System;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 通用滚动条：绑一个 IScrollAxis、扔进布局即可用的纯控件。职责只有三件——画手柄、随轴重绘、处理手柄拖动。
// 命中测试只覆盖手柄（ICustomHitTest）：即便铺满整块区域做覆盖层，也只有手柄拦截指针、其余穿透，不抢内容
// 事件。整体显隐（如"光标靠近才显示"）不是本控件的职责，由所在 view 控制 Opacity/IsVisible（见 EdgeProximityReveal）。
//
// 手柄模型：手柄不是被拖动的实体，只是绘制层产物。拖动时把指针位移换算成视图应移动量、直接改
// IScrollAxis.ViewOffset（数据本体）；轴变化触发重绘，重绘按当前视野算手柄位置并钳回视野内。
// 无界轴（设了 ContentExtentProvider，如时间轴）据此天然支持拖出内容外无限后滚：手柄钳在边缘、offset 继续
// 增大，拖动时锁光标于锚点 warp 累积位移、不受屏幕边约束。有界轴按内容比例缩放、拖动不隐藏光标（手柄与
// 光标 1:1 同步、本就贴着手柄）。
internal sealed class ScrollBar : Control, ICustomHitTest
{
    // 无界轴用：返回内容末尾的像素长度作 thumb 尺寸口径，并据此判定为无界（拖动隐藏光标 + warp 无限拖）。
    // null（有界轴）直接用 axis.ContentLength、走普通位置位移拖动。
    public Func<double>? ContentExtentProvider { get; set; }

    public ScrollBar(IScrollAxis axis, Orientation orientation)
    {
        mAxis = axis;
        mOrientation = orientation;
        mAxis.AxisChanged += OnAxisChanged;
    }

    ~ScrollBar()
    {
        mAxis.AxisChanged -= OnAxisChanged;
        DetachInput();
    }

    // 可选：从宿主接管指针输入，用于本控件自己收不到事件的场景——最典型是挂在 AdornerLayer 上叠在第三方
    // 控件（AvaloniaEdit）之上时，adorner 拿不到自身指针事件。挂上后本控件 hit-test 关闭（纯绘制），改由
    // 宿主隧道阶段驱动拖动。正常放在视觉树里（如钢琴窗/编排区的 LayerPanel）则不必调用本方法，走自身事件 +
    // ICustomHitTest（只手柄可命中、不抢内容）。
    public void AttachInput(InputElement source)
    {
        if (ReferenceEquals(mSource, source))
            return;

        DetachInput();
        mSource = source;
        IsHitTestVisible = false;   // 输入改由宿主旁听，本体不参与命中
        source.AddHandler(PointerMovedEvent, OnSourcePointerMoved, RoutingStrategies.Tunnel);
        source.AddHandler(PointerPressedEvent, OnSourcePointerPressed, RoutingStrategies.Tunnel);
        source.AddHandler(PointerReleasedEvent, OnSourcePointerReleased, RoutingStrategies.Tunnel);
        source.AddHandler(PointerCaptureLostEvent, OnSourceCaptureLost,
            RoutingStrategies.Direct | RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public void DetachInput()
    {
        if (mSource == null)
            return;

        mSource.RemoveHandler(PointerMovedEvent, OnSourcePointerMoved);
        mSource.RemoveHandler(PointerPressedEvent, OnSourcePointerPressed);
        mSource.RemoveHandler(PointerReleasedEvent, OnSourcePointerReleased);
        mSource.RemoveHandler(PointerCaptureLostEvent, OnSourceCaptureLost);
        mSource = null;
        IsHitTestVisible = true;
    }

    // 捕获目标：宿主旁听模式捕获宿主（本体 hit-test 关），否则捕获本体。
    InputElement CaptureTarget => mSource ?? this;

    void OnAxisChanged() => InvalidateVisual();

    // 只有手柄可命中：其余区域穿透到底下内容（即便本控件铺满整区）。
    // 须显式挡掉"已淡出"的情形——Avalonia 连 Opacity=0 的控件也照样命中测试（透明≠不可点），
    // 否则淡没了的滚动条其手柄区仍会吃掉点击。
    public bool HitTest(Point point) => Opacity > 0 && IsOverThumb(point);

    // 作普通控件（如 DockPanel 保留道）时给一个厚度；作覆盖层时父级（LayerPanel）会强制 finalSize、此值不生效。
    protected override Size MeasureOverride(Size availableSize)
    {
        double thickness = EdgeMargin * 2 + ThumbThickness;
        return mOrientation == Orientation.Vertical ? new Size(thickness, 0) : new Size(0, thickness);
    }

    public override void Render(DrawingContext context)
    {
        if (!TryGetThumb(out double pos, out double len))
            return;

        double thickness = ThumbThickness;
        double radius = thickness / 2;
        double edge = EdgeLine;
        var rect = mOrientation == Orientation.Vertical
            ? new Rect(edge - EdgeMargin - thickness, pos, thickness, len)
            : new Rect(pos, edge - EdgeMargin - thickness, len, thickness);

        // 暗淡半透明柔灰（整体显隐由外部 Opacity 叠加）。
        context.DrawRectangle(Style.LIGHT_WHITE.Opacity(ThumbOpacity).ToBrush(), null, rect, radius, radius);
    }

    double TrackLength => mOrientation == Orientation.Vertical ? Bounds.Height : Bounds.Width;

    double BaseContentLength => ContentExtentProvider?.Invoke() ?? mAxis.ContentLength;

    // 手柄贴的那条边的坐标（纵向=x=右、横向=y=底）：即控件自身跨轴尺寸。落位（如"波形上方"）由布局用 Margin
    // 决定 Bounds，本控件只在自己边缘绘制。
    double EdgeLine => mOrientation == Orientation.Vertical ? Bounds.Width : Bounds.Height;

    // 按当前轴状态算手柄（沿滚动方向的）位置与长度。返回 false 表示无需显示（内容全在视野内）。
    // 尺寸恒按"基准内容长度"算（不随 offset 变），故拖过内容末尾时手柄长度不变、只是位置被钳在边缘。
    bool TryGetThumb(out double pos, out double len)
    {
        pos = 0;
        len = 0;

        double track = TrackLength;
        double view = mAxis.ViewLength;
        double content = BaseContentLength;
        if (track <= 0 || view <= 0 || content <= 0 || view >= content)
            return false;

        len = Math.Min(view / content * track, track);   // 完全按 视野/内容 比例，不钳最小值
        pos = Math.Clamp(mAxis.ViewOffset / content * track, 0, track - len);
        return true;
    }

    bool IsOverThumb(Point p)
    {
        if (!TryGetThumb(out double pos, out double len))
            return false;

        double along = mOrientation == Orientation.Vertical ? p.Y : p.X;
        double cross = EdgeLine - (mOrientation == Orientation.Vertical ? p.X : p.Y);
        double grab = EdgeMargin + ThumbThickness + GrabPadding;
        return cross >= 0 && cross <= grab && along >= pos - GrabPadding && along <= pos + len + GrabPadding;
    }

    // ——拖动——
    // 两条输入路径（自身事件 / 宿主旁听）共用同一套逻辑：按下取"手柄坐标系里的点"、命中手柄即起拖。
    // 坐标一律相对 CaptureTarget（自身或宿主，两者与本控件铺同一区域），与渲染用的 Bounds 对齐。

    // 自身事件（正常放视觉树时；挂了宿主则 hit-test 关、这些不触发）
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (mSource == null)
            HandlePress(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (mSource == null)
            HandleMove(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (mSource == null && mDragging)
        {
            EndDrag(e.Pointer);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (mSource == null && mDragging)
            EndDrag(null);
    }

    // 宿主旁听（adorner 等自己收不到事件时；隧道阶段抢在内容前，只在命中手柄时拦截）
    void OnSourcePointerPressed(object? sender, PointerPressedEventArgs e) => HandlePress(e);
    void OnSourcePointerMoved(object? sender, PointerEventArgs e) => HandleMove(e);
    void OnSourcePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!mDragging)
            return;
        EndDrag(e.Pointer);
        e.Handled = true;
    }

    void OnSourceCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!mDragging)
            return;
        // 内容控件（如 AvaloniaEdit 的 TextArea）在按下时抢走了捕获：重夺即可继续拖；重夺不成才结束。
        if (mSource != null)
        {
            e.Pointer.Capture(mSource);
            if (ReferenceEquals(e.Pointer.Captured, mSource))
                return;
        }
        EndDrag(null);
    }

    void HandlePress(PointerPressedEventArgs e)
    {
        var target = CaptureTarget;
        if (!e.GetCurrentPoint(target).Properties.IsLeftButtonPressed)
            return;

        var p = e.GetPosition(target);
        if (!IsOverThumb(p))
            return;

        double track = TrackLength;
        double content = BaseContentLength;
        if (track <= 0 || content <= 0)
            return;

        mDragging = true;
        mDragRatio = content / track;   // 固定于按下瞬间 → 拖动线性；offset 可超基准内容，轴自钳
        TryGetThumb(out mDragThumbStartPos, out _);   // 记按下时手柄位置，供松手时让光标随手柄位移重现
        e.Pointer.Capture(target);

        // 仅无界轴（设了 ContentExtentProvider）才隐藏光标 + warp：手柄可拖出内容外无限后滚，须锁光标于锚点
        // 累积位移、不受屏幕边约束。有界轴拖动范围有限、光标与手柄 1:1 同步，无需隐藏，走普通位置位移。
        mWarping = ContentExtentProvider != null && CursorWarp.TryGetPosition(out mWarpAnchorX, out mWarpAnchorY);
        if (mWarping)
        {
            mScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            mSavedCursor = target.Cursor;
            target.Cursor = new Cursor(StandardCursorType.None);
            CursorWarp.SetCursorVisible(false);
        }
        else
        {
            mLastDragCoord = mOrientation == Orientation.Vertical ? p.Y : p.X;
        }

        e.Handled = true;
    }

    void HandleMove(PointerEventArgs e)
    {
        if (!mDragging)
            return;

        double delta;
        if (mWarping)
        {
            // 光标锁定式：读位移后把光标 warp 回锚点 → 光标原地不动、可无限往外拖。
            if (!CursorWarp.TryGetPosition(out double cx, out double cy))
                return;
            double deltaNative = mOrientation == Orientation.Vertical ? cy - mWarpAnchorY : cx - mWarpAnchorX;
            if (deltaNative == 0)
                return;
            CursorWarp.SetPosition(mWarpAnchorX, mWarpAnchorY);
            // Windows/X11 坐标为物理像素、需折算到 DIP；macOS 坐标已是逻辑单位（点），直接用。
            delta = CursorWarp.CoordinatesAreLogical ? deltaNative : deltaNative / mScale;
        }
        else
        {
            var p = e.GetPosition(CaptureTarget);
            double now = mOrientation == Orientation.Vertical ? p.Y : p.X;
            delta = now - mLastDragCoord;
            mLastDragCoord = now;
        }

        if (delta != 0)
            mAxis.ViewOffset = mAxis.ViewOffset + delta * mDragRatio;   // 直接改数据本体，轴自钳范围
        e.Handled = true;
    }

    void EndDrag(IPointer? pointer)
    {
        if (mWarping)
        {
            // 让光标在"相对手柄不变"的位置重现：手柄随滚动移了多少，光标就从锚点顺移多少，落回手柄上。
            if (TryGetThumb(out double posNow, out _))
            {
                double shift = posNow - mDragThumbStartPos;
                double screenShift = CursorWarp.CoordinatesAreLogical ? shift : shift * mScale;
                if (mOrientation == Orientation.Vertical)
                    CursorWarp.SetPosition(mWarpAnchorX, mWarpAnchorY + screenShift);
                else
                    CursorWarp.SetPosition(mWarpAnchorX + screenShift, mWarpAnchorY);
            }

            CaptureTarget.Cursor = mSavedCursor;
            CursorWarp.SetCursorVisible(true);   // 与按下时的隐藏成对恢复
        }

        mDragging = false;
        mWarping = false;
        if (pointer != null && ReferenceEquals(pointer.Captured, CaptureTarget))
            pointer.Capture(null);
    }

    readonly IScrollAxis mAxis;
    readonly Orientation mOrientation;

    InputElement? mSource;
    Cursor? mSavedCursor;
    bool mDragging;
    double mDragRatio;
    double mLastDragCoord;
    double mDragThumbStartPos;

    bool mWarping;
    double mWarpAnchorX;
    double mWarpAnchorY;
    double mScale = 1;

    const double ThumbThickness = 8;    // 手柄宽度：够大方、好点中，仍不膨胀
    const double ThumbOpacity = 0.5;    // 手柄自身透明度（整体显隐由外部 Opacity 再叠）
    const double EdgeMargin = 3;        // 手柄离边缘留白
    const double GrabPadding = 6;       // 抓取判定较视觉手柄略放宽
}
