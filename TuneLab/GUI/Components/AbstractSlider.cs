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
    public double MinValue => mMinValue;
    public double MaxValue => mMaxValue;
    public double DefaultValue { get => mDefaultValue; set => mDefaultValue = value; }
    public bool IsInteger { get => mIsInteger; set { mIsInteger = value; RefreshUI(); } }
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
        mMinValue = min;
        mMaxValue = max;
        RefreshUI();
    }

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
        var v = MathUtility.LineValue(0, MinValue, 1, MaxValue, r);
        return double.IsNaN(v) ? DefaultValue : v;
    }

    void ChangeValue(double value)
    {
        value = value.Limit(MinValue, MaxValue);
        if (IsInteger)
            value = value.Round();

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
        double x = MathUtility.LineValue(MinValue, start.X, MaxValue, end.X, Value.Limit(mMinValue, mMaxValue));
        double y = MathUtility.LineValue(MinValue, start.Y, MaxValue, end.Y, Value.Limit(mMinValue, mMaxValue));
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

    double mMinValue = 0;
    double mMaxValue = 1;
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
