using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;

namespace TuneLab.GUI.Components;

internal class ScrollView : Panel
{
    public IScrollAxis HorizontalAxis => mHorizontalAxis;
    public IScrollAxis VerticalAxis => mVerticalAxis;
    public Control? Content
    {
        get => mContent;
        set => mContent.Set(value);
    }

    public bool FitWidth
    {
        get => mIsFitWidth;
        set { mIsFitWidth = value; InvalidateArrange(); }
    }

    public bool FitHeight
    {
        get => mIsFitHeight;
        set { mIsFitHeight = value; InvalidateArrange(); }
    }

    public ScrollView()
    {
        ClipToBounds = true;

        mHorizontalAxis.AxisChanged += InvalidateArrange;
        mVerticalAxis.AxisChanged += InvalidateArrange;
        mContent.When<Control, EventHandler<SizeChangedEventArgs>>(
            (c, e) => { c.SizeChanged += e; },
            (c, e) => { c.SizeChanged -= e; }).Subscribe(
            OnContentSizeChanged,
            s);
        mContent.ObjectWillChange.Subscribe(OnContentWillChange, s);
        mContent.ObjectChanged.Subscribe(OnContentChanged, s);
    }

    ~ScrollView()
    {
        s.DisposeAll();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        availableSize = new Size(double.PositiveInfinity, double.PositiveInfinity);

        foreach (Control child in Children)
        {
            child.Measure(availableSize);
        }

        return new Size();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = mContent.Object;
        if (content == null)
            return finalSize;

        var contentSize = content.DesiredSize;
        var contentWidth = mIsFitWidth ? finalSize.Width : contentSize.Width;
        var contentHeight = mIsFitHeight ? finalSize.Height : contentSize.Height;
        mContent.Object?.Arrange(new(-mHorizontalAxis.ViewOffset, -mVerticalAxis.ViewOffset, contentWidth, contentHeight));
        return finalSize;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mHorizontalAxis.ViewLength = e.NewSize.Width;
        mVerticalAxis.ViewLength = e.NewSize.Height;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var deltaX = shift ? e.Delta.Y : e.Delta.X;
        var deltaY = shift ? e.Delta.X : e.Delta.Y;
        if (deltaX != 0) mHorizontalAxis.AnimateMove(deltaX * 70);
        if (deltaY != 0) mVerticalAxis.AnimateMove(deltaY * 70);
    }

    void OnContentWillChange()
    {
        var content = mContent.Object;
        if (content == null)
            return;

        Children.Remove(content);
    }

    void OnContentChanged()
    {
        var content = mContent.Object;
        if (content == null)
            return;

        Children.Add(content);
        OnContentSizeChanged(content.Bounds.Size);
    }

    void OnContentSizeChanged(object? s, SizeChangedEventArgs e)
    {
        OnContentSizeChanged(e.NewSize);
    }

    void OnContentSizeChanged(Size size)
    {
        mHorizontalAxis.ContentSize = size.Width;
        mVerticalAxis.ContentSize = size.Height;
    }

    bool mIsFitWidth = false;
    bool mIsFitHeight = false;

    readonly Owner<Control> mContent = new();

    readonly AnimationScalableScrollAxis mHorizontalAxis = new();
    readonly AnimationScalableScrollAxis mVerticalAxis = new();
    readonly DisposableManager s = new();
}
