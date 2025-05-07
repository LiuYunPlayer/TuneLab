using Avalonia.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.Utils;
using TuneLab.GUI.Components;

namespace TuneLab.GUI.Controllers;

internal class ComboBoxController : DropDown, IDataValueController<ReadOnlyPrimitiveValue>
    , IDataValueController<string>
    , IDataValueController<double>
    , IDataValueController<int>
    , IDataValueController<bool>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommited => mValueCommited;
    public ReadOnlyPrimitiveValue Value { get => mValue; set { SetValue(value); } }

    string IValueController<string>.Value => Value.AsString();
    double IValueController<double>.Value => Value.AsNumber();
    int IValueController<int>.Value => Value.AsNumber().Round();
    bool IValueController<bool>.Value => Value.AsBoolean();

    public ComboBoxController()
    {
        Height = 28;
        SelectionChanged += OnDropDownSelectionChanged;
    }

    public void SetConfig(ComboBoxConfig config)
    {
        mConfig = config;
        Items.Clear();
        foreach (var option in config.Options)
        {
            Items.Add(option.ShowText());
        }
        Display(config.DefaultValue.Value);
    }

    public void DisplayIndex(int index)
    {
        if ((uint)index >= mConfig.Options.Count)
        {
            index = -1;
            PlaceholderText = "-";
        }

        mValue = mConfig.Options[index].Value;
        acceptSelectionChanged = false;
        SelectedIndex = index;
        acceptSelectionChanged = true;
    }

    public void Display(ReadOnlyPrimitiveValue value)
    {
        mValue = value;
        acceptSelectionChanged = false;
        int index = mConfig.Options.Convert(option => option.Value).IndexOf(mValue);
        if ((uint)index < mConfig.Options.Count)
        {
            SelectedIndex = index;
        }
        else
        {
            PlaceholderText = value == mConfig.DefaultValue.Value ? mConfig.DefaultValue.ShowText() : value.ToString();
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

        SetValue((uint)SelectedIndex < mConfig.Options.Count ? mConfig.Options[SelectedIndex].Value : mConfig.DefaultValue.Value);
    }

    void SetValue(ReadOnlyPrimitiveValue value)
    {
        if (value == mValue)
            return;

        mValueWillChange.Invoke();
        Display(mValue);
        mValueChanged.Invoke();
        mValueCommited.Invoke();
    }

    public void Display(string value) => Display((ReadOnlyPrimitiveValue)value);
    public void Display(double value) => Display((ReadOnlyPrimitiveValue)value);
    public void Display(int value) => Display((ReadOnlyPrimitiveValue)value);
    public void Display(bool value) => Display((ReadOnlyPrimitiveValue)value);

    ActionEvent mValueWillChange = new();
    ActionEvent mValueChanged = new();
    ActionEvent mValueCommited = new();

    ComboBoxConfig mConfig = DefaultConfig;
    ReadOnlyPrimitiveValue mValue;

    static readonly ComboBoxConfig DefaultConfig = new() { Options = [] };
}

public static class ComboBoxOptionExtensions
{
    public static string ShowText(this ComboBoxOption option) => option.DisplayText ?? option.Value.ToString();
}
