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
    // 确认时才写入（不逐键写预览）：失焦 / Enter 时文字与开始编辑时不同才发一次 ValueChanged，随即 ValueCommitted。
    // 逐键写会在第一次写入时就通知到合成引擎（即使随后改回原样或按 Esc），白白重合成；确认时写一次则改回原样 = 什么都没发生。
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommitted => mValueCommitted;
    public string Value { get => mTextInput.Text; set => mTextInput.Text = value; }

    // 掩码开关：'\0' 为 TextBox 默认（不掩码），非零字符即逐字符掩码显示。
    public bool IsPassword { set => mTextInput.PasswordChar = value ? '●' : '\0'; }

    public SingleLineTextController()
    {
        mTextInput = new TextInput()
        {
            Height = 28,
            AcceptsReturn = false,
            DiscardOnEscape = true,
        };

        Children.Add(mTextInput);
        mTextInput.EnterInput.Subscribe(() => mStartText = mTextInput.Text);
        mTextInput.EndInput.Subscribe(() =>
        {
            if (mTextInput.Text != mStartText)
                mValueChanged.Invoke();
            mValueCommitted.Invoke();
        });
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
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommitted = new();
    string mStartText = string.Empty;   // 开始编辑时的文字（多值时为空），确认时与它比对
}
