using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;
using CheckBox = TuneLab.GUI.Components.CheckBox;

namespace TuneLab.GUI.Controllers;

internal class CheckBoxController : DockPanel, IValueController<bool>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommited => mValueCommited;

    public bool Value => mCheckBox.IsChecked;

    public CheckBoxController()
    {
        Children.Add(mCheckBox);

        mCheckBox.Switched += () =>
        {
            mValueWillChange.Invoke();
            mValueChanged.Invoke();
            mValueCommited.Invoke();
        };
    }

    public void Display(object? value)
    {
        mCheckBox.Background = value == null ? Colors.Transparent : Style.HIGH_LIGHT;
        if (value is bool boolValue)
        {
            if (boolValue)
                mCheckBox.CheckIcon = Assets.Check;
            mCheckBox.Display(boolValue);
        }
        else
        {
            mCheckBox.CheckIcon = Assets.Hyphen;
            mCheckBox.Display(true);
        }
    }

    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommited = new();

    readonly CheckBox mCheckBox = new();
}
