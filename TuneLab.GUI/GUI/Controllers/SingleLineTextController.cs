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
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;

namespace TuneLab.GUI.Controllers;

internal class SingleLineTextController : LayerPanel, IDataValueController<string>
{
    public IActionEvent ValueWillChange => mTextInput.EnterInput;
    public IActionEvent ValueChanged => mTextInput.TextChanged;
    public IActionEvent ValueCommitted => mTextInput.EndInput;
    public string Value { get => mTextInput.Text; set => mTextInput.Text = value; }

    // 掩码开关：'\0' 为 TextBox 默认（不掩码），非零字符即逐字符掩码显示。
    public bool IsPassword { set => mTextInput.PasswordChar = value ? '●' : '\0'; }

    public SingleLineTextController()
    {
        mTextInput = new TextInput()
        {
            Height = 28,
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
        mTextInput.DisplayNull();
    }

    public void DisplayMultiple()
    {
        mTextInput.DisplayMultiple();
    }

    readonly TextInput mTextInput;
}
