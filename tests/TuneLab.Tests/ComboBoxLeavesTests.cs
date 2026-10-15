using System.Linq;
using TuneLab.Commands;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 分组下拉的选项 = 展开分组后的叶子。命令面（agent/CLI）按值描述与写入下拉时只扫顶层，
// 曾让分组里的选项既列不出来也写不进去——这里钉住「展开分组、略去分隔线、保持菜单序」。
public class ComboBoxLeavesTests
{
    static ComboBoxConfig Grouped() => ComboBoxConfig.Create(
        [
            "top",
            new ComboBoxItem("Group", ["a", ComboBoxItem.Separator("in group"), new ComboBoxItem("Nested", ["b"])]),
        ])
        .AppendSeparator("tail")
        .Append("last");

    [Fact]
    public void Leaves_ExpandGroupsDepthFirstAndSkipSeparators()
    {
        var leaves = Grouped().Items.Leaves().Select(i => i.Value.ToString()).ToList();

        Assert.Equal(["top", "a", "b", "last"], leaves);
    }

    [Fact]
    public void Describe_ListsOptionsInsideGroups()
    {
        var text = ConfigText.Describe(Grouped());

        Assert.Contains("\"a\"", text);
        Assert.Contains("\"b\"", text);
        Assert.DoesNotContain("Group", text);
    }
}
