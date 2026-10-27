using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// ConfigValues（非界面入口按 config 校验值的唯一口径）：每种 config 都有明确处理，叶子按约束校验、复合字段递归。
public class ConfigValuesTests
{
    static PropertyValue V(string s) => PropertyValue.Create(s);
    static PropertyValue N(double d) => PropertyValue.Create(d);

    static PropertyObject Obj(params (string Key, PropertyValue Value)[] entries)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var (key, value) in entries)
            map.Add(key, value);
        return new PropertyObject(map);
    }

    static ObjectConfig Fields(params (string Key, IControllerConfig Config)[] fields)
    {
        var map = new OrderedMap<PropertyKey, IControllerConfig>();
        foreach (var (key, config) in fields)
            map.Add(key, config);
        return ObjectConfig.Create(map);
    }

    // 覆盖哨兵：SDK 里的每个 IControllerConfig 实现，ConfigValues 都必须明确处理——要么是值（Values），要么明确不是值
    //（NotValues，Check 拒并说明）。新增控件忘了在 ConfigValues 里处理，这里先失败。
    [Fact]
    public void EveryConfigTypeIsHandled()
    {
        var sdkConfigs = typeof(IControllerConfig).Assembly.GetTypes()
            .Where(t => typeof(IControllerConfig).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract && t.IsPublic)
            .ToList();
        var handled = ConfigValues.Values.Concat(ConfigValues.NotValues).ToHashSet();
        var missing = sdkConfigs.Where(t => !handled.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0, "ConfigValues doesn't handle: " + string.Join(", ", missing));
    }

    // 每种"是值"的 config：它自己的默认值必须通过校验，描述也不能退化成笼统的 "value"。
    public static IEnumerable<object[]> ValueSamples()
    {
        yield return [CheckBoxConfig.Create(true)];
        yield return [SliderConfig.Linear(0.5, 0, 1)];
        yield return [DraggableNumberBoxConfig.Create(3)];
        yield return [ComboBoxConfig.Create(["a", "b"])];
        yield return [TagConfig.Create(["x", "y"])];
        yield return [TextBoxConfig.Create("t")];
        yield return [PathPickerConfig.CreateFile("")];
        yield return [CanvasConfig.Create(PropertyObject.Empty, 240, 80, _ => throw new NotSupportedException())];
        yield return [Fields(("n", SliderConfig.Linear(0, 0, 1)))];
        yield return [ArrayConfig.Create([SliderConfig.Linear(0, 0, 1), TextBoxConfig.Create("")])];
        yield return [ListConfig.Create([TextBoxConfig.Create("a")], [new AddableElement(TextBoxConfig.Create(""))])];
        yield return [ExtensibleObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "k", CheckBoxConfig.Create() } },
            [new AddableKey("j", SliderConfig.Linear(0, 0, 1))])];
    }

    [Theory]
    [MemberData(nameof(ValueSamples))]
    public void DefaultValuePassesAndIsDescribed(IControllerConfig config)
    {
        Assert.Contains(config.GetType(), ConfigValues.Values);
        var (_, error) = ConfigValues.Check(config, config.GetDefaultValue(), "f");
        Assert.Null(error);
        Assert.NotEqual("value", ConfigText.Describe(config));
    }

    [Fact]
    public void NotValuesAreRejectedWithAReason()
    {
        var button = ButtonConfig.Create("Run", _ => Task.CompletedTask);
        Assert.Contains("button", ConfigValues.Check(button, V("x"), "run").Error);
        Assert.Contains("automation", ConfigValues.Check(AutomationConfig.Create(0, 1), N(0), "a").Error);
    }

    [Fact]
    public void NullAndMultipleAreNotValues()
    {
        Assert.NotNull(ConfigValues.Check(TextBoxConfig.Create(), PropertyValue.Null, "f").Error);
        Assert.NotNull(ConfigValues.Check(TextBoxConfig.Create(), PropertyValue.Multiple, "f").Error);
    }

    [Fact]
    public void LeavesAreCheckedAndNormalized()
    {
        Assert.Contains("out of range", ConfigValues.Check(SliderConfig.Linear(0, 0, 1), N(2), "s").Error);
        Assert.Equal(N(0.25), ConfigValues.Check(SliderConfig.Linear(0, 0, 1), V("0.25"), "s").Value);   // 数字字符串落 double
        Assert.Equal(PropertyValue.Create(true), ConfigValues.Check(CheckBoxConfig.Create(), V("true"), "c").Value);
        Assert.Contains("is text", ConfigValues.Check(TextBoxConfig.Create(), PropertyValue.Create(true), "t").Error);
        Assert.Equal(V("3"), ConfigValues.Check(TextBoxConfig.Create(), N(3), "t").Value);
    }

    // 下拉只认存储值（先区分大小写、再不区分），落成候选原本的值形态；不认显示文字。
    [Fact]
    public void ComboBoxMatchesStoredValuesOnly()
    {
        var combo = ComboBoxConfig.Create(new ComboBoxItem[] { new(0, "Low"), new(1, "Mid") });
        Assert.Equal(N(1), ConfigValues.Check(combo, V("1"), "q").Value);
        Assert.NotNull(ConfigValues.Check(combo, V("Mid"), "q").Error);
        Assert.Equal(V("Soft"), ConfigValues.Check(ComboBoxConfig.Create(["Soft", "Hard"]), V("soft"), "s").Value);
    }

    [Fact]
    public void ObjectRejectsUnknownFieldsAndNamesTheNestedPath()
    {
        var config = Fields(("lfo", Fields(("rate", SliderConfig.Linear(5, 0, 20)))));

        var unknown = ConfigValues.Check(config, Obj(("nope", N(1))), "vib").Error;
        Assert.Contains("has no field \"nope\"", unknown);
        Assert.Contains("lfo", unknown);

        var nested = ConfigValues.Check(config, Obj(("lfo", Obj(("rate", N(99))))), "vib").Error;
        Assert.Contains("\"vib.lfo.rate\"", nested);
    }

    // 变长列表整组写入：已声明的位按逐元素 config，超出的元素须是某个 + 候选类型。
    [Fact]
    public void ListChecksEachElementAndAcceptsAddableTypesBeyondTheDeclaredOnes()
    {
        var list = ListConfig.Create([PathPickerConfig.CreateFolder("")],
            [new AddableElement(PathPickerConfig.CreateFolder(""), "Folder")]);
        var (value, error) = ConfigValues.Check(list, new PropertyArray([V("C:/a"), V("C:/b"), V("C:/c")]), "dirs");
        Assert.Null(error);
        Assert.True(value.ToArray(out var array) && array.Count == 3);

        var bad = ConfigValues.Check(list, new PropertyArray([V("C:/a"), PropertyValue.Create(true)]), "dirs").Error;
        Assert.Contains("\"dirs[1]\"", bad);

        var full = ListConfig.Create([TextBoxConfig.Create("")], []);
        Assert.Contains("can't hold another element", ConfigValues.Check(full, new PropertyArray([V("a"), V("b")]), "l").Error);
    }

    [Fact]
    public void FixedArrayCapsTheLength()
    {
        var array = ArrayConfig.Create([SliderConfig.Linear(0, 0, 1), SliderConfig.Linear(0, 0, 1)]);
        Assert.Null(ConfigValues.Check(array, new PropertyArray([N(0.1)]), "a").Error);
        Assert.Contains("at most 2", ConfigValues.Check(array, new PropertyArray([N(0), N(0), N(0)]), "a").Error);
    }

    // 变长键控：已存的键按 Properties，其余须是 + 候选键（分组展开）。
    [Fact]
    public void ExtensibleObjectAcceptsDeclaredAndAddableKeys()
    {
        var config = ExtensibleObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "a", CheckBoxConfig.Create() } },
            [new AddableKey("Group", [new AddableKey("b", SliderConfig.Linear(0, 0, 1))])]);
        Assert.Null(ConfigValues.Check(config, Obj(("a", PropertyValue.Create(true)), ("b", N(0.5))), "x").Error);
        Assert.Contains("has no field \"c\"", ConfigValues.Check(config, Obj(("c", N(1))), "x").Error);
    }
}
