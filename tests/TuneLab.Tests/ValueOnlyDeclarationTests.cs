using System;
using TuneLab.Commands;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 只传值的声明：独立通道的空壳（AutomationConfig / TimelineLaneCanvasConfig 的无参 Create）与控件树里的 ValueOnlyConfig。
public class ValueOnlyDeclarationTests
{
    sealed class NullControl : ICanvasControl { }

    [Fact]
    public void AutomationShell_IsNeverAndCannotBeShown()
    {
        var shell = AutomationConfig.Create();
        Assert.Equal(ControllerVisibility.Never, shell.Visibility);
        Assert.True(shell.IsPiecewise);                       // 同 Create(min, max)：不给默认值就是分段
        Assert.False(shell.WithDefault(0.5).IsPiecewise);
        Assert.Equal(ControllerVisibility.Never, shell.WithDefault(0.5).Visibility);
        Assert.Throws<ArgumentException>(() => shell.WithVisibility(ControllerVisibility.ShownByDefault));
        Assert.Throws<ArgumentException>(() => shell.WithDefault(0.5).WithVisibility(ControllerVisibility.HiddenByDefault));
        Assert.Equal(ControllerVisibility.Never, shell.WithVisibility(ControllerVisibility.Never).Visibility);
    }

    // 空壳不量化、不钳位：合成读到的就是曲线原值。
    [Fact]
    public void AutomationShell_ScaleIsIdentity()
    {
        var scale = AutomationConfig.Create().Scale;
        foreach (var value in new[] { -3.5, 0, 0.37, 1, 120.25 })
            Assert.Equal(value, scale.ToValue(scale.ToNormalized(value)));
    }

    [Fact]
    public void FullAutomationConfig_CanTakeAnyVisibility()
    {
        var config = AutomationConfig.Create(0, 1).WithDefault(0);
        Assert.Equal(ControllerVisibility.ShownByDefault, config.Visibility);
        Assert.Equal(ControllerVisibility.HiddenByDefault, config.WithVisibility(ControllerVisibility.HiddenByDefault).Visibility);
        var never = config.WithVisibility(ControllerVisibility.Never);
        Assert.Equal(ControllerVisibility.Never, never.Visibility);
        Assert.Equal(ControllerVisibility.ShownByDefault, never.WithVisibility(ControllerVisibility.ShownByDefault).Visibility);   // 有量程的可以切回来
    }

    [Fact]
    public void TimelineLaneCanvasShell_HasNoFactoryAndCannotBeShown()
    {
        var shell = TimelineLaneCanvasConfig.Create();
        Assert.Null(shell.CreateControl);
        Assert.Equal(ControllerVisibility.Never, shell.Visibility);
        Assert.Throws<ArgumentException>(() => shell.WithVisibility(ControllerVisibility.HiddenByDefault));

        var full = TimelineLaneCanvasConfig.Create(_ => new NullControl());
        Assert.Equal(ControllerVisibility.ShownByDefault, full.Visibility);
        Assert.Equal(ControllerVisibility.Never, full.WithVisibility(ControllerVisibility.Never).Visibility);
    }

    // 值语义同画布：没有默认值（不实现 IValueConfig），Null = 清掉，任何真实的值都收、不校验形状。
    [Fact]
    public void ValueOnlyConfig_TakesAnyValueLikeACanvas()
    {
        var config = ValueOnlyConfig.Create();
        Assert.False(config is IValueConfig);
        Assert.Null(ConfigValues.Check(config, PropertyValue.Null, "x").Error);
        Assert.Null(ConfigValues.Check(config, PropertyValue.Create(3.0), "x").Error);
        Assert.Null(ConfigValues.Check(config, new PropertyArray([PropertyValue.Create("a")]), "x").Error);
        Assert.NotNull(ConfigValues.Check(config, PropertyValue.Multiple, "x").Error);
    }

    // 恢复默认 / preset 物化：对象默认值里不占它的键（同画布）。
    [Fact]
    public void ValueOnlyConfig_TakesNoKeyInObjectDefaults()
    {
        var obj = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "level", SliderConfig.Linear(1, 0, 2) },
            { "legacy", ValueOnlyConfig.Create() },
        });
        Assert.True(TuneLab.GUI.Controllers.ControllerConfigDefaults.GetDefaultValue(obj).ToObject(out var defaults));
        Assert.True(defaults.Map.ContainsKey("level"));
        Assert.False(defaults.Map.ContainsKey("legacy"));
    }
}
