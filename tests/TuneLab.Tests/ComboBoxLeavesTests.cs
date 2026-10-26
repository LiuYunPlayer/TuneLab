using TuneLab.Foundation;
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

    // ===== 默认值：必须是一个能选的值（宿主按默认值判"未设"并定类型）=====

    // 首项是分组时不能取 Items[0]（分组的值是 Null）：取第一个能选的叶子。
    [Fact]
    public void Default_IsFirstSelectableLeaf_WhenFirstItemIsAGroup()
        => Assert.Equal("a", ComboBoxConfig.Create([new ComboBoxItem("Group", ["a", "b"]), "c"]).DefaultOption.Value.ToString());

    // 指定的默认不能选（分隔线 / Null 值）：回落到第一个能选的叶子。
    [Fact]
    public void Default_FallsBackWhenTheGivenDefaultIsNotSelectable()
    {
        Assert.Equal("top", Grouped().WithDefault(ComboBoxItem.Separator()).DefaultOption.Value.ToString());
        Assert.Equal("top", Grouped().WithDefault(PropertyValue.Null).DefaultOption.Value.ToString());
        Assert.Equal("a", Grouped().WithDefault("a").DefaultOption.Value.ToString());
    }

    // 一个能选的都没有（选项动态为空）：默认就是 Null——本就无可选，是正常状态。
    [Fact]
    public void Default_IsNull_WhenNothingIsSelectable()
    {
        Assert.True(ComboBoxConfig.Create().DefaultOption.Value.IsNull());
        Assert.True(ComboBoxConfig.Create([new ComboBoxItem("Empty group", [])]).DefaultOption.Value.IsNull());
    }

    // 值为 Multiple 的项不是值：不列进菜单（选了也存不进去）。
    [Fact]
    public void Leaves_SkipItemsWhoseValueIsMultiple()
        => Assert.Equal(["x"], ComboBoxConfig.Create([PropertyValue.Multiple, "x"]).Items.Leaves().Select(i => i.Value.ToString()).ToList());
}
