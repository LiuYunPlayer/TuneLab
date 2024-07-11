using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Linq;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

internal class TextInput : TextBox, IDataValueController<string>
{
    public new IBrush? Background { get => base.Background; set { base.Background = value; RefreshStyles(); } }
    public new Thickness BorderThickness { get => base.BorderThickness; set { base.BorderThickness = value; RefreshStyles(); } }
    public IActionEvent EnterInput => mEnterInput;
    public new IActionEvent TextChanged => mTextChanged;
    public IActionEvent EndInput => mEndInput;
    public new string Text { get => base.Text ?? string.Empty; set => base.Text = value; }

    public IActionEvent ValueWillChange => EnterInput;
    public IActionEvent ValueChanged => TextChanged;
    public IActionEvent ValueCommited => EndInput;
    public string Value => Text;

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

        GotFocus += (s, e) => { mEnterInput.Invoke(); };
        LostFocus += (s, e) => { mEndInput.Invoke(); };
        KeyDown += (s, e) => { if (!AcceptsReturn && e.Key == Key.Enter) this.Unfocus(); };
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public void Display(string text)
    {
        Text = text;
    }

    public void DisplayNull()
    {
        Text = string.Empty;
    }

    public void DisplayMultiple()
    {
        Text = "(Multiple)";
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
