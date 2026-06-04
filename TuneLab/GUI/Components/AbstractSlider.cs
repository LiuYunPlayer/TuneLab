using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using TuneLab.Foundation.Event;
using TuneLab.GUI.Input;
using TuneLab.Foundation.Science;
using TuneLab.Utils;
using TuneLab.Foundation.Utils;
using TuneLab.Foundation.Property;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;

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
    public bool IsInterger { get => mIsInterger; set { mIsInterger = value; RefreshUI(); } }
    public int IntergerValue => mValue.Round();
    public AbstractThumb? Thumb { get => mThumb.Value; set => mThumb.Set(value); }

    int IValueController<int>.Value => IntergerValue;

    public abstract class AbstractThumb(AbstractSlider slider) : MovableComponent
    {
        public virtual Avalonia.Point Piovt => new(Bounds.Width / 2, Bounds.Height / 2);

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

    protected abstract Avalonia.Point StartPoint { get; }
    protected abstract Avalonia.Point EndPoint { get; }

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

        Thumb?.Arrange(new(ThumbPivotPosition() - Thumb.Piovt, Thumb.DesiredSize));
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
        if (IsInterger)
            value = value.Round();

        if (value == Value)
            return;

        mValue = value;
        mValueChanged.Invoke();
    }

    public Avalonia.Point ThumbPivotPosition()
    {
        double x = MathUtility.LineValue(MinValue, StartPoint.X, MaxValue, EndPoint.X, Value.Limit(mMinValue, mMaxValue));
        double y = MathUtility.LineValue(MinValue, StartPoint.Y, MaxValue, EndPoint.Y, Value.Limit(mMinValue, mMaxValue));
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
    bool mIsInterger = false;
    double mValue = 0;

    readonly Holder<AbstractThumb> mThumb = new();

    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommitted = new();
    readonly ActionEvent mValueDisplayed = new();
    readonly DisposableManager s = new();
}
