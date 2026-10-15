using System.Collections.Generic;
using TuneLab.SDK;

namespace TuneLab.GUI.Controllers;

// ComboBoxItem 的宿主侧呈现投影：SDK 面只承载数据（Value / DisplayText / SubItems），
// 「显示成什么文本」是渲染器（本 GUI）的私事，故做成宿主扩展而非 SDK 实例成员。
internal static class ComboBoxItemExtensions
{
    // 显示文本：优先 DisplayText，缺省回退到值的字面量。
    public static string ShowText(this ComboBoxItem item) => item.DisplayText ?? item.Value.ToString() ?? string.Empty;

    // 可选项（叶子）：深度优先展开分组、略去分隔线，序与下拉菜单展平的叶子序一致。
    // 凡「按值找选项」的地方都走这里——只扫顶层会让分组里的选项既看不见也写不进。
    public static IEnumerable<ComboBoxItem> Leaves(this IEnumerable<ComboBoxItem> items)
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
