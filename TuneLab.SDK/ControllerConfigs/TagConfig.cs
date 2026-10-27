using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// tag 的一个候选：存储值与显示文本分离（同 ComboBoxItem 的口径）——Value 是落进数据的真实值（任意基础类型），
// DisplayText 是 chip 上的文字（为 null 时退化为 Value 的字面量）。分组与分隔线也与 ComboBoxItem 同形。
// 刻意不复用 ComboBoxItem：两者此刻长得一样，但「从一组值里选一个」与「从一组值里挑若干个」各有自己的演进方向，
// 绑成同一类型会让任何一边的改动牵连另一边。
// RS0027 不适用（同 AddableElement）：分组构造与主构造的首参类型不相交（string vs PropertyValue），调用绑定无歧义；
// 规则防的是给已发布签名追加可选参数，这里两种构造是一起新增的。
#pragma warning disable RS0027
public readonly struct TagItem(PropertyValue value, string? displayText = null) : IEquatable<TagItem>
{
    // 只读（不开 init）：外部初始化器改不出「分组却带值」这类矛盾态，唯一构造路径就是下面几个入口。
    public PropertyValue Value { get; } = value;
    public string? DisplayText { get; } = displayText;
    // 非 null = 分组项（本身不可选，展开为子菜单，DisplayText 为组名）；允许空列表 = 空分组。仅经分组构造函数设置。
    public IReadOnlyList<TagItem>? SubItems { get; }
    // 分隔线项（DisplayText 为可选居中标签）。internal：插件只造不读（经 InternalsVisibleTo 给宿主渲染器）。
    internal bool IsSeparator { get; private init; }

    // 分组构造：组名 + 子项。收 IEnumerable 便于直接传 .Select(...) 结果，内部物化。Value 取 Null（不参与选中/反查）。
    public TagItem(string displayText, IEnumerable<TagItem> subItems) : this(PropertyValue.Null, displayText)
    {
        SubItems = subItems as IReadOnlyList<TagItem> ?? subItems.ToList();
    }

    // 分隔线（可带居中标签）：分段用，顶层与分组子项里都能放。与 ComboBoxItem.Separator 同形。
    public static TagItem Separator(string? label = null) => new(PropertyValue.Null, label) { IsSeparator = true };

    // 各基础类型的隐式转换：集合表达式 ["a", "b"] / [1, 2] 逐元素转成 TagItem；数字一律落为 double（与 JSON number 一致）。
    public static implicit operator TagItem(PropertyValue value) => new(value);
    public static implicit operator TagItem(bool value) => new(PropertyValue.Create(value));
    public static implicit operator TagItem(string value) => new(PropertyValue.Create(value));
    public static implicit operator TagItem(int value) => new(PropertyValue.Create((double)value));
    public static implicit operator TagItem(long value) => new(PropertyValue.Create((double)value));
    public static implicit operator TagItem(float value) => new(PropertyValue.Create((double)value));
    public static implicit operator TagItem(double value) => new(PropertyValue.Create(value));

    public bool Equals(TagItem other)
        => Value.Equals(other.Value) && DisplayText == other.DisplayText && IsSeparator == other.IsSeparator && SubItemsEqual(SubItems, other.SubItems);
    public override bool Equals(object? obj) => obj is TagItem other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value, DisplayText);

    static bool SubItemsEqual(IReadOnlyList<TagItem>? a, IReadOnlyList<TagItem>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }
}
#pragma warning restore RS0027

// 标签组：从候选里挑若干个值，以 chip 横排、自动换行。值落为 PropertyArray（元素即所选候选的 Value）。
//
// 语义 = **有序、不重复**：新加的追加到末尾，用户可拖动调序（把关键的排前面），同一个值至多出现一次
//（+ 菜单隐藏已选的值）。与「元素为 ComboBoxConfig 的 ListConfig」数据形状相同、语义不同——那边允许重复、
// 元素可逐个改；作者按需要的语义二选一，换着用时数据照样读得通（这边读到重复值只显示第一个，下次编辑即去重写回）。
// 读到候选之外的值（旧数据 / 引擎删了选项）原样保留、显示为异常态，不替用户删。
//
// 构造函数全封，只走静态工厂 + 链式 With（与 ComboBoxConfig 同款 ABI 理由）。
public sealed class TagConfig : IValueConfig
{
    public IReadOnlyList<TagItem> Items { get; private set; } = [];
    // 默认所选（按此顺序）；缺省为空。
    public IReadOnlyList<PropertyValue> DefaultTags { get; private set; } = [];

    private TagConfig() { }
    TagConfig Clone() => (TagConfig)MemberwiseClone();

    // 构造即拷贝（[.. items]）：与调用方构造后对原 list 的改动无关。
    // 候选（叶子）的值与默认所选都必须是真实值：Null 不是值（宿主按"没有"处理），Multiple 只是多选读出时"各对象不一致"的
    // 标记、绝不入存储——两者都是写错了，当场报出来，免得插件运行起来才莫名其妙。分组 / 分隔线自身的 Null 不在此列。
    public static TagConfig Create(IReadOnlyList<TagItem> items)
    {
        EnsureLeafValues(items);
        return new() { Items = [.. items] };
    }

    public TagConfig WithDefault(IReadOnlyList<PropertyValue> tags)
    {
        foreach (var tag in tags)
            EnsureRealValue(tag, nameof(tags));
        var c = Clone();
        c.DefaultTags = [.. tags];
        return c;
    }

    static void EnsureLeafValues(IReadOnlyList<TagItem> items)
    {
        foreach (var item in items)
        {
            if (item.SubItems is { } subItems)
                EnsureLeafValues(subItems);
            else if (!item.IsSeparator)
                EnsureRealValue(item.Value, nameof(items));
        }
    }

    static void EnsureRealValue(PropertyValue value, string parameter)
    {
        if (value.IsNull() || value.IsMultiple())
            throw new ArgumentException("Tag values must be real values: Null means no value, and Multiple only marks differing values when reading a multi-selection.", parameter);
    }

    PropertyValue IValueConfig.DefaultValue => new PropertyArray(DefaultTags);
}
