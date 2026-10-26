using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

using TuneLab.GUI.Controllers;

namespace TuneLab.GUI.Components;

internal class TextInput : TextBox, IDataValueController<string>
{
    public new IBrush? Background { get => base.Background; set { base.Background = value; RefreshStyles(); } }
    public new Thickness BorderThickness { get => base.BorderThickness; set { base.BorderThickness = value; RefreshStyles(); } }
    public IActionEvent EnterInput => mEnterInput;
    public new IActionEvent TextChanged => mTextChanged;
    public IActionEvent EndInput => mEndInput;
    public new string Text { get => base.Text ?? string.Empty; set { mEnterInput.Invoke(); base.Text = value; mTextChanged.Invoke(); mEndInput.Invoke(); } }

    public IActionEvent ValueWillChange => EnterInput;
    public IActionEvent ValueChanged => TextChanged;
    public IActionEvent ValueCommitted => EndInput;
    public string Value => Text;

    // Esc 放弃本次编辑：显示复原成开始编辑时的样子、失焦。只对"确认时才写入"的用法成立（SingleLineTextController）：
    // 编辑中从没写过数据，复原显示就等于什么都没发生。默认关——对话框里的输入框按 Esc 多半是想关窗。
    public bool DiscardOnEscape { get; set; }

    public TextInput()
    {
        MinWidth = 0;
        MinHeight = 0;

        Padding = new(8, 0);
        Background = Style.BACK.ToBrush();
        Foreground = Style.TEXT_NORMAL.ToBrush();
        FontSize = 12;
        BorderThickness = new(0);
        CornerRadius = new(4);
        SelectionBrush = Style.LIGHT_WHITE.ToBrush();
        SelectionForegroundBrush = Style.BACK.ToBrush();
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        CaretBrush = Brushes.White;

        mStyles = new(this);
        Styles.Add(mStyles);

        GotFocus += (s, e) => { mFocusText = base.Text ?? string.Empty; mFocusWatermark = Watermark; mEnterInput.Invoke(); };
        LostFocus += (s, e) => { mEndInput.Invoke(); };
        KeyDown += (s, e) => { if (!AcceptsReturn && e.Key == Key.Enter) this.Unfocus(); };
        AddHandler(KeyDownEvent, (s, e) =>
        {
            if (!DiscardOnEscape || e.Key != Key.Escape || !IsFocused)
                return;
            e.Handled = true;
            // 复原成开始编辑时的样子（含多值的占位提示）：聚焦中的控件不接外部刷新，只能自己复原。
            base.Text = mFocusText;
            Watermark = mFocusWatermark;
            this.Unfocus();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    // 编辑中（聚焦）不被外部刷新覆盖：多选扇出会逐对象触发刷新，中途重写文本框会清空/重置光标。
    // 用户输入在编辑期内即权威值，失焦后再由正常刷新归位。
    public void Display(string text)
    {
        if (IsFocused)
            return;
        Watermark = string.Empty;
        base.Text = text;
    }

    public void DisplayNull()
    {
        if (IsFocused)
            return;
        Watermark = string.Empty;
        base.Text = string.Empty;
    }

    // 多值：用 watermark 占位而非真实文本——聚焦输入即从空白起编，不会编辑到 "(Multiple)" 字面量。
    public void DisplayMultiple()
    {
        if (IsFocused)
            return;
        base.Text = string.Empty;
        Watermark = "(Multiple)";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var text = Text;
        base.OnKeyDown(e);
        if (text != Text)
            mTextChanged.Invoke();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        var text = Text;
        base.OnTextInput(e);
        if (text != Text)
            mTextChanged.Invoke();
    }

    readonly ActionEvent mEnterInput = new();
    readonly ActionEvent mTextChanged = new();
    readonly ActionEvent mEndInput = new();
    string mFocusText = string.Empty;   // 开始编辑（聚焦）时的显示，Esc 放弃时复原
    string? mFocusWatermark;

    class TextInputStyles : Styles
    {
        public TextInputStyles(TextInput textInput)
        {
            Add(new Avalonia.Styling.Style(x => x.OfType<TextBox>().Class(":focus").Template().Name("PART_BorderElement"))
            {
                Setters = {
                new Setter(TextBox.BorderThicknessProperty, textInput.BorderThickness),
                new Setter(TextBox.BackgroundProperty, textInput.Background),
            }
            });
            Add(new Avalonia.Styling.Style(x => x.OfType<TextBox>().Class(":pointerover").Template().Name("PART_BorderElement"))
            {
                Setters = {
                new Setter(TextBox.BorderThicknessProperty, textInput.BorderThickness),
                new Setter(TextBox.BackgroundProperty, textInput.Background),
            }
            });
        }
    }

    void RefreshStyles()
    {
        Styles.Remove(mStyles);
        mStyles = new(this);
        Styles.Add(mStyles);
    }

    TextInputStyles mStyles;
}
