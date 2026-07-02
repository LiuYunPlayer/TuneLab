using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Globalization;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.GUI.Input;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 可拖拽数值框：横/纵拖动擦写数值（相对增量，无界）、双击原地键入精确值。number 控件族里 slider 的无界补集，
// 实现 IDataValueController<double> 与 slider 同接口——经 BindDataProperty 复用同一套 merge 预览 + 单撤销步机制。
//
// 值处理三者正交：Response.Apply（手感）→ Snap（吸附）→ Clamp（边界）。吸附在前、钳位在后，保证最终值严格落在
// [Min,Max] 内（边界不在步长格点上时边界优先）。
internal class DraggableNumberBox : Container, IDataValueController<double>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommitted => mValueCommitted;
    public double Value => mValue;

    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public IDragResponse Response { get; set; } = DragResponse.Linear(1.0);
    public double? Step { get; set; }
    public INumberFormat NumberFormat { get => mFormat; set { mFormat = value; InvalidateVisual(); } }
    // 静态显示时的底色（双击进入编辑时才显标准深色输入框）。默认深底；置 null = 透明（露出容器底色，如音素色条），仅绘数值文本。
    public IBrush? BoxBackground { get => mBoxBackground; set { mBoxBackground = value; InvalidateVisual(); } }
    // 静态数值文本颜色。默认 LIGHT_WHITE；透明底（如音素色条）上可设纯白以保对比。
    public IBrush TextForeground { get => mTextForeground; set { mTextForeground = value; InvalidateVisual(); } }
    // 三态（Multiple/Invalid，mValue=NaN）下无确定起点；从此值起拖（同 slider 的 ValueOn NaN 回退），使未设值的属性也可被拖动赋值。
    public double DefaultValue { get; set; } = 0;

    public DraggableNumberBox()
    {
        Height = 24;

        mTextInput = new TextInput() { IsVisible = false, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        Children.Add(mTextInput);

        // 编辑结束（失焦 / 回车）：解析文本，解析成功则按一个撤销步提交（WillChange→Set→Committed）。
        mTextInput.EndInput.Subscribe(() =>
        {
            if (!mTextInput.IsVisible)
                return;

            mTextInput.IsVisible = false;
            if (mFormat.Parse(mTextInput.Text) is double parsed)
            {
                mValueWillChange.Invoke();
                ChangeValue(parsed);
                mValueCommitted.Invoke();
            }
            RefreshText();
        });
    }

    public void Display(double value)
    {
        mState = State.Value;
        mValue = value;
        RefreshText();
    }

    public void DisplayNull()
    {
        mState = State.Invalid;
        mValue = double.NaN;
        RefreshText();
    }

    public void DisplayMultiple()
    {
        mState = State.Multiple;
        mValue = double.NaN;
        RefreshText();
    }

    public override void Render(DrawingContext context)
    {
        if (mBoxBackground != null)
            context.FillRectangle(mBoxBackground, this.Rect(), 4);

        var text = GetValueString();
        if (string.IsNullOrEmpty(text))
            return;

        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Assets.SegoeUI), 12, mTextForeground);
        context.DrawText(formatted, new((Bounds.Width - formatted.Width) / 2, (Bounds.Height - formatted.Height) / 2));
    }

    protected override void OnMouseEnter(MouseEnterEventArgs e)
    {
        if (!mDragging)
            Cursor = SizeWestEastCursor;
    }

    protected override void OnMouseLeave(MouseLeaveEventArgs e)
    {
        if (!mDragging)
            Cursor = null;
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        if (e.MouseButtonType != MouseButtonType.PrimaryButton)
            return;

        // 双击进入键入编辑（区别于 slider 的双击重置默认值）。
        if (e.IsDoubleClick)
        {
            mLastDownIsDoubleClick = true;
            EnterEdit();
            return;
        }

        // 三态（Multiple/Invalid，mValue=NaN）无确定起点，回退到 DefaultValue 起拖；否则从当前值起拖。
        // 此刻不开 merge：纯点击（按下即抬起、无移动）不应产生空 merge / 误锁音素，故 ValueWillChange 推迟到首次移动。
        mStartValue = double.IsNaN(mValue) ? DefaultValue : mValue;
        mDownPosition = MousePosition;
        mDragging = true;
        mDragBegun = false;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!mDragging)
            return;

        // 首次实际移动才开 merge（按下到此一定发生了位移：Component 已过滤同位置事件）。
        if (!mDragBegun)
        {
            mDragBegun = true;
            mValueWillChange.Invoke();
        }

        // 翻 Y 归一成"上为正"的用户直觉系；Shift 精调降灵敏度。
        double fine = e.KeyModifiers.HasFlag(ModifierKeys.Shift) ? FineTuneFactor : 1.0;
        var delta = new Point((MousePosition.X - mDownPosition.X) * fine, (mDownPosition.Y - MousePosition.Y) * fine);
        ChangeValue(Response.Apply(mStartValue, delta));
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

        if (!mDragging)
            return;

        mDragging = false;
        if (!IsHover)
            Cursor = null;

        if (mDragBegun)
        {
            mDragBegun = false;
            mValueCommitted.Invoke();
            // 拖完把焦点移离本控件：否则键盘焦点滞留在此，Ctrl+Z 等全局快捷键不路由到编辑器、撤销无效
            //（等价用户手动点别处移焦后才能撤销）。
            this.Unfocus();
        }
    }

    void EnterEdit()
    {
        mTextInput.Display(double.IsNaN(mValue) ? string.Empty : mFormat.Format(mValue));
        mTextInput.IsVisible = true;
        mTextInput.Focus();
        mTextInput.SelectAll();
    }

    void ChangeValue(double value)
    {
        if (!double.IsNaN(value))
        {
            if (Step is double step && step > 0)
                value = Math.Round(value / step) * step;
            if (MinValue is double min && value < min)
                value = min;
            if (MaxValue is double max && value > max)
                value = max;
        }

        if (value == mValue)
            return;

        mValue = value;
        mState = State.Value;
        mValueChanged.Invoke();
        RefreshText();
    }

    string GetValueString()
    {
        if (!double.IsNaN(mValue))
            return mFormat.Format(mValue);

        return mState == State.Multiple ? "-" : "";
    }

    void RefreshText() => InvalidateVisual();

    enum State { Value, Multiple, Invalid }

    const double FineTuneFactor = 0.25;
    static readonly Avalonia.Input.Cursor SizeWestEastCursor = new(StandardCursorType.SizeWestEast);

    readonly TextInput mTextInput;
    IBrush? mBoxBackground = Style.BACK.ToBrush();
    IBrush mTextForeground = Style.LIGHT_WHITE.ToBrush();
    INumberFormat mFormat = TuneLab.SDK.NumberFormat.Decimals(2);
    State mState = State.Value;
    double mValue = 0;
    double mStartValue = 0;
    Avalonia.Point mDownPosition;
    bool mDragging = false;
    bool mDragBegun = false;
    bool mLastDownIsDoubleClick = false;

    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommitted = new();
}
