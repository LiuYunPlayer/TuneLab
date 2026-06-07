using Avalonia.Controls;
using System;
using System.Collections.Generic;
using TuneLab.Foundation.Event;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.GUI.Components;

namespace TuneLab.GUI.Controllers;

// 值模型为单一 PropertyValue box：option 值可为任意基础类型，显示用 option.ShowText()（DisplayText 缺省回退值字面量）。
// 数据驱动的 Display(value) 按值在 options 里反查下标以高亮对应项；用户选择则反向把该项的值发为数据改动。
internal class ComboBoxController : DropDown, IDataValueController<PropertyValue>, IValueController<string>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommitted => mValueCommitted;
    public PropertyValue Value => mValue;
    // 当前选中位置（非数据绑定用：如 FunctionBar 量化下拉按 Index 联动业务值）。
    public int Index => SelectedIndex;

    // string 外观：供按字符串绑定的场景（Settings 的语言/驱动下拉、Select(int.Parse) 桥到 int 设置）。
    // 读取把当前值字面量化，显示把字符串包成 PropertyValue 走统一 Display。
    string IValueController<string>.Value => mValue.ToString() ?? string.Empty;
    void IValueController<string>.Display(string value) => Display(PropertyValue.Create(value));

    public ComboBoxController()
    {
        Height = 28;
        SelectionChanged += OnDropDownSelectionChanged;
    }

    public void SetConfig(ComboBoxConfig config)
    {
        mConfig = config;
        // 改 Items 是内部状态同步，必须屏蔽由此引发的 SelectionChanged——否则它会走 OnDropDownSelectionChanged
        // → SetValue 这条「用户改动」路径，把数据值改掉（reconcile 高频 SetConfig 时尤为明显）。
        acceptSelectionChanged = false;
        Items.Clear();
        foreach (var option in config.Options)
        {
            Items.Add(option.ShowText());
        }
        acceptSelectionChanged = true;
        Display(config.DefaultOption.Value);
    }

    public void Display(PropertyValue value)
    {
        mValue = value;
        acceptSelectionChanged = false;
        int index = IndexOfValue(value);
        if ((uint)index < (uint)mConfig.Options.Count)
        {
            SelectedIndex = index;
        }
        else
        {
            // 值不在选项内：显默认项文本（命中默认值时）或值字面量作占位，不真正选中任何项。
            PlaceholderText = value.Equals(mConfig.DefaultOption.Value) ? mConfig.DefaultOption.ShowText() : (value.ToString() ?? string.Empty);
            SelectedIndex = -1;
        }
        acceptSelectionChanged = true;
    }

    public void DisplayNull()
    {
        mValue = PropertyValue.Invalid;
        acceptSelectionChanged = false;
        PlaceholderText = string.Empty;
        SelectedIndex = -1;
        acceptSelectionChanged = true;
    }

    public void DisplayMultiple()
    {
        mValue = PropertyValue.Invalid;
        acceptSelectionChanged = false;
        PlaceholderText = "(Multiple)";
        SelectedIndex = -1;
        acceptSelectionChanged = true;
    }

    // 按值在 options 里反查下标（PropertyValue 是 struct，不能用 where T:class 约束的 IndexOf 扩展，故手写）。
    int IndexOfValue(PropertyValue value)
    {
        for (int i = 0; i < mConfig.Options.Count; i++)
        {
            if (mConfig.Options[i].Value.Equals(value))
                return i;
        }
        return -1;
    }

    bool acceptSelectionChanged = true;
    void OnDropDownSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!acceptSelectionChanged)
            return;

        SetValue((uint)SelectedIndex < (uint)mConfig.Options.Count ? mConfig.Options[SelectedIndex].Value : mConfig.DefaultOption.Value);
    }

    void SetValue(PropertyValue value)
    {
        if (value.Equals(mValue))
            return;

        mValueWillChange.Invoke();
        mValue = value;
        mValueChanged.Invoke();
        mValueCommitted.Invoke();
    }

    ActionEvent mValueWillChange = new();
    ActionEvent mValueChanged = new();
    ActionEvent mValueCommitted = new();

    ComboBoxConfig mConfig = new();
    PropertyValue mValue = PropertyValue.Invalid;
}
