using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TuneLab.Commands;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// tag 组（TagConfig）：有序、不重复。钉住值运算（读 / 去重 / 多成员并集 / 增删 / 整组挪动）、
// + 菜单过滤、命令面的描述与校验、片段预设的整值写入。界面交互由手测用例覆盖（tests/TAG-CONFIG-TEST-CASES.md）。
public class TagConfigTests
{
    static PropertyValue V(string s) => PropertyValue.Create(s);
    static List<PropertyValue> L(params string[] values) => values.Select(V).ToList();
    static PropertyArray A(params string[] values) => new(values.Select(V).ToList());

    // ===== 读：缺键取默认、重复只认第一个 =====

    [Fact]
    public void ReadFallsBackToDefaultWhenAbsent()
    {
        var node = new DataPropertyObject();
        Assert.Equal(L("a", "b"), TagModel.Read(node, "tags", A("a", "b")));
    }

    [Fact]
    public void ReadKeepsStoredOrderAndDropsDuplicates()
    {
        var node = new DataPropertyObject();
        node.SetValue("tags", A("c", "a", "c", "b", "a"));
        Assert.Equal(L("c", "a", "b"), TagModel.Read(node, "tags", A()));
    }

    // 存值不是数组（非法数据，如旧版本留下的对象）：按缺省处理，显示声明的默认值，而不是显示成"一个都没选"。
    [Fact]
    public void ReadFallsBackToDefaultWhenStoredValueIsNotAnArray()
    {
        var node = new DataPropertyObject();
        node.SetValue("tags", PropertyValue.Create(PropertyObject.Empty));
        Assert.Equal(L("a"), TagModel.Read(node, "tags", A("a")));
    }

    // ===== 增删挪 =====

    [Fact]
    public void AddAppendsAtTheEndAndNeverDuplicates()
    {
        Assert.Equal(L("b", "a", "c"), TagModel.Add(L("b", "a"), V("c")));
        Assert.Equal(L("b", "a"), TagModel.Add(L("b", "a"), V("b")));
    }

    [Fact]
    public void RemoveDropsEveryGivenValue()
        => Assert.Equal(L("b"), TagModel.Remove(L("a", "b", "c"), [V("a"), V("c")]));

    [Theory]
    [InlineData(0, new[] { "c", "a", "b", "d" })]   // 挪到最前
    [InlineData(4, new[] { "a", "b", "d", "c" })]   // 挪到末尾
    [InlineData(1, new[] { "a", "c", "b", "d" })]   // 挪到 b 之前
    [InlineData(2, new[] { "a", "b", "c", "d" })]   // 落在原处
    public void MoveBlockPlacesASingleTag(int insertBefore, string[] expected)
        => Assert.Equal(L(expected), TagModel.MoveBlock(L("a", "b", "c", "d"), [V("c")], insertBefore));

    [Fact]
    public void MoveBlockKeepsTheGroupInItsOwnOrder()
        // 整组 {a, c} 挪到 d 之后：组内保持 a 在 c 前。
        => Assert.Equal(L("b", "d", "a", "c"), TagModel.MoveBlock(L("a", "b", "c", "d"), [V("c"), V("a")], 4));

    // ===== 多选并集 =====

    [Fact]
    public void UnionFollowsTheFirstMemberThenAppendsTheRest()
    {
        var view = TagModel.Union([L("b", "a"), L("a", "c")]);
        Assert.Equal(L("b", "a", "c"), view.Select(v => v.Value));
        Assert.Equal([1, 2, 1], view.Select(v => v.Count));
    }

    [Fact]
    public void ReorderingNeedsIdenticalSequences()
    {
        Assert.True(TagModel.SameSequence([L("a", "b"), L("a", "b")]));
        Assert.False(TagModel.SameSequence([L("a", "b"), L("b", "a")]));
    }

    // ===== 显示文本与非数字值 =====

    static readonly IReadOnlyList<TagItem> Catalog =
    [
        new TagItem("Bright", [new TagItem("resonant", "Resonant"), new TagItem("power", "Power")]),
        TagItem.Separator(),
        new TagItem(1, "One"),
        true,
    ];

    [Fact]
    public void DisplayTextFindsCandidatesInsideGroupsAndAcceptsNonStringValues()
    {
        Assert.Equal("Power", TagModel.DisplayTextOf(Catalog, V("power")));
        Assert.Equal("One", TagModel.DisplayTextOf(Catalog, PropertyValue.Create(1)));
        Assert.Equal("True", TagModel.DisplayTextOf(Catalog, PropertyValue.Create(true)));
        Assert.Null(TagModel.DisplayTextOf(Catalog, V("gone")));   // 候选外：调用方按异常态显示原值
    }

    // ===== + 菜单：已选隐藏，空了的分组 / 区段一并隐藏 =====

    static string Shape(IReadOnlyList<DropDownItem> items)
        => string.Join(" ", items.Select(i => i.IsSeparator ? "|" + i.Text + "|"
                                       : i.IsGroup ? i.Text + "[" + Shape(i.Children!) + "]"
                                       : i.Text));

    [Fact]
    public void AddMenuHidesPresentValuesAndTheGroupsTheyEmpty()
    {
        Assert.Equal("Bright[Resonant Power] || One True", Shape(AddableMenu.Build(Catalog, _ => false)));

        var present = new[] { V("resonant"), V("power") };
        Assert.Equal("One True", Shape(AddableMenu.Build(Catalog, v => present.Contains(v))));
    }

    // ===== 命令面 =====

    [Fact]
    public void DescribeSaysOrderedAndDistinct()
    {
        var text = ConfigText.Describe(TagConfig.Create(Catalog));
        Assert.Contains("ordered list of distinct values", text);
        Assert.Contains("\"power\" (\"Power\")", text);
        Assert.Contains("1 (\"One\")", text);
    }

    [Fact]
    public void FormatValueRendersArrays()
        => Assert.Equal("[\"a\", 1]", ConfigText.FormatValue(new PropertyArray([V("a"), PropertyValue.Create(1)])));

    // 命令的 Data 里存 ToJson 转出的原始值、渲染时再经 FormatValue(JsonNode) 转回：数组若转不回来，
    // tag 字段的当前值就显示成 "(none)"。
    [Fact]
    public void RenderingFromJsonKeepsArrays()
        => Assert.Equal("[\"a\", 1]", ConfigText.FormatValue(ConfigText.ToJson(new PropertyArray([V("a"), PropertyValue.Create(1)]))));

    [Fact]
    public void NormalizeMatchesCandidatesKeepsOrderAndDropsDuplicates()
    {
        using var raw = JsonDocument.Parse("[\"POWER\", 1, \"resonant\", \"power\"]");
        var (value, error) = ExtensionSettingsText.Normalize(TagConfig.Create(Catalog), raw.RootElement, "timbre");

        Assert.Null(error);
        Assert.True(value.ToArray(out var array));
        Assert.Equal([V("power"), PropertyValue.Create(1), V("resonant")], array.ToList());
    }

    [Fact]
    public void NormalizeRejectsTheWholeListOnOneUnknownTag()
    {
        using var raw = JsonDocument.Parse("[\"power\", \"nope\"]");
        var (_, error) = ExtensionSettingsText.Normalize(TagConfig.Create(Catalog), raw.RootElement, "timbre");
        Assert.Contains("\"nope\" is not an allowed tag", error);
    }

    [Fact]
    public void NormalizeAsksForAnArray()
    {
        using var raw = JsonDocument.Parse("\"power\"");
        var (_, error) = ExtensionSettingsText.Normalize(TagConfig.Create(Catalog), raw.RootElement, "timbre");
        Assert.Contains("give a JSON array", error);
    }

    // ===== 片段预设：整值写入（有序、空选择也能清空）=====

    [Fact]
    public void PresetApplyReplacesTheWholeTagList()
    {
        var config = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "timbre", TagConfig.Create(Catalog) } });
        var node = new DataPropertyObject();
        node.SetValue("timbre", A("resonant", "power"));

        PartPresets.ApplyProperties(config, new PropertyObject(new Map<string, PropertyValue> { { "timbre", A("power") } }), node);
        Assert.Equal(L("power"), TagModel.Read(node, "timbre", A()));

        PartPresets.ApplyProperties(config, new PropertyObject(new Map<string, PropertyValue> { { "timbre", A() } }), node);
        Assert.Empty(TagModel.Read(node, "timbre", A("resonant")));   // 显式空选择，不回落默认
    }
}
