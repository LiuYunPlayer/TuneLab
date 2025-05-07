﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TuneLab.Foundation.Event;
using TuneLab.GUI.Controllers;

namespace TuneLab.GUI.Components;

internal class EditableLabel : LayerPanel, IDataValueController<string>
{
    public IActionEvent EndInput => mEndInput;
    public Thickness Padding { get => mTextInput.Padding; set { mTextBlock.Padding = value; mTextInput.Padding = value; } }
    public FontFamily FontFamily { get => mTextBlock.FontFamily; set { mTextBlock.FontFamily = value; mTextInput.FontFamily = value; } }
    public double FontSize { get => mTextBlock.FontSize; set { mTextBlock.FontSize = value; mTextInput.FontSize = value; } }
    public CornerRadius CornerRadius { get => mBorder.CornerRadius; set { mBorder.CornerRadius = value; mTextInput.CornerRadius = value; } }
    public Avalonia.Layout.HorizontalAlignment HorizontalContentAlignment { get => mTextBlock.HorizontalAlignment; set { mTextBlock.HorizontalAlignment = value; mTextInput.HorizontalContentAlignment = value; } }
    public Avalonia.Layout.VerticalAlignment VerticalContentAlignment { get => mTextBlock.VerticalAlignment; set { mTextBlock.VerticalAlignment = value; mTextInput.VerticalContentAlignment = value; } }
    public IBrush? Foreground { get => mTextBlock.Foreground; set { mTextBlock.Foreground = value; } }
    public new IBrush? Background { get => mTextBlock.Background; set { mBorder.Background = value; } }
    public IBrush? InputForeground { get => mTextInput.Foreground; set => mTextInput.Foreground = value; }
    public IBrush? InputBackground { get => mTextInput.Background; set => mTextInput.Background = value; }
    public string Text { get => mTextBlock.Text ?? string.Empty; set => mTextBlock.Text = value; }

    public IActionEvent ValueWillChange => mTextInput.ValueWillChange;
    public IActionEvent ValueChanged => mTextInput.ValueChanged;
    public IActionEvent ValueCommited => mEndInput;
    public string Value => mTextInput.IsVisible ? mTextInput.Value : Text;

    public EditableLabel()
    {
        Padding = new(8, 0);

        mBorder.DoubleTapped += (s, e) =>
        {
            mTextInput.Display(mTextBlock.Text ?? string.Empty);
            mTextInput.IsVisible = true;
            mTextInput.Focus();
            mTextInput.SelectAll();
        };
        mTextInput.EndInput.Subscribe(() =>
        {
            mTextBlock.Text = mTextInput.Text;
            mTextInput.IsVisible = false;
            mEndInput.Invoke();
        });

        Children.Add(mBorder);
        Children.Add(mTextBlock);
        Children.Add(mTextInput);
    }

    Border mBorder = new();
    TextBlock mTextBlock = new TextBlock() { IsHitTestVisible = false };
    TextInput mTextInput = new TextInput() { IsVisible = false };

    readonly ActionEvent mEndInput = new();

    public void Display(string value)
    {
        Text = value;
    }

    public void DisplayNull()
    {
        Text = string.Empty;
    }

    public void DisplayMultiple()
    {
        Text = "(Multiple)";
    }
}
