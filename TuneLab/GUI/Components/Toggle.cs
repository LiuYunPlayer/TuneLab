using System;
using System.Collections.Generic;
using TuneLab.Base.Properties;
using TuneLab.Foundation.Event;

namespace TuneLab.GUI.Components;

internal class ToggleContent
{
    public required IItem Item { get; set; }
    public ColorSet UncheckedColorSet;
    public ColorSet CheckedColorSet;
    public ColorSet ColorSet { set { CheckedColorSet = value; UncheckedColorSet = value; } }
}

internal class Toggle : Button, IDataValueController<bool>
{
    public event Func<bool>? AllowSwitch;
    public IActionEvent Switched => mValueChanged;
    public bool IsChecked
    {
        get => mIsChecked;
        set
        {
            if (AllowSwitch != null && !AllowSwitch())
                return;

            if (IsChecked == value)
                return;

            mValueWillChange.Invoke();
            mIsChecked = value;
            mValueChanged.Invoke();
            mValueCommited.Invoke();
            foreach (var kvp in mContentMap)
            {
                kvp.Value.ColorSet = IsChecked ? kvp.Key.CheckedColorSet : kvp.Key.UncheckedColorSet;
            }
            CorrectColor();
        }
    }

    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommited => mValueCommited;
    public bool Value => IsChecked;

    public Toggle()
    {
        Pressed += () => IsChecked = !IsChecked;
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
        mIsChecked = value;
        foreach (var kvp in mContentMap)
        {
            kvp.Value.ColorSet = IsChecked ? kvp.Key.CheckedColorSet : kvp.Key.UncheckedColorSet;
        }
        CorrectColor();
    }

    bool mIsChecked = false;
    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommited = new();
    readonly Dictionary<ToggleContent, ButtonContent> mContentMap = [];
}
