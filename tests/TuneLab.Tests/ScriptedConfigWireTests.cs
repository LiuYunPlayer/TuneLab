using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Extensions.Scripted;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// ScriptedConfigWire：sidecar 声明的控件配置重建为工厂构造的 config 族（设计 §5.3 / wire "config" 节）。
internal static class ConfigFixtures
{
    // 与 tests/ScriptedFakeSidecar WidgetConfig() 同形（两处一致由测试交叉钉）。
    public const string Widgets = """
    {"type":"object","properties":[
      {"id":"model_dir","displayText":"模型目录","config":{"type":"path","defaultValue":"","target":"folder"}},
      {"id":"gain","config":{"type":"slider","defaultValue":0.0,"scale":{"kind":"linear","min":-12.0,"max":12.0},
        "format":{"kind":"decimals","digits":1},"randomizable":true,"minLabel":"Soft","maxLabel":"Hard"}},
      {"id":"seed_mode","config":{"type":"combo","items":[
        {"value":{"s":"fast"},"displayText":"Fast"},
        {"separator":true,"displayText":"advanced"},
        {"value":{"s":"full"}}],"default":{"s":"fast"}}},
      {"id":"denoise","config":{"type":"checkbox","defaultValue":true}},
      {"id":"api_key","config":{"type":"textbox","defaultValue":"","password":true}},
      {"id":"note","config":{"type":"textbox","defaultValue":"","multiline":true,"maxVisibleLines":4}},
      {"id":"steps","config":{"type":"number","defaultValue":4.0,"min":1.0,"max":10.0,"step":1.0}}
    ]}
    """;
}

public class ScriptedConfigWireTests
{
    [Fact]
    public void RebuildsEveryLeafTypeViaFactories()
    {
        var cfg = Assert.IsAssignableFrom<ObjectConfig>(ScriptedConfigWire.ReadConfig(JsonNode.Parse(ConfigFixtures.Widgets)));
        var props = cfg.Properties;
        Assert.Equal(7, props.Count);

        var path = Assert.IsType<PathPickerConfig>(props["model_dir"]);
        Assert.Equal(PathPickerTarget.Folder, path.Target);

        var slider = Assert.IsType<SliderConfig>(props["gain"]);
        Assert.Equal(-12.0, slider.Scale.ToValue(0));
        Assert.Equal(12.0, slider.Scale.ToValue(1));
        Assert.Equal("Soft", slider.MinLabel);
        Assert.True(slider.Randomizable);
        Assert.Equal("0.0", slider.Format.Format(0.0));        // decimals(1)
        Assert.True(slider.Format.Parse("2.5") > 2);

        var combo = Assert.IsType<ComboBoxConfig>(props["seed_mode"]);
        Assert.Equal(3, combo.Items.Count);                    // fast / separator / full
        Assert.Equal(PropertyValue.Create("fast"), combo.DefaultOption.Value);
        Assert.Equal("Fast", combo.Items[0].DisplayText);

        var check = Assert.IsType<CheckBoxConfig>(props["denoise"]);
        Assert.True(check.DefaultValue);

        var pw = Assert.IsType<TextBoxConfig>(props["api_key"]);
        Assert.True(pw.IsPassword);

        var multiline = Assert.IsType<TextBoxConfig>(props["note"]);
        Assert.True(multiline.IsMultiline);
        Assert.Equal(4, multiline.MaxVisibleLines);

        var num = Assert.IsType<DraggableNumberBoxConfig>(props["steps"]);
        Assert.Equal(1.0, num.Min);
        Assert.Equal(10.0, num.Max);
        Assert.Equal(1.0, num.Step);
    }

    [Fact]
    public void KeyOrderAndDisplayTextsSurvive()
    {
        var cfg = (ObjectConfig)ScriptedConfigWire.ReadConfig(JsonNode.Parse(ConfigFixtures.Widgets));
        Assert.Equal(new[] { "model_dir", "gain", "seed_mode", "denoise", "api_key", "note", "steps" },
            cfg.Properties.Keys.Select(k => k.Id));
        Assert.Equal("模型目录", cfg.Properties.Keys.First().DisplayText);
    }

    [Fact]
    public void AutomationMapAndPiecewiseNaN()
    {
        var map = ScriptedConfigWire.ReadAutomationMap(JsonNode.Parse("""
            [{"id":"growl","displayText":"Growl","config":{"type":"automation","scale":{"kind":"linear","min":0.0,"max":1.0},"defaultValue":0.0,"color":"#E5A573"}},
             {"id":"pitch","config":{"type":"automation","minValue":0.0,"maxValue":1200.0,"defaultValue":"NaN"}}]
            """), "automations");
        Assert.Equal(2, map.Count);
        Assert.False(map["growl"].IsPiecewise);
        Assert.Equal("#E5A573", map["growl"].Color);
        Assert.True(map["pitch"].IsPiecewise);                 // 字符串 "NaN" → 分段轨
        Assert.Equal(1200.0, map["pitch"].MaxValue);
    }

    [Fact]
    public void CustomScalesAreRefusedWithHumanWords()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ScriptedConfigWire.ReadConfig(JsonNode.Parse(
            """{"type":"slider","defaultValue":0.0,"scale":{"kind":"my-log","min":0.0,"max":1.0}}""")));
        Assert.Contains("cannot cross the wire", ex.Message);
    }

    [Fact]
    public void UnknownConfigTypeThrows()
    {
        Assert.Throws<NotSupportedException>(() => ScriptedConfigWire.ReadConfig(JsonNode.Parse("""{"type":"knob"}""")));
    }

    [Fact]
    public void MalformedShapeThrowsWithJsonPath()
    {
        // slider 缺 scale：RequireObject 抛，指到名。
        var ex = Assert.Throws<JsonException>(() => ScriptedConfigWire.ReadConfig(JsonNode.Parse(
            """{"type":"slider","defaultValue":0.0}""")));
        Assert.Contains("slider.scale", ex.Message);
    }
}
