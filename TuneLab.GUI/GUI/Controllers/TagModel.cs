using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.GUI.Controllers;

// tag 组（TagConfig）的值运算：读、去重、多成员并集、增删、整组挪动。与界面分开，便于单测。
//
// 语义 = 有序、不重复（见 TagConfig）。存值是数组，元素即 tag 值；读到的重复值只认第一个，
// 下次写回时自然去重（写的总是去重后的序列）。
internal static class TagModel
{
    // 一个成员此刻的 tag 序列：存值是数组 → 去重（保首次出现）；缺键 / 类型不符 → 声明默认值。
    // 缺键取默认 = 「默认值也是值」，与其余字段的 presence 语义一致：没改过就显示默认，一改就整段物化。
    public static List<PropertyValue> Read(IDataPropertyObject member, string key, PropertyValue defaultValue)
    {
        var value = member.GetValue(key, defaultValue);
        return value.ToArray(out var array) ? Dedupe(array) : [];
    }

    public static List<PropertyValue> Dedupe(IEnumerable<PropertyValue> values)
    {
        var result = new List<PropertyValue>();
        foreach (var value in values)
            if (!result.Contains(value))
                result.Add(value);
        return result;
    }

    // 多成员并集视图：顺序 = 首个成员的顺序，其余成员独有的依次追加在后；Count = 拥有它的成员数
    //（== 成员总数为「全有」，否则为「部分有」）。
    public static List<(PropertyValue Value, int Count)> Union(IReadOnlyList<IReadOnlyList<PropertyValue>> members)
    {
        var order = new List<PropertyValue>();
        foreach (var member in members)
            foreach (var value in member)
                if (!order.Contains(value))
                    order.Add(value);
        return order.Select(value => (value, members.Count(member => member.Contains(value)))).ToList();
    }

    // 各成员序列完全相同（同值同序）——只有这时「挪到第 i 个」对每个成员才是同一件事，才允许拖动调序。
    public static bool SameSequence(IReadOnlyList<IReadOnlyList<PropertyValue>> members)
    {
        for (int i = 1; i < members.Count; i++)
            if (!members[i].SequenceEqual(members[0]))
                return false;
        return true;
    }

    // 追加到末尾；已有则不动（不重复）。
    public static List<PropertyValue> Add(IReadOnlyList<PropertyValue> tags, PropertyValue value)
        => tags.Contains(value) ? [.. tags] : [.. tags, value];

    public static List<PropertyValue> Remove(IReadOnlyList<PropertyValue> tags, IReadOnlyCollection<PropertyValue> values)
        => tags.Where(tag => !values.Contains(tag)).ToList();

    // 整组挪动：把 moved 里的值（保持彼此原有次序）挪到 insertBefore 处——insertBefore 是原序列里的位置
    //（0..Count，Count = 挪到末尾），落点 = 该位置之前未被挪动的元素之后。
    public static List<PropertyValue> MoveBlock(IReadOnlyList<PropertyValue> tags, IReadOnlyCollection<PropertyValue> moved, int insertBefore)
    {
        var block = tags.Where(moved.Contains).ToList();
        var rest = tags.Where(tag => !moved.Contains(tag)).ToList();
        int at = tags.Take(insertBefore).Count(tag => !moved.Contains(tag));
        rest.InsertRange(at, block);
        return rest;
    }

    // 显示文本：命中候选取其 DisplayText（缺省回退值字面量）；不在候选里返回 null（调用方按异常态呈现原值字面量）。
    public static string? DisplayTextOf(IReadOnlyList<TagItem> items, PropertyValue value)
    {
        foreach (var leaf in items.Leaves())
            if (leaf.Value.Equals(value))
                return leaf.ShowText();
        return null;
    }

    public static string Literal(PropertyValue value) => value.ToString() ?? string.Empty;
}

// TagItem 的宿主侧呈现投影（同 ComboBoxItemExtensions）：「显示成什么文本」是渲染器的私事。
internal static class TagItemExtensions
{
    public static string ShowText(this TagItem item) => item.DisplayText ?? item.Value.ToString() ?? string.Empty;

    // 可选项（叶子）：深度优先展开分组、略去分隔线。凡「按值找候选」的地方都走这里。
    public static IEnumerable<TagItem> Leaves(this IEnumerable<TagItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
                continue;
            if (item.SubItems is not null)
            {
                foreach (var leaf in item.SubItems.Leaves())
                    yield return leaf;
            }
            else
            {
                yield return item;
            }
        }
    }
}
