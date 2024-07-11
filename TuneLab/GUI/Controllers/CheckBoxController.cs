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

internal class CheckBoxController : DockPanel, IMultipleValueController<bool>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommited => mValueCommited;

    public bool Value => mCheckBox.IsChecked;

    public CheckBoxController()
    {
        Children.Add(mCheckBox);

        mCheckBox.Switched.Subscribe(() =>
        {
            mValueWillChange.Invoke();
            mValueChanged.Invoke();
            mValueCommited.Invoke();
        });
    }

    public void Display(bool value)
    {
        mCheckBox.Background = Style.HIGH_LIGHT;
        mCheckBox.CheckIcon = Assets.Check;
        mCheckBox.Display(value);
    }

    public void DisplayNull()
    {
        mCheckBox.Background = Colors.Transparent;
        mCheckBox.CheckIcon = Assets.Hyphen;
        mCheckBox.Display(false);
    }

    public void DisplayMultiple()
    {
        mCheckBox.Background = Style.HIGH_LIGHT;
        mCheckBox.CheckIcon = Assets.Hyphen;
        mCheckBox.Display(true);
    }

    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommited = new();

    readonly CheckBox mCheckBox = new();
}
