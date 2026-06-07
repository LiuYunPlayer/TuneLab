using System;
using System.Collections.Generic;
using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 下拉选项：存储值与显示文本分离。Value 是落进数据的真实值（任意基础类型，统一装进单一 PropertyValue box），
// DisplayText 是界面呈现文本（为 null 时退化为 Value 的字面量）。这样可「界面显示中文 / 底层存枚举值」。
// 各基础类型的隐式转换让插件直接写裸值——集合表达式 ["a","b"] / [1,2,3] / 混合 [1, "x", new ComboBoxOption(2,"d")]
// 都逐元素隐式转成 ComboBoxOption、匹配下方唯一的 list 构造器，无需手动构造 PropertyValue；数字类型一律落为 double
//（与 JSON number 一致）。注意：不另设 IReadOnlyList<基础类型> 的便捷构造器——否则集合表达式字面量会与本 list 构造器
// 在「string-list vs ComboBoxOption-list」间产生重载二义（C# 12 判不出更优、直接报错）。已建好的 typed 变量（如
// List<string>）不会逐元素隐式转换，调用方就地 .Select(o => (ComboBoxOption)o).ToList() 即可。
public struct ComboBoxOption(PropertyValue value, string? displayText = null) : IEquatable<ComboBoxOption>
{
    public PropertyValue Value { get; set; } = value;
    public string? DisplayText { get; set; } = displayText;

    // 显示文本：优先 DisplayText，缺省回退到值的字面量。
    public readonly string ShowText() => DisplayText ?? Value.ToString() ?? string.Empty;

    public static implicit operator ComboBoxOption(PropertyValue value) => new(value);
    public static implicit operator ComboBoxOption(bool value) => new(PropertyValue.Create(value));
    public static implicit operator ComboBoxOption(string value) => new(PropertyValue.Create(value));

    public static implicit operator ComboBoxOption(sbyte value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(byte value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(short value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(ushort value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(int value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(uint value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(long value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(ulong value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(nint value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(nuint value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(float value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxOption(double value) => new(PropertyValue.Create(value));
    public static implicit operator ComboBoxOption(decimal value) => new(PropertyValue.Create((double)value));

    public readonly bool Equals(ComboBoxOption other) => Value.Equals(other.Value) && DisplayText == other.DisplayText;
    public override readonly bool Equals(object? obj) => obj is ComboBoxOption other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(Value, DisplayText);
}

// 原 EnumConfig（按 UI 控件命名）。选项与默认值都用 ComboBoxOption（值/显示分离），默认值是「值」而非「索引」。
public class ComboBoxConfig(IReadOnlyList<ComboBoxOption> options, ComboBoxOption defaultValue) : IValueConfig
{
    public ComboBoxOption DefaultOption { get; set; } = defaultValue;
    public IReadOnlyList<ComboBoxOption> Options { get; set; } = options;
    PropertyValue IValueConfig.DefaultValue => DefaultOption.Value;

    public ComboBoxConfig() : this(Array.Empty<ComboBoxOption>(), default) { }
    // 默认值缺省取首项（空列表给 default）。
    public ComboBoxConfig(IReadOnlyList<ComboBoxOption> options) : this(options, options.Count == 0 ? default : options[0]) { }
}
