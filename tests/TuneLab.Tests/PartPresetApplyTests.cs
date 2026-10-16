using System.Collections.Generic;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// part preset 应用的属性那一半（PartPresets.ApplyProperties）：固定结构对象按字段合并、变长容器按整值替换。
// 回归 issue #154：值为 {} 的键控条目（只靠键存在表达"选中"）应用后落不下键、空选择清不掉旧条目。
public class PartPresetApplyTests
{
    static PropertyObject Obj(params (string key, PropertyValue value)[] entries)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var (key, value) in entries)
            map.Add(key, value);
        return new PropertyObject(map);
    }

    static ObjectConfig Empty() => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());

    // options：键控容器，候选 entry_a / entry_b，条目模板都是无子字段的对象；另有一个标量 gain 与一个固定对象 tone。
    static ObjectConfig Config(params string[] selected)
    {
        var properties = new OrderedMap<PropertyKey, IControllerConfig>();
        foreach (var key in selected)
            properties.Add(key, Empty());
        return ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "options", ExtensibleObjectConfig.Create(properties, [new AddableKey("entry_a", Empty()), new AddableKey("entry_b", Empty())]) },
            { "gain", CheckBoxConfig.Create() },
            { "tone", ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "bright", CheckBoxConfig.Create() } }) },
        });
    }

    static PropertyObject Options(DataPropertyObject node)
        => node.GetInfo().Map.TryGetValue("options", out var value) && value.ToObject(out var options) ? options : PropertyObject.Empty;

    [Fact]
    public void EmptyObjectEntryIsMaterialized()
    {
        var node = new DataPropertyObject();
        var preset = Obj(("options", Obj(("entry_a", PropertyObject.Empty))));

        PartPresets.ApplyProperties(Config(), preset, node);

        Assert.True(Options(node).Map.ContainsKey("entry_a"));
    }

    [Fact]
    public void SelectionReplacesExistingEntriesInsteadOfMerging()
    {
        var node = new DataPropertyObject();
        node.SetValue("options", Obj(("entry_b", PropertyObject.Empty)));
        var preset = Obj(("options", Obj(("entry_a", PropertyObject.Empty))));

        PartPresets.ApplyProperties(Config("entry_b"), preset, node);

        var options = Options(node);
        Assert.True(options.Map.ContainsKey("entry_a"));
        Assert.False(options.Map.ContainsKey("entry_b"));
    }

    [Fact]
    public void EmptySelectionClearsExistingEntries()
    {
        var node = new DataPropertyObject();
        node.SetValue("options", Obj(("entry_a", PropertyObject.Empty)));
        var preset = Obj(("options", PropertyObject.Empty));

        PartPresets.ApplyProperties(Config("entry_a"), preset, node);

        Assert.True(node.GetInfo().Map.ContainsKey("options"));
        Assert.Empty(Options(node).Map);
    }

    // 固定结构对象仍是字段合并：目标里 preset 没有的子键（孤儿）不被替换掉。
    [Fact]
    public void FixedObjectStillMergesFieldByField()
    {
        var node = new DataPropertyObject();
        node.Object("tone").SetValue("legacy", 1.0);
        var preset = Obj(("tone", Obj(("bright", true))), ("gain", true));

        PartPresets.ApplyProperties(Config(), preset, node);

        var info = node.GetInfo();
        var tone = info.GetObject("tone");
        Assert.Equal((PropertyValue)true, tone.Map["bright"]);
        Assert.Equal((PropertyValue)1.0, tone.Map["legacy"]);
        Assert.Equal((PropertyValue)true, info.Map["gain"]);
    }

    // 声明之外的键（换过引擎留下的孤儿）照旧递归合并，不替它做整值替换的决定。
    [Fact]
    public void UndeclaredObjectIsMergedNotReplaced()
    {
        var node = new DataPropertyObject();
        node.Object("orphan").SetValue("keep", 1.0);
        var preset = Obj(("orphan", Obj(("added", 2.0))));

        PartPresets.ApplyProperties(Config(), preset, node);

        var orphan = node.GetInfo().GetObject("orphan");
        Assert.Equal((PropertyValue)1.0, orphan.Map["keep"]);
        Assert.Equal((PropertyValue)2.0, orphan.Map["added"]);
    }
}
