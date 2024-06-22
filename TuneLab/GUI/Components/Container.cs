using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Input;

namespace TuneLab.GUI.Components;

internal class Container : Panel
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
    protected virtual void OnMouseLeave() { }
    protected virtual void OnKeyDownEvent(KeyEventArgs e) { }
    protected virtual void OnKeyPressedEvent(KeyEventArgs e) { }
    protected virtual void OnKeyUpEvent(KeyEventArgs e) { }

    protected virtual Size OnMeasureOverride(Size availableSize)
    {
        return mLayoutPanel.BaseMeasureOverride(availableSize);
    }

    protected virtual Size OnMeasureCore(Size availableSize)
    {
        return mLayoutPanel.BaseMeasureCore(availableSize);
    }

    protected virtual Size OnArrangeOverride(Size finalSize)
    {
        return mLayoutPanel.BaseArrangeOverride(finalSize);
    }

    protected virtual void OnArrangeCore(Rect finalRect)
    {
        mLayoutPanel.BaseArrangeCore(finalRect);
    }

    protected virtual void MeasureInvalidated()
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
        mMainComponent.Arrange(new(finalSize));
        mLayoutPanel.Arrange(new(finalSize));
        return finalSize;
    }

    protected sealed override void OnMeasureInvalidated()
    {
        base.OnMeasureInvalidated();
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

        protected override void OnMouseLeave()
        {
            container.OnMouseLeave();
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
            return container.OnMeasureOverride(availableSize);
        }

        protected override Size MeasureCore(Size availableSize)
        {
            return container.OnMeasureCore(availableSize);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            return container.OnArrangeOverride(finalSize);
        }

        protected override void ArrangeCore(Rect finalRect)
        {
            container.OnArrangeCore(finalRect);
        }

        protected override void OnMeasureInvalidated()
        {
            container.MeasureInvalidated();
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
