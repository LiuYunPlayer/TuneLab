using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Property;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;

using TuneLab.GUI.Controllers;

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
            mValueCommitted.Invoke();
            foreach (var kvp in mContentMap)
            {
                kvp.Value.ColorSet = IsChecked ? kvp.Key.CheckedColorSet : kvp.Key.UncheckedColorSet;
            }
            CorrectColor();
        } 
    }

    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommitted => mValueCommitted;
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

    public virtual void Display(bool value)
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
    readonly ActionEvent mValueCommitted = new();
    readonly Dictionary<ToggleContent, ButtonContent> mContentMap = [];
}
