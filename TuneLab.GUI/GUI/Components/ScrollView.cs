using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using TuneLab.Foundation;

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
        mContent.WillModify.Subscribe(OnContentWillChange, s);
        mContent.Modified.Subscribe(OnContentChanged, s);
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
        var content = mContent.Value;
        if (content == null)
            return finalSize;

        var contentSize = content.DesiredSize;
        var contentWidth = mIsFitWidth ? finalSize.Width : contentSize.Width;
        var contentHeight = mIsFitHeight ? finalSize.Height : contentSize.Height;
        mContent.Value?.Arrange(new(-mHorizontalAxis.ViewOffset, -mVerticalAxis.ViewOffset, contentWidth, contentHeight));
        // 就地把"刚安排下去的内容尺寸"同步进轴（值相同则 setter 直接返回、不会触发多余重排）。
        // 不能只依赖内容控件的 SizeChanged：那是内容 Bounds 变化后的回调，内容被持续追加时（如流式文本）
        // 轴会晚一帧甚至收不到通知，滚动条手柄与"贴底"判定都会用到过期的 ContentLength。这里与 SizeChanged
        // 报的是同一个值（都是安排尺寸），只是更及时。
        OnContentSizeChanged(new Size(contentWidth, contentHeight));
        return finalSize;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mHorizontalAxis.ViewLength = e.NewSize.Width;
        mVerticalAxis.ViewLength = e.NewSize.Height;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // 横向分量（触控板双指横滑 / 倾斜滚轮）恒走横轴；shift 则把纵滚也折进横轴、此时不再纵移。
        // 不能像原先那样 shift 时把两轴互换——横滑本就是横向意图，换轴会让 shift+横滑去滚纵轴。
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var deltaX = e.Delta.X + (shift ? e.Delta.Y : 0);
        var deltaY = shift ? 0 : e.Delta.Y;
        if (deltaX != 0) mHorizontalAxis.AnimateMove(deltaX * 70);
        if (deltaY != 0) mVerticalAxis.AnimateMove(deltaY * 70);
    }

    void OnContentWillChange()
    {
        var content = mContent.Value;
        if (content == null)
            return;

        Children.Remove(content);
    }

    void OnContentChanged()
    {
        var content = mContent.Value;
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

    readonly Holder<Control> mContent = new();

    readonly AnimationScalableScrollAxis mHorizontalAxis = new();
    readonly AnimationScalableScrollAxis mVerticalAxis = new();
    readonly DisposableManager s = new();
}
