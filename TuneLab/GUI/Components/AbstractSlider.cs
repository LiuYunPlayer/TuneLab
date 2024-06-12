using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using TuneLab.Base.Event;
using TuneLab.GUI.Input;
using TuneLab.Base.Science;
using TuneLab.Utils;
using TuneLab.Base.Utils;

namespace TuneLab.GUI.Components;

internal abstract class AbstractSlider : Panel
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommited => mValueCommited;
    public double Value { get => mValue; set { if (value == Value) return; mValueWillChange.Invoke(); ChangeValue(value); mValueCommited.Invoke(); } }
    public double MinValue => mMinValue;
    public double MaxValue => mMaxValue;
    public double DefaultValue { get => mDefaultValue; set => mDefaultValue = value; }
    public bool IsInterger { get => mIsInterger; set { mIsInterger = value; RefreshUI(); } }
    public int IntergerValue => mValue.Round();
    public AbstractThumb? Thumb { get => mThumb.Object; set => mThumb.Set(value); }

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
        mThumb.ObjectWillChange.Subscribe(() => { if (mThumb.Object == null) return; Children.Remove(mThumb.Object); }, s);
        mThumb.ObjectChanged.Subscribe(() => { if (mThumb.Object == null) return; Children.Add(mThumb.Object); }, s);
        ValueChanged.Subscribe(RefreshUI);
        mSliderArea = new(this);
        Children.Add(mSliderArea);
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

    protected abstract Avalonia.Point StartPoint { get; }
    protected abstract Avalonia.Point EndPoint { get; }

    protected virtual void OnDraw(DrawingContext context)
    {

    }

    bool mLastDownIsDoubleClick = false;

    protected void OnMouseDown(MouseDownEventArgs e)
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

    protected void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!mSliderArea.IsPrimaryButtonPressed)
            return;

        MoveTo(e.Position);
    }

    protected void OnMouseUp(MouseUpEventArgs e)
    {
        if (e.MouseButtonType != MouseButtonType.PrimaryButton)
            return;

        if (mLastDownIsDoubleClick)
        {
            mLastDownIsDoubleClick = false;
            return;
        }

        mValueCommited.Invoke();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mSliderArea.Arrange(new(finalSize));
        if (double.IsNaN(mValue))
            return finalSize;

        mThumb.Object?.Arrange(new(ThumbPosition(), mThumb.Object.DesiredSize));
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
        mValueCommited.Invoke();
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
        return MathUtility.LineValue(0, MinValue, 1, MaxValue, r);
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

    Avalonia.Point ThumbPosition()
    {
        double x = MathUtility.LineValue(MinValue, StartPoint.X, MaxValue, EndPoint.X, Value);
        double y = MathUtility.LineValue(MinValue, StartPoint.Y, MaxValue, EndPoint.Y, Value);
        return new Avalonia.Point(x, y);
    }

    void RefreshUI()
    {
        if (mThumb.Object != null)
            mThumb.Object.IsVisible = !double.IsNaN(mValue);

        InvalidateArrange();
        mSliderArea.InvalidateVisual();
    }

    class SliderArea(AbstractSlider slider) : Component
    {
        protected override void OnMouseDown(MouseDownEventArgs e)
        {
            slider.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            slider.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseUpEventArgs e)
        {
            slider.OnMouseUp(e);
        }

        public override void Render(DrawingContext context)
        {
            slider.OnDraw(context);
        }
    }

    double mMinValue = 0;
    double mMaxValue = 1;
    double mDefaultValue = 0;
    bool mIsInterger = false;
    double mValue = 0;

    readonly SliderArea mSliderArea;
    readonly Owner<AbstractThumb> mThumb = new();

    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommited = new();
    readonly DisposableManager s = new();
}
