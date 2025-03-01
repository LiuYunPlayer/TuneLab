using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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