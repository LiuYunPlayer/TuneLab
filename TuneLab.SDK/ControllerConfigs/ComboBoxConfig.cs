using System;
using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

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
    // 非 null = 分组项（本身不可选，展开为二级子菜单）；子项各自带值/显示文本、可再嵌套。仅经分组构造函数设置。
    // 与 Value 语义互斥（叶子有值无子项、分组有子项无值），故不混入叶子主构造函数的可选参数。
    // 允许空列表：表示"分组存在但暂无子项"（如引擎已加载但无音源），渲染为空子菜单、不可选。
    public IReadOnlyList<ComboBoxOption>? SubOptions { get; set; } = null;
    public readonly bool IsGroup => SubOptions is not null;
    // 分隔线项：不可选、不计入选中；DisplayText 为可选居中标签（null/空 = 纯线）。
    public bool IsSeparator { get; init; } = false;

    // 分组构造：显示文本 + 子项（空列表 = 空分组）；本身不可选，Value 取 Null（不参与选中/反查）。
    public ComboBoxOption(string displayText, IReadOnlyList<ComboBoxOption> subOptions) : this(PropertyValue.Null, displayText)
    {
        SubOptions = subOptions;
    }

    // 分隔线（可带居中标签）：分段用。
    public static ComboBoxOption Separator(string? label = null) => new(PropertyValue.Null, label) { IsSeparator = true };

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

    public readonly bool Equals(ComboBoxOption other)
        => Value.Equals(other.Value) && DisplayText == other.DisplayText && IsSeparator == other.IsSeparator && SubOptionsEqual(SubOptions, other.SubOptions);
    public override readonly bool Equals(object? obj) => obj is ComboBoxOption other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(Value, DisplayText);

    static bool SubOptionsEqual(IReadOnlyList<ComboBoxOption>? a, IReadOnlyList<ComboBoxOption>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }
}

// 原 EnumConfig（按 UI 控件命名）。选项与默认值都用 ComboBoxOption（值/显示分离），默认值是「值」而非「索引」。
public class ComboBoxConfig : IValueConfig
{
    public required IReadOnlyList<ComboBoxOption> Options { get; init; }

    // 默认值缺省取首项（空列表给 default）——未显式设 DefaultOption 时由 getter 懒回退到 Options[0]。
    ComboBoxOption? mDefaultOption;
    public ComboBoxOption DefaultOption
    {
        get => mDefaultOption ?? (Options.Count == 0 ? default : Options[0]);
        init => mDefaultOption = value;
    }

    PropertyValue IValueConfig.DefaultValue => DefaultOption.Value;
}
