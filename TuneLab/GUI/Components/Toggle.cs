using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;

namespace TuneLab.GUI.Components;

internal class ToggleContent
{
    public required IItem Item { get; set; }
    public ColorSet UncheckedColorSet;
    public ColorSet CheckedColorSet;
}

internal class Toggle : Button, IValueController<bool>
{
    public event Func<bool>? AllowSwitch;
    public IActionEvent Switched => mSwitched;

    public bool IsChecked { get; private set; }

    public IActionEvent ValueCommited => Switched;
    public bool Value => IsChecked;

    public Toggle()
    {
        Pressed += () =>
        {
            if (AllowSwitch != null && !AllowSwitch())
                return;

            IsChecked = !IsChecked;
            mSwitched.Invoke();
            foreach (var kvp in mContentMap)
            {
                kvp.Value.ColorSet = IsChecked ? kvp.Key.CheckedColorSet : kvp.Key.UncheckedColorSet;
            }
        };
    }

    public Toggle AddContent(ToggleContent content)
    {
        var buttonContent = new ButtonContent() { Item = content.Item, ColorSet = IsChecked ? content.CheckedColorSet : content.UncheckedColorSet };
        mContentMap.Add(content, buttonContent);
        AddContent(buttonContent);
        return this;
    }

    public void Display(bool value)
    {
        IsChecked = value;
        foreach (var kvp in mContentMap)
        {
            kvp.Value.ColorSet = IsChecked ? kvp.Key.CheckedColorSet : kvp.Key.UncheckedColorSet;
        }
        CorrectColor();
    }

    readonly ActionEvent mSwitched = new();
    Dictionary<ToggleContent, ButtonContent> mContentMap = new();
}
