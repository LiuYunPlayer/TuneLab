using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Input;

namespace TuneLab.GUI.Components;

internal class Container : ContainerBase
{
    public ModifierKeys Modifiers => mMainComponent.Modifiers;
    public Avalonia.Point MousePosition => mMainComponent.MousePosition;
    public bool IsHover => mMainComponent.IsHover;
    public bool IsPrimaryButtonPressed => mMainComponent.IsPrimaryButtonPressed;
    public bool IsMiddleButtonPressed => mMainComponent.IsMiddleButtonPressed;
    public bool IsSecondaryButtonPressed => mMainComponent.IsSecondaryButtonPressed;
    public bool IsPressed => mMainComponent.IsPressed;
    public long DoubleClickInterval { get => mMainComponent.DoubleClickInterval; set => mMainComponent.DoubleClickInterval = value; }

    public new Controls Children => mLayoutPanel.Children;

    public Container()
    {
        mMainComponent = new(this);
        base.Children.Add(mMainComponent);

        mLayoutPanel = new(this);
        base.Children.Add(mLayoutPanel);

        // 一摞层：声明了要转的子元素（Component.ForwardsUnhandledPointerToContainer，如插件画布）叠在上面，主组件（宿主那一层）在最底。
        // Avalonia 只把事件给命中的最上面那个子元素；它不认领（没置 Handled）冒泡到这里，按叠放顺序往下问——同样落在指针下的
        // 其他转发子元素（ZIndex 高的在上，同 ZIndex 后加的在上），最后是主组件。主组件与子元素是兄弟、不在冒泡路径上，得在这里转。
        // 拖动的归属（谁收后续的移动与抬起）：谁认领（置 Handled）就把鼠标捕获拿过去，其余收过按下的层收到补发的抬起；
        // 都没认领则先不定，捕获留在最上面那层——按着键的移动照样先问它，它不认领再按顺序问收过按下的其余层、最后主组件，第一个认领的拿走。
        // 一直没人认领就松手：抬起先给最上面那层，它没置 Handled 再按顺序给其余层，第一个置 Handled 的拿走这次点击，其余的收到 IsClick = false。
        // 滚轮、没按键的移动（悬停）不分层：最上面那层不要就直接给主组件。
        // 移动、抬起连已处理的也要看（handledEventsToo）：最上面那层自己认领了，其余收过按下的层要撤掉；接住了移动还要清本层的指针形状。
        AddHandler(PointerPressedEvent, OnChildPointer, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnChildPointer, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnChildPointer, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnChildPointer, RoutingStrategies.Bubble);
    }

    void OnChildPointer(object? sender, PointerEventArgs e)
    {
        if (e.Source is not Component { ForwardsUnhandledPointerToContainer: true } source || ReferenceEquals(source, mMainComponent)
            || !mLayoutPanel.IsVisualAncestorOf(source))
            return;
        if (e.Handled)
        {
            if (e.RoutedEvent == PointerMovedEvent)
            {
                // 子元素自己接住了这次移动：本层只在收到移动时重算指针形状，移动被子元素接走就收不到——不处理，子元素（指针形状随祖先）
                // 会一直显示本层上一次留下的形状。
                OnChildHandledPointerMove();
                if (IsAnyButtonPressed(e))
                    AbandonPending(source);   // 最上面那层认领了这次拖动
            }
            else if (e.RoutedEvent == PointerReleasedEvent)
            {
                AbandonPending(source);       // 最上面那层拿走了这次点击（或本就是它的拖动）
            }
            return;
        }

        if (e is PointerPressedEventArgs)
        {
            OnUnclaimedPress(source, e);
        }
        else if (e.RoutedEvent == PointerReleasedEvent)
        {
            OnUnclaimedRelease(e);
        }
        else if (e.RoutedEvent == PointerMovedEvent && IsAnyButtonPressed(e) && mPendingLayers.Count > 0)
        {
            OnUnclaimedDrag(e);
        }
        else
        {
            mMainComponent.ReceiveForwarded(e);   // 滚轮、悬停：不分层
        }
    }

    // 最上面那层没认领的按下：按叠放顺序问下面的层，最后主组件；都不认领则归属先不定（记下收过按下的层，捕获留在最上面那层）。
    void OnUnclaimedPress(Component source, PointerEventArgs e)
    {
        AbandonPending(null);   // 上一次没收尾的（防御）
        mPendingButton = ((PointerPressedEventArgs)e).GetCurrentPoint(source).Properties.PointerUpdateKind.ToMouseButtonType();
        mPendingLayers.Add(source);
        foreach (var layer in LayersBelow(source, e))
        {
            layer.ReceiveForwarded(e);
            if (e.Handled)
            {
                Claim(layer, e);
                return;
            }
            mPendingLayers.Add(layer);
        }
        mMainComponent.ReceiveForwarded(e);
        if (e.Handled)
        {
            Claim(mMainComponent, e);
            return;
        }
        mMainComponent.ForgetPress(mPendingButton);   // 主组件不收后续的移动与抬起，撤掉它按着的状态
    }

    // 归属未定时按着键、最上面那层没认领的移动：按顺序问收过按下的其余层，最后主组件，第一个认领的拿走。
    void OnUnclaimedDrag(PointerEventArgs e)
    {
        for (int i = 1; i < mPendingLayers.Count; i++)
        {
            var layer = mPendingLayers[i];
            layer.ReceiveForwarded(e);
            if (e.Handled)
            {
                Claim(layer, e);
                return;
            }
        }
        mMainComponent.ReceiveForwarded(e);
        if (e.Handled)
            Claim(mMainComponent, e);
    }

    // 一直没人认领、最上面那层也没要这次点击的抬起：按顺序给收过按下的其余层，第一个置 Handled 的拿走点击，其余的收到 IsClick = false。
    void OnUnclaimedRelease(PointerEventArgs e)
    {
        if (mPendingLayers.Count == 0)
        {
            mMainComponent.ReceiveForwarded(e);
            return;
        }
        var layers = mPendingLayers.Skip(1).ToList();
        mPendingLayers.Clear();
        bool taken = false;
        foreach (var layer in layers)
        {
            if (taken)
            {
                layer.AbandonPress(mPendingButton);
                continue;
            }
            layer.ReceiveForwarded(e);
            taken = e.Handled;
        }
    }

    // owner 认领了这次拖动：捕获归它（最上面那层因捕获丢失自己补发抬起），其余收过按下的层撤掉。
    void Claim(Component owner, PointerEventArgs e)
    {
        e.Pointer.Capture(owner);
        AbandonPending(owner);
    }

    // 撤掉收过按下、没认领的层（除 keep 与最上面那层——它若不是认领者，会因捕获丢失自己补发抬起）。
    void AbandonPending(Component? keep)
    {
        for (int i = 1; i < mPendingLayers.Count; i++)
        {
            if (!ReferenceEquals(mPendingLayers[i], keep))
                mPendingLayers[i].AbandonPress(mPendingButton);
        }
        mPendingLayers.Clear();
    }

    // 指针下、叠在 source 下面的转发子元素，自上而下（ZIndex 高的在上，同 ZIndex 后加的在上——同 Avalonia 的命中顺序）。
    IEnumerable<Component> LayersBelow(Component source, PointerEventArgs e)
    {
        var children = mLayoutPanel.Children;
        int sourceIndex = children.IndexOf(source);
        var below = new List<(Component Layer, int ZIndex, int Index)>();
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not Component { ForwardsUnhandledPointerToContainer: true, IsVisible: true, IsHitTestVisible: true } layer
                || ReferenceEquals(layer, source))
                continue;
            bool isBelow = layer.ZIndex < source.ZIndex || layer.ZIndex == source.ZIndex && i < sourceIndex;
            var p = e.GetPosition(layer);
            if (isBelow && p.X >= 0 && p.Y >= 0 && p.X < layer.Bounds.Width && p.Y < layer.Bounds.Height)
                below.Add((layer, layer.ZIndex, i));
        }
        return below.OrderByDescending(b => b.ZIndex).ThenByDescending(b => b.Index).Select(b => b.Layer);
    }

    readonly List<Component> mPendingLayers = [];   // 归属未定时收过这次按下的层（[0] = 最上面那层，捕获在它那儿）
    MouseButtonType mPendingButton;

    // 子元素接走了一次移动时本层的指针形状：默认清掉（本层按位置算出的形状在子元素上不适用）。与位置无关的形状（如按着 Shift 的
    // I 形光标）由派生类自己留着。
    protected virtual void OnChildHandledPointerMove() => Cursor = null;

    static bool IsAnyButtonPressed(PointerEventArgs e)
    {
        var properties = e.GetCurrentPoint(null).Properties;
        return properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed || properties.IsRightButtonPressed;
    }

    public new virtual void Render(DrawingContext context) { }
    protected virtual void OnScroll(WheelEventArgs e) { }
    protected virtual void OnMouseDown(MouseDownEventArgs e) { }
    protected virtual void OnMouseMove(MouseMoveEventArgs e) { }
    protected virtual void OnMouseUp(MouseUpEventArgs e) { }
    protected virtual void OnMouseEnter(MouseEnterEventArgs e) { }
    protected virtual void OnMouseLeave(MouseLeaveEventArgs e) { }
    protected virtual void OnKeyDownEvent(KeyEventArgs e) { }
    protected virtual void OnKeyPressedEvent(KeyEventArgs e) { }
    protected virtual void OnKeyUpEvent(KeyEventArgs e) { }

    protected new virtual Size MeasureOverride(Size availableSize)
    {
        return mLayoutPanel.BaseMeasureOverride(availableSize);
    }

    protected new virtual Size MeasureCore(Size availableSize)
    {
        return mLayoutPanel.BaseMeasureCore(availableSize);
    }

    protected new virtual Size ArrangeOverride(Size finalSize)
    {
        return mLayoutPanel.BaseArrangeOverride(finalSize);
    }

    protected new virtual void ArrangeCore(Rect finalRect)
    {
        mLayoutPanel.BaseArrangeCore(finalRect);
    }

    protected new virtual void OnMeasureInvalidated()
    {
        mLayoutPanel.BaseOnMeasureInvalidated();
    }

    public new void InvalidateMeasure()
    {
        base.InvalidateMeasure();
        mLayoutPanel.InvalidateMeasure();
    }

    public new void InvalidateArrange()
    {
        base.InvalidateArrange();
        mLayoutPanel.InvalidateArrange();
    }

    public new void InvalidateVisual()
    {
        base.InvalidateVisual();
        mMainComponent.InvalidateVisual();
    }

    protected sealed override Size ArrangeOverrideImpl(Size finalSize)
    {
        mMainComponent.Arrange(new(finalSize));
        mLayoutPanel.Arrange(new(finalSize));
        return finalSize;
    }

    class MainComponent(Container container) : Component
    {
        public override void Render(DrawingContext context)
        {
            container.Render(context);
        }

        protected override void OnScroll(WheelEventArgs e)
        {
            container.OnScroll(e);
        }

        protected override void OnMouseDown(MouseDownEventArgs e)
        {
            container.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            container.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseUpEventArgs e)
        {
            container.OnMouseUp(e);
        }

        protected override void OnMouseEnter(MouseEnterEventArgs e)
        {
            container.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(MouseLeaveEventArgs e)
        {
            container.OnMouseLeave(e);
        }

        protected override void OnKeyDownEvent(KeyEventArgs e)
        {
            container.OnKeyDownEvent(e);
        }

        protected override void OnKeyPressedEvent(KeyEventArgs e)
        {
            container.OnKeyPressedEvent(e);
        }

        protected override void OnKeyUpEvent(KeyEventArgs e)
        {
            container.OnKeyUpEvent(e);
        }
    }

    class LayoutPanel(Container container) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            return container.MeasureOverride(availableSize);
        }

        protected override Size MeasureCore(Size availableSize)
        {
            return container.MeasureCore(availableSize);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            return container.ArrangeOverride(finalSize);
        }

        protected override void ArrangeCore(Rect finalRect)
        {
            container.ArrangeCore(finalRect);
        }

        protected override void OnMeasureInvalidated()
        {
            container.OnMeasureInvalidated();
        }

        public Size BaseMeasureOverride(Size availableSize)
        {
            return base.MeasureOverride(availableSize);
        }

        public Size BaseMeasureCore(Size availableSize)
        {
            return base.MeasureCore(availableSize);
        }

        public Size BaseArrangeOverride(Size finalSize)
        {
            return base.ArrangeOverride(finalSize);
        }

        public void BaseArrangeCore(Rect finalRect)
        {
            base.ArrangeCore(finalRect);
        }

        public void BaseOnMeasureInvalidated()
        {
            base.OnMeasureInvalidated();
        }
    }

    readonly MainComponent mMainComponent;
    readonly LayoutPanel mLayoutPanel;
}

internal abstract class ContainerBase : Panel
{
    protected sealed override Size MeasureCore(Size availableSize)
    {
        return base.MeasureCore(availableSize);
    }

    protected sealed override Size MeasureOverride(Size availableSize)
    {
        return base.MeasureOverride(availableSize);
    }

    protected sealed override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
    }

    protected sealed override Size ArrangeOverride(Size finalSize)
    {
        return ArrangeOverrideImpl(finalSize);
    }

    protected sealed override void OnMeasureInvalidated()
    {
        base.OnMeasureInvalidated();
    }

    protected abstract Size ArrangeOverrideImpl(Size finalSize);
}