using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using DynamicData;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;
using TuneLab.GUI.Components;

namespace TuneLab.GUI.Controllers;

internal class SingleLineTextController : StackPanel, IMultipleValueController<string>
{
    public IActionEvent ValueWillChange => mTextInput.EnterInput;
    public IActionEvent ValueChanged => mTextInput.TextChanged;
    public IActionEvent ValueCommited => mTextInput.EndInput;
    public string Value { get => mTextInput.Text; set => mTextInput.Text = value; }

    public SingleLineTextController()
    {
        Orientation = Orientation.Vertical;

        mTextInput = new TextInput()
        {
            Height = 28,
            Margin = new(24, 12),
            AcceptsReturn = false
        };

        Children.Add(mTextInput);
    }

    public void Display(string text)
    {
        mTextInput.Display(text);
    }

    public void DisplayNull()
    {
        mTextInput.Display("-");
    }

    public void DisplayMultiple()
    {
        mTextInput.Display("(Multiple)");
    }

    readonly TextInput mTextInput;
}
