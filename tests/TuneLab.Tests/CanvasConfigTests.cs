using System;
using System.Text.Json;
using TuneLab.Commands;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// CanvasConfig（插件自绘画布）的宿主侧封条：它是普通值字段（默认值递归 / preset / 恢复默认照常），工厂不在声明时调用，
// 命令面只读、不替插件解读值。画布的渲染、事件与写回需要窗口，不在此测。
public class CanvasConfigTests
{
    sealed class NullControl : ICanvasControl { }

    static PropertyValue Curve => new PropertyArray([PropertyValue.Create(0.0), PropertyValue.Create(1.0)]);

    [Fact]
    public void Create_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => CanvasConfig.Create(Curve, 80, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasConfig.Create(Curve, 0, _ => new NullControl()));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasConfig.Create(Curve, double.NaN, _ => new NullControl()));
    }

    // 默认值就是未设状态、也定了字段类型：Null 作默认 = 永远存不进值，Multiple 绝不入存储——都当场报错。
    // 标量 / 数组 / 对象都合法（顶层用对象只是建议）。
    [Fact]
    public void Create_RejectsNullOrMultipleDefault()
    {
        Assert.Throws<ArgumentException>(() => CanvasConfig.Create(PropertyValue.Null, 80, _ => new NullControl()));
        Assert.Throws<ArgumentException>(() => CanvasConfig.Create(PropertyValue.Multiple, 80, _ => new NullControl()));
        CanvasConfig.Create(PropertyValue.Create(0.5), 80, _ => new NullControl());
        CanvasConfig.Create(PropertyObject.Empty, 80, _ => new NullControl());
    }

    [Fact]
    public void Create_DoesNotCallTheFactory()
    {
        int calls = 0;
        var config = CanvasConfig.Create(Curve, 80, _ => { calls++; return new NullControl(); });
        _ = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "curve", config } }).GetDefaultValue();
        Assert.Equal(0, calls);   // 声明（含 agent / CLI 只读声明）不建控件
    }

    [Fact]
    public void WithSummary_ReturnsACopy()
    {
        var plain = CanvasConfig.Create(Curve, 80, _ => new NullControl());
        var summarized = plain.WithSummary(_ => "Linear");
        Assert.Null(plain.Summary);
        Assert.Equal("Linear", summarized.Summary!(Curve));
        Assert.Equal(plain.Height, summarized.Height);
        Assert.Same(plain.CreateControl, summarized.CreateControl);
    }

    [Fact]
    public void DefaultValueOfAnObject_IncludesTheCanvasValue()
    {
        var config = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "curve", CanvasConfig.Create(Curve, 80, _ => new NullControl()) },
        });

        Assert.True(config.GetDefaultValue().ToObject(out var value));
        Assert.Equal(Curve, value.Map["curve"]);   // 值字段：恢复默认 / preset 照常带上它
    }

    [Fact]
    public void CommandSurface_PointsToTheIntroduction_AndTakesAnyJsonAsIs()
    {
        var canvas = CanvasConfig.Create(Curve, 80, _ => new NullControl());
        var text = ConfigText.Describe(canvas);
        Assert.Contains("canvas", text);
        Assert.Contains("introduction", text);

        // 宿主不知道形状，也就不校验：嵌套的数组 / 对象原样收下。
        using var raw = JsonDocument.Parse("{\"points\": [0, 0.5, 1], \"mode\": \"smooth\"}");
        var (value, error) = ExtensionSettingsText.Normalize(canvas, raw.RootElement, "curve");
        Assert.Null(error);
        Assert.True(value.ToObject(out var obj));
        Assert.True(obj.Map["points"].ToArray(out var points));
        Assert.Equal(3, points.Count);
        Assert.Equal("{\"points\": [0, 0.5, 1], \"mode\": \"smooth\"}", ConfigText.FormatValue(value));
        Assert.Equal(raw.RootElement.GetRawText().Replace(" ", ""), ConfigText.ToJson(value)!.ToJsonString());

        using var none = JsonDocument.Parse("null");
        Assert.NotNull(ExtensionSettingsText.Normalize(canvas, none.RootElement, "curve").Error);
    }

    // 一个插件控件只实现 Render：其余成员全走默认体（空），编译即证明 Render 也不是必须实现的。
    [Fact]
    public void Control_AllMembersHaveDefaults()
    {
        ICanvasControl control = new NullControl();
        control.Render(null!);
        control.OnScroll(null!);
    }
}
