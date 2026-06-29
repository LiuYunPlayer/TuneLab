using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Utils;
using TuneLab.SDK;

using TuneLab.GUI.Controllers;

namespace TuneLab.GUI.Components;

internal abstract class AbstractSlider : Container, IDataValueController<double>, IDataValueController<int>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommitted => mValueCommitted;
    public IActionEvent ValueDisplayed => mValueDisplayed;
    public double Value { get => mValue; set { if (value == Value) return; mValueWillChange.Invoke(); ChangeValue(value); mValueCommitted.Invoke(); } }
    // 量程由标度定义：两端即 ToValue(0)/ToValue(1)（约定标度单调递增）。
    public double MinValue => mScale.ToValue(0);
    public double MaxValue => mScale.ToValue(1);
    public double DefaultValue { get => mDefaultValue; set => mDefaultValue = value; }
    // 标度：位置↔值映射的唯一来源。SDK 滑条经此直接注入（含对数等任意标度）。
    public INormalizedScale Scale { get => mScale; set { mScale = value; RefreshUI(); } }
    // 便利开关：整数态。仅供宿主内非 SDK 滑条（设置面板/增益等）沿用旧式 SetRange + IsInteger 用法；
    // 改它会按当前量程重建线性/整数标度。SDK 滑条不走这条，直接设 Scale。
    public bool IsInteger { get => mIsInteger; set { mIsInteger = value; mScale = BuildLinearScale(MinValue, MaxValue); RefreshUI(); } }
    public int IntergerValue => mValue.Round();
    public AbstractThumb? Thumb { get => mThumb.Value; set => mThumb.Set(value); }

    int IValueController<int>.Value => IntergerValue;

    public abstract class AbstractThumb(AbstractSlider slider) : MovableComponent
    {
        // 用 DesiredSize 而非 Bounds：首次 arrange 时 thumb 自身 Bounds 仍为 0（尚未定尺寸），
        // 用 Bounds 算出的中心偏移为 0 → thumb 左上角贴 pivot（偏右下半身位），下一帧才居中 → 可见跳动。
        // DesiredSize 在 arrange 前的 measure 阶段已确定（thumb 有固定 Width/Height），两帧一致。
        public virtual Avalonia.Point Piovt => new(DesiredSize.Width / 2, DesiredSize.Height / 2);

        protected override void OnMouseDown(MouseDownEventArgs e)
        {
            if (e.IsDoubleClick)
            {
                mLastDownIsDoubleClick = true;
                slider.Value = slider.DefaultValue;
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEventArgs e)
        {
            if (mLastDownIsDoubleClick)
            {
                mLastDownIsDoubleClick = false;
                return;
            }


            base.OnMouseUp(e);
        }

        bool mLastDownIsDoubleClick = false;
    }

    public AbstractSlider()
    {
        mThumb.When(thumb => thumb.MoveStart).Subscribe(OnThumbMoveStart, s);
        mThumb.When(thumb => thumb.MoveEnd).Subscribe(OnThumbMoveEnd, s);
        mThumb.When(thumb => thumb.Moved).Subscribe(OnThumbMoved, s);
        mThumb.WillModify.Subscribe(() => { if (mThumb.Value == null) return; Children.Remove(mThumb.Value); }, s);
        mThumb.Modified.Subscribe(() => { if (mThumb.Value == null) return; Children.Add(mThumb.Value); }, s);
        ValueChanged.Subscribe(RefreshUI);
    }

    ~AbstractSlider()
    {
        s.DisposeAll();
    }

    public void SetRange(double min, double max)
    {
        mScale = BuildLinearScale(min, max);
        RefreshUI();
    }

    INormalizedScale BuildLinearScale(double min, double max)
        => mIsInteger ? NormalizedScale.Integer(min, max) : NormalizedScale.Linear(min, max);

    public void Display(double value)
    {
        mValue = value;
        RefreshUI();
    }

    public void Display(int value)
    {
        Display((double)value);
    }

    public void DisplayNull()
    {
        Display(double.NaN);
    }

    public void DisplayMultiple()
    {
        Display(double.NaN);
    }

    // 由具体 slider 按给定尺寸算两端点。布局期(ArrangeOverride)必须用传入的 finalSize 而非 Bounds——
    // Bounds 在 arrange 完成前仍是上一帧值，首帧据此定位会错位、下一帧才跳到正确处。
    protected abstract Avalonia.Point GetStartPoint(Size size);
    protected abstract Avalonia.Point GetEndPoint(Size size);

    // 非布局期（鼠标命中映射等）Bounds 已是最新，照常用之。
    protected Avalonia.Point StartPoint => GetStartPoint(Bounds.Size);
    protected Avalonia.Point EndPoint => GetEndPoint(Bounds.Size);

    bool mLastDownIsDoubleClick = false;

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        if (e.MouseButtonType != MouseButtonType.PrimaryButton)
            return;

        if (e.IsDoubleClick)
        {
            mLastDownIsDoubleClick = true;
            Value = DefaultValue;
            return;
        }

        mValueWillChange.Invoke();
        MoveTo(e.Position);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!IsPrimaryButtonPressed)
            return;

        MoveTo(e.Position);
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        if (e.MouseButtonType != MouseButtonType.PrimaryButton)
            return;

        if (mLastDownIsDoubleClick)
        {
            mLastDownIsDoubleClick = false;
            return;
        }

        mValueCommitted.Invoke();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (double.IsNaN(mValue))
            return finalSize;

        // 用传入的 finalSize 算端点（首帧 Bounds 尚未更新、为 0），thumb 偏移用 DesiredSize（见 Piovt）——
        // 二者合起来保证首次 arrange 即定位正确，消除 thumb 从偏位跳到正确处的现象。
        Thumb?.Arrange(new(ThumbPivotPosition(finalSize) - Thumb.Piovt, Thumb.DesiredSize));
        return finalSize;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        InvalidateArrange();
    }

    void OnThumbMoveStart()
    {
        mValueWillChange.Invoke();
    }

    void OnThumbMoveEnd()
    {
        mValueCommitted.Invoke();
    }

    void OnThumbMoved(Avalonia.Point point)
    {
        MoveTo(point + Thumb!.Piovt);
    }

    void MoveTo(Avalonia.Point point)
    {
        ChangeValue(ValueOn(point));
    }

    double ValueOn(Avalonia.Point point)
    {
        Vector axis = (EndPoint - StartPoint).ToVector();
        Vector vector = (point - StartPoint).ToVector();
        var r = vector * axis / axis.SquaredLength;
        return double.IsNaN(r) ? DefaultValue : mScale.ToValue(r.Limit(0, 1));
    }

    void ChangeValue(double value)
    {
        // 经标度归一化后钳到 [0,1] 再回值：一步同时完成钳量程 + 离散化（整数/步长等）。
        if (!double.IsNaN(value))
            value = mScale.ToValue(mScale.ToNormalized(value).Limit(0, 1));

        if (value == Value)
            return;

        mValue = value;
        mValueChanged.Invoke();
    }

    public Avalonia.Point ThumbPivotPosition() => ThumbPivotPosition(Bounds.Size);

    Avalonia.Point ThumbPivotPosition(Size size)
    {
        var start = GetStartPoint(size);
        var end = GetEndPoint(size);
        double t = mScale.ToNormalized(mValue).Limit(0, 1);
        double x = start.X + (end.X - start.X) * t;
        double y = start.Y + (end.Y - start.Y) * t;
        return new Avalonia.Point(x, y);
    }

    void RefreshUI()
    {
        if (mThumb.Value != null)
            mThumb.Value.IsVisible = !double.IsNaN(mValue);

        InvalidateArrange();
        InvalidateVisual();
        mValueDisplayed.Invoke();
    }

    INormalizedScale mScale = NormalizedScale.Linear(0, 1);
    double mDefaultValue = 0;
    bool mIsInteger = false;
    double mValue = 0;

    readonly Holder<AbstractThumb> mThumb = new();

    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommitted = new();
    readonly ActionEvent mValueDisplayed = new();
    readonly DisposableManager s = new();
}
