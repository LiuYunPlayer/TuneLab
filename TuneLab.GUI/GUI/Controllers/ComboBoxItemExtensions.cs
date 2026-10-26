using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.GUI.Controllers;

// ComboBoxItem 的宿主侧呈现投影：SDK 面只承载数据（Value / DisplayText / SubItems），
// 「显示成什么文本」是渲染器（本 GUI）的私事，故做成宿主扩展而非 SDK 实例成员。
internal static class ComboBoxItemExtensions
{
    // 叶子是否列进菜单：值为 Multiple 的不列（插件写错了的选项，选了也存不进去）。分组 / 分隔线不经此判。
    public static bool IsListed(this ComboBoxItem item) => item.SubItems is not null || !item.Value.IsMultiple();

    // 显示文本：优先 DisplayText，缺省回退到值的字面量。
    public static string ShowText(this ComboBoxItem item) => item.DisplayText ?? item.Value.ToString() ?? string.Empty;

    // 可选项（叶子）：深度优先展开分组、略去分隔线，序与下拉菜单展平的叶子序一致。
    // 凡「按值找选项」的地方都走这里——只扫顶层会让分组里的选项既看不见也写不进。
    // 值为 Multiple 的项也略去（菜单同样不建，见 IsListed）：它不是值、存储层拒收，选中即写入失败。
    public static IEnumerable<ComboBoxItem> Leaves(this IEnumerable<ComboBoxItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator || !item.IsListed())
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
