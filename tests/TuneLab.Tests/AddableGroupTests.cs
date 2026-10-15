using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// + 菜单候选的分组形状（与 ComboBoxItem 同构）：分组项 SubItems 非 null、不携 Template；叶子反之。
// 宿主据「先判 SubItems」区分两者，故这里钉住两种构造各自的形状。
public class AddableGroupTests
{
    [Fact]
    public void AddableElement_Leaf_HasTemplateAndNoSubItems()
    {
        var template = TextBoxConfig.Create("a");
        var leaf = new AddableElement(template, "a");

        Assert.Same(template, leaf.Template);
        Assert.Equal("a", leaf.Label);
        Assert.Null(leaf.SubItems);
    }

    [Fact]
    public void AddableElement_Group_HasSubItemsAndNoTemplate()
    {
        var group = new AddableElement("Vowels", "ai".Select(c => new AddableElement(TextBoxConfig.Create(c.ToString()), c.ToString())));

        Assert.Equal("Vowels", group.Label);
        Assert.Null(group.Template);
        Assert.NotNull(group.SubItems);
        Assert.Equal(["a", "i"], group.SubItems!.Select(e => e.Label));
    }

    [Fact]
    public void AddableElement_Group_NestsAndAllowsEmpty()
    {
        var group = new AddableElement("Consonants", [new AddableElement("Nasal", []), new AddableElement("Plosive", [])]);

        Assert.Equal(2, group.SubItems!.Count);
        Assert.All(group.SubItems!, sub => Assert.Empty(sub.SubItems!));
    }

    [Fact]
    public void AddableKey_Group_HasDisplayTextOnlyAndSubItems()
    {
        var leaf = new AddableKey(("alice", "Alice"), ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>()));
        var group = new AddableKey("Female", [leaf]);

        Assert.Equal("Female", group.Key.DisplayText);
        Assert.Null(group.Key.Id);
        Assert.Null(group.Template);
        Assert.Equal("alice", Assert.Single(group.SubItems!).Key.Id);
        Assert.Null(leaf.SubItems);
    }

    // ===== 分隔线（与 ComboBoxItem.Separator 同形）：经宿主投影验证——插件读不到分隔线标记，宿主菜单才是它的消费者 =====

    static string Shape(IReadOnlyList<DropDownItem> items)
        => string.Join(" ", items.Select(i => i.IsSeparator ? "|" + i.Text + "|"
                                       : i.IsGroup ? i.Text + "[" + Shape(i.Children!) + "]"
                                       : i.Text));

    static AddableElement Leaf(string name) => new(TextBoxConfig.Create(name), name);
    static AddableKey Key(string id) => new((id, id), ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>()));

    [Fact]
    public void AddableElement_Separators_ProjectAtTopLevelAndInsideGroups()
    {
        var menu = AddableMenu.Build(
        [
            Leaf("a"),
            AddableElement.Separator("Sets"),
            new AddableElement("G", [Leaf("b"), AddableElement.Separator(), Leaf("c")]),
        ]);

        Assert.Equal("a |Sets| G[b || c]", Shape(menu));
        Assert.Equal(3, AddableMenu.CountLeaves(menu));   // 分隔线不计入可添加项
    }

    [Fact]
    public void AddableKey_SeparatorWithEmptiedSection_IsDropped()
    {
        IReadOnlyList<AddableKey> candidates =
        [
            AddableKey.Separator("Warm"), Key("red"),
            AddableKey.Separator("Cool"), Key("green"), Key("blue"),
        ];

        // red 已加入 → Warm 段空了，段标题随之隐藏
        Assert.Equal("|Cool| green blue", Shape(AddableMenu.Build(candidates, id => id == "red")));
        // green、blue 已加入 → Cool 段空了
        Assert.Equal("|Warm| red", Shape(AddableMenu.Build(candidates, id => id is "green" or "blue")));
    }

    [Fact]
    public void AddableKey_NothingFiltered_KeepsAuthoredStructure()
    {
        IReadOnlyList<AddableKey> candidates = [AddableKey.Separator("Head"), AddableKey.Separator(), Key("x")];

        // 没有任何键被滤掉时不清理：作者写的结构原样呈现（同下拉不做清理）
        Assert.Equal("|Head| || x", Shape(AddableMenu.Build(candidates, _ => false)));
    }

    [Fact]
    public void AddableKey_GroupEmptiedByFilter_HidesItsSectionSeparatorToo()
    {
        IReadOnlyList<AddableKey> candidates =
        [
            new AddableKey("Female", [Key("alice")]),
            AddableKey.Separator("Male"),
            new AddableKey("M", [Key("bob")]),
        ];

        Assert.Equal("Female[alice]", Shape(AddableMenu.Build(candidates, id => id == "bob")));
    }
}
