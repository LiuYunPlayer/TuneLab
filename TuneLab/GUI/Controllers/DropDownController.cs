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

namespace TuneLab.GUI.Controllers;

internal class DropDownController : DockPanel
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommited => mValueCommited;
    public string Value { get => mValue; set { SetValue((uint)Options.IndexOf(value) < Options.Count ? value : mConfig.DefaultValue); Display(Value); } }

    public int SelectedIndex => mDropDown.SelectedIndex;
    public IReadOnlyList<string> Options => mConfig.Options;

    public DropDownController()
    {
        Children.Add(mDropDown);
        mDropDown.SelectionChanged += OnDropDownSelectionChanged;
    }

    public void SetConfig(EnumConfig config)
    {
        mConfig = config;
        mDropDown.Items.Clear();
        foreach (var option in config.Options)
        {
            mDropDown.Items.Add(option);
        }
        Display(config.DefaultValue);
    }

    public void Display(string value)
    {
        mValue = value;
        acceptSelectionChanged = false;
        int index = Options.IndexOf(value);
        if ((uint)index < Options.Count)
        {
            mDropDown.SelectedIndex = index;
        }
        else
        {
            mDropDown.PlaceholderText = value;
            mDropDown.SelectedIndex = -1;
        }
        acceptSelectionChanged = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mDropDown.Width = e.NewSize.Width - 48;
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

    DropDown mDropDown = new() { Height = 28, Margin = new(24, 12) };
    EnumConfig mConfig = new([]);
    string mValue = string.Empty;
}
