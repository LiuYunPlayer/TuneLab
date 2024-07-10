using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;
using TuneLab.GUI.Components;
using TuneLab.Base.Utils;
using System.Threading;

namespace TuneLab.GUI.Controllers;

internal class ComboBoxController : DropDown, IValueController<string>, IValueController<int>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommited => mValueCommited;
    public string Value { get => mValue; set { SetValue((uint)Options.IndexOf(value) < Options.Count ? value : mConfig.DefaultValue); Display(Value); } }
    public int Index { get => Options.IndexOf(Value); }
    int IValueController<int>.Value => Index;
    public IReadOnlyList<string> Options => mConfig.Options;

    public ComboBoxController()
    {
        Height = 28;
        SelectionChanged += OnDropDownSelectionChanged;
    }

    public void SetConfig(EnumConfig config)
    {
        mConfig = config;
        Items.Clear();
        foreach (var option in config.Options)
        {
            Items.Add(option);
        }
        Display(config.DefaultValue);
    }

    public void Display(int value)
    {
        if ((uint)value >= Options.Count)
        {
            value = -1;
            PlaceholderText = "-";
        }

        acceptSelectionChanged = false;
        SelectedIndex = value;
        acceptSelectionChanged = true;
    }

    public void Display(string value)
    {
        mValue = value;
        acceptSelectionChanged = false;
        int index = Options.IndexOf(mValue);
        if ((uint)index < Options.Count)
        {
            SelectedIndex = index;
        }
        else
        {
            PlaceholderText = mValue;
            SelectedIndex = -1;
        }
        acceptSelectionChanged = true;
    }

    public void DisplayNull()
    {
        mValue = string.Empty;
        acceptSelectionChanged = false;
        PlaceholderText = "(Null)";
        SelectedIndex = -1;
        acceptSelectionChanged = true;
    }

    public void DisplayMultiple()
    {
        mValue = string.Empty;
        acceptSelectionChanged = false;
        PlaceholderText = "(Multiple)";
        SelectedIndex = -1;
        acceptSelectionChanged = true;
    }

    bool acceptSelectionChanged = true;
    void OnDropDownSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!acceptSelectionChanged)
            return;

        SetValue((uint)SelectedIndex < Options.Count ? Options[SelectedIndex] : mConfig.DefaultValue);
    }

    void SetValue(string value)
    {
        if (value == mValue)
            return;

        mValueWillChange.Invoke();
        mValue = value;
        mValueChanged.Invoke();
        mValueCommited.Invoke();
    }

    ActionEvent mValueWillChange = new();
    ActionEvent mValueChanged = new();
    ActionEvent mValueCommited = new();

    EnumConfig mConfig = new([]);
    string mValue = string.Empty;
}
