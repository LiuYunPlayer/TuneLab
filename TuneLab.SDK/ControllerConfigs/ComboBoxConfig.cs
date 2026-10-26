using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 下拉选项：存储值与显示文本分离。Value 是落进数据的真实值（任意基础类型，统一装进单一 PropertyValue box），
// DisplayText 是界面呈现文本（为 null 时退化为 Value 的字面量）。这样可「界面显示中文 / 底层存枚举值」。
// 各基础类型的隐式转换让插件直接写裸值——集合表达式 ["a","b"] / [1,2,3] / 混合 [1, "x", new ComboBoxItem(2,"d")]
// 都逐元素隐式转成 ComboBoxItem、匹配下方唯一的 list 构造器，无需手动构造 PropertyValue；数字类型一律落为 double
//（与 JSON number 一致）。注意：不另设 IReadOnlyList<基础类型> 的便捷构造器——否则集合表达式字面量会与本 list 构造器
// 在「string-list vs ComboBoxItem-list」间产生重载二义（C# 12 判不出更优、直接报错）。已建好的 typed 变量（如
// List<string>）不会逐元素隐式转换，调用方就地 .Select(o => (ComboBoxItem)o).ToList() 即可。
public readonly struct ComboBoxItem(PropertyValue value, string? displayText = null) : IEquatable<ComboBoxItem>
{
    public PropertyValue Value { get; init; } = value;
    public string? DisplayText { get; init; } = displayText;
    // 非 null = 分组项（本身不可选，展开为二级子菜单）；子项各自带值/显示文本、可再嵌套。仅经分组构造函数设置。
    // 与 Value 语义互斥（叶子有值无子项、分组有子项无值），故不混入叶子主构造函数的可选参数。
    // 允许空列表：表示"分组存在但暂无子项"（如引擎已加载但无音源），渲染为空子菜单、不可选。
    public IReadOnlyList<ComboBoxItem>? SubItems { get; init; } = null;
    // 分隔线项：不可选、不计入选中；DisplayText 为可选居中标签（null/空 = 纯线）。
    // private init：唯一构造路径是 Separator() 工厂，外部初始化器不能把普通项标成分隔线（避免矛盾态）。
    // internal：插件只造不读（经 InternalsVisibleTo 暴露给宿主渲染器）。
    internal bool IsSeparator { get; private init; } = false;

    // 分组构造：显示文本 + 子项（空列表 = 空分组）；本身不可选，Value 取 Null（不参与选中/反查）。
    // 收 IEnumerable 便于调用方直接传 .Select(...) 结果，内部物化为 IReadOnlyList（需多次索引/读取，不可留惰性序列）。
    public ComboBoxItem(string displayText, IEnumerable<ComboBoxItem> subItems) : this(PropertyValue.Null, displayText)
    {
        SubItems = subItems as IReadOnlyList<ComboBoxItem> ?? subItems.ToList();
    }

    // 分隔线（可带居中标签）：分段用。任意层级可用——顶层与分组的子项里都能放（分组构造收的就是 ComboBoxItem 列表）；
    // 顶层也可走 ComboBoxConfig.AppendSeparator 便捷写法。与 AddableElement/AddableKey.Separator 同形。
    public static ComboBoxItem Separator(string? label = null) => new(PropertyValue.Null, label) { IsSeparator = true };

    public static implicit operator ComboBoxItem(PropertyValue value) => new(value);
    public static implicit operator ComboBoxItem(bool value) => new(PropertyValue.Create(value));
    public static implicit operator ComboBoxItem(string value) => new(PropertyValue.Create(value));

    public static implicit operator ComboBoxItem(sbyte value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(byte value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(short value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(ushort value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(int value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(uint value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(long value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(ulong value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(nint value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(nuint value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(float value) => new(PropertyValue.Create((double)value));
    public static implicit operator ComboBoxItem(double value) => new(PropertyValue.Create(value));
    public static implicit operator ComboBoxItem(decimal value) => new(PropertyValue.Create((double)value));

    public readonly bool Equals(ComboBoxItem other)
        => Value.Equals(other.Value) && DisplayText == other.DisplayText && IsSeparator == other.IsSeparator && SubItemsEqual(SubItems, other.SubItems);
    public override readonly bool Equals(object? obj) => obj is ComboBoxItem other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(Value, DisplayText);

    static bool SubItemsEqual(IReadOnlyList<ComboBoxItem>? a, IReadOnlyList<ComboBoxItem>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }
}

// 原 EnumConfig（按 UI 控件命名）。选项与默认值都用 ComboBoxItem（值/显示分离），默认值是「值」而非「索引」。
// 构造函数全封，只走静态工厂 + 链式 Append/With（与 SliderConfig 同款 ABI 理由）。
// 链式构造隐藏列表拼装：Create().Append(...).AppendSeparator().Append(...)；分组内的分隔线用 ComboBoxItem.Separator 放进子项列表。
public sealed class ComboBoxConfig : IValueConfig
{
    public IReadOnlyList<ComboBoxItem> Items { get; private set; } = [];

    // 默认值 = WithDefault 指定的项；未指定、或指定的不是一个能选的值（分隔线 / 分组 / 值为 Null 或 Multiple 的项）时，
    // 取第一个能选的叶子（深度优先、跳过分隔线与分组）——不能取 Items[0]：首项是分组时默认值会成 Null，而宿主按默认值
    // 判"未设"并定类型。一个能选的都没有（如选项动态为空）时为 default（值为 Null）：本就无可选，这是正常状态。
    ComboBoxItem? mDefaultOption;
    public ComboBoxItem DefaultOption
    {
        get => mDefaultOption is { } option && IsSelectable(option) ? option : FirstSelectable(Items) ?? default;
        private set => mDefaultOption = value;
    }

    static bool IsSelectable(ComboBoxItem item)
        => !item.IsSeparator && item.SubItems == null && !item.Value.IsNull() && !item.Value.IsMultiple();

    static ComboBoxItem? FirstSelectable(IReadOnlyList<ComboBoxItem> items)
    {
        foreach (var item in items)
        {
            if (item.SubItems is { } subItems)
            {
                if (FirstSelectable(subItems) is { } leaf)
                    return leaf;
            }
            else if (IsSelectable(item))
            {
                return item;
            }
        }
        return null;
    }

    private ComboBoxConfig() { }
    ComboBoxConfig Clone() => (ComboBoxConfig)MemberwiseClone();

    // 空起手，配合 Append 链式拼装。
    public static ComboBoxConfig Create() => new();
    // 一次给全列表的便捷路。构造即拷贝（[.. options]）：与调用方构造后对原 list 的改动无关，
    // 且与 Append 链路的 [.. Items, …] 写法一致（Items 恒为 config 自持的独立数组）。
    public static ComboBoxConfig Create(IReadOnlyList<ComboBoxItem> options) => new() { Items = [.. options] };

    public ComboBoxConfig Append(ComboBoxItem option) { var c = Clone(); c.Items = [.. Items, option]; return c; }
    public ComboBoxConfig Append(IEnumerable<ComboBoxItem> options) { var c = Clone(); c.Items = [.. Items, .. options]; return c; }
    // 追加分隔线（可带居中标签）= Append(ComboBoxItem.Separator(label))。
    public ComboBoxConfig AppendSeparator(string? label = null) => Append(ComboBoxItem.Separator(label));

    public ComboBoxConfig WithDefault(ComboBoxItem option) { var c = Clone(); c.mDefaultOption = option; return c; }

    PropertyValue IValueConfig.DefaultValue => DefaultOption.Value;
}
