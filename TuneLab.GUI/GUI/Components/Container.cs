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

        // 子元素冒泡上来、没人处理的指针事件：主组件与子元素是兄弟、不在冒泡路径上，得在这里转给它——只转声明了要转的子元素
        //（Component.ForwardsUnhandledPointerToContainer，如插件画布：它不要的按下 / 滚轮归下面这层的手势，中键平移、Ctrl 滚轮缩放……）。
        // 拖动的归属（谁收后续的移动与抬起）：子元素没认领的按下转给主组件，主组件认领（置 Handled）就把鼠标捕获拿过来，
        // 子元素因捕获丢失收到补发的抬起；都没认领则先不定，捕获留在子元素——按着键的移动照样先问它，它认领就归它，
        // 它不认领的再转给主组件，主组件认领同样拿走捕获。抬起时仍没人认领，抬起给子元素（它的按下与抬起照样成对）。
        // 移动连已处理的也要看（handledEventsToo）：子元素接住了的移动不转，但要清掉本层的指针形状，见 OnChildPointer。
        AddHandler(PointerPressedEvent, OnChildPointer, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnChildPointer, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnChildPointer, RoutingStrategies.Bubble);
    }

    void OnChildPointer(object? sender, PointerEventArgs e)
    {
        if (e.Source is not Component { ForwardsUnhandledPointerToContainer: true } source || ReferenceEquals(source, mMainComponent)
            || !mLayoutPanel.IsVisualAncestorOf(source))
            return;
        if (e.Handled)
        {
            // 子元素自己接住了这次移动：本层只在收到移动时重算指针形状，移动被子元素接走就收不到——不处理，子元素（指针形状随祖先）
            // 会一直显示本层上一次留下的形状。
            if (e.RoutedEvent == PointerMovedEvent)
                OnChildHandledPointerMove();
            return;
        }
        mMainComponent.ReceiveForwarded(e);
        // 主组件认领了这次按下 / 按着键时的这次移动：之后的移动与抬起归它。
        if (e.Handled && (e is PointerPressedEventArgs || (e.RoutedEvent == PointerMovedEvent && IsAnyButtonPressed(e))))
            e.Pointer.Capture(mMainComponent);
        else if (e is PointerPressedEventArgs pressed)
            mMainComponent.ForgetPress(pressed.GetCurrentPoint(mMainComponent).Properties.PointerUpdateKind.ToMouseButtonType());
    }

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