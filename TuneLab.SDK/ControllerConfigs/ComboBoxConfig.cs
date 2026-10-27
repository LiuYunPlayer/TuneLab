using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

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
