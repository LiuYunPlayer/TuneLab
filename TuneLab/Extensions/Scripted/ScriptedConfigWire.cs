using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Scripted;

// sidecar → 宿主 的控件配置族解码（设计文档 §5.3 值编码 + scripting/wire/v1.json "config"）。
// config 类全部私有构造 + 静态工厂 + With 链（sdk-api-evolution D 类封闭族），反射写不进——按工厂重建。
// 不可跨线的可调用成员（INormalizedScale.Custom / INumberFormat.Custom / IDragResponse）：
// 规范只承载声明数据（linear|integer 标度、decimals 格式）；自定义 lambda 是进程内代码，
// façade 不得发送，codec 见之即抛（早失败于人话，胜过渲染期猜）。
//
// 线上形（type 判别，数值成员 NaN/±Infinity 用字符串字面量，同值编码规范）：
//   {"type":"slider","defaultValue":d,"scale":{"kind":"linear"|"integer","min":a,"max":b},
//     "format"?:{"kind":"decimals","digits":n},"randomizable"?:b,"minLabel"?:"s","maxLabel"?:"s"}
//   {"type":"checkbox","defaultValue":b}
//   {"type":"textbox","defaultValue":s,"password"?:b,"multiline"?:b,"maxVisibleLines"?:n}
//   {"type":"combo","items":[{"value":<pv>,"displayText"?:"s","separator"?:b,"subItems"?:[items]}],"default"?:<pv>}
//   {"type":"number","defaultValue":d,"min"?:a,"max"?:b,"step"?:s,"sensitivity"?:v,"randomizable"?:b,"format"?...}
//   {"type":"path","defaultValue":s,"target":"file"|"folder","fileTypes"?:[{"name":s,"patterns":[s]}],"pickerTitle"?:s}
//   {"type":"automation","scale":{...},"defaultValue"?:d(NaN=分段),"minValue"/"maxValue" 亦可在无 scale 时给出,
//     "color"?,"format"?, "minLabel"?, "maxLabel"?, "randomizable"?}
//   {"type":"object","properties":[{"id":s,"displayText"?:s,"config":<config>}]}（数组保序）
// automation 轨 map 一律 [{id,displayText?,config:<automation>}]（键随 id、显示名随 displayText）。
internal static class ScriptedConfigWire
{
    public static IControllerConfig ReadConfig(JsonNode? node)
    {
        var o = RequireObject(node, "config");
        var type = RequireString(o["type"], "config.type");
        switch (type)
        {
            case "slider":
            {
                var cfg = SliderConfig.Create(ReadDouble(o["defaultValue"], "slider.defaultValue"), ReadScale(o["scale"], "slider.scale"));
                if (ReadFormat(o["format"]) is { } fmt) cfg = cfg.WithFormat(fmt);
                if (ReadBool(o["randomizable"])) cfg = cfg.WithRandomizable();
                if (ReadString(o["minLabel"]) is { } mn) cfg = cfg.WithMinLabel(mn);
                if (ReadString(o["maxLabel"]) is { } mx) cfg = cfg.WithMaxLabel(mx);
                return cfg;
            }
            case "checkbox":
                return CheckBoxConfig.Create(ReadBool(o["defaultValue"]));
            case "textbox":
            {
                var cfg = TextBoxConfig.Create(ReadString(o["defaultValue"]) ?? string.Empty);
                if (ReadBool(o["password"])) cfg = cfg.WithPassword();
                if (ReadBool(o["multiline"])) cfg = cfg.WithMultiline(o["maxVisibleLines"] is JsonValue mv ? (int)ToDouble(mv) : 0);
                return cfg;
            }
            case "combo":
            {
                var cfg = ComboBoxConfig.Create();
                if (o["items"] is JsonArray items)
                    foreach (var item in items)
                        cfg = cfg.Append(ReadComboItem(item));
                if (o["default"] is { } def)
                {
                    var want = ScriptedWire.ReadPropertyValue(def);
                    foreach (var it in cfg.Items)
                        if (it.Value.Equals(want)) { cfg = cfg.WithDefault(it); break; }
                }
                return cfg;
            }
            case "number":
            {
                var cfg = DraggableNumberBoxConfig.Create(ReadDouble(o["defaultValue"], "number.defaultValue"));
                if (o["min"] is JsonValue minV) cfg = cfg.WithMin(ToDouble(minV));
                if (o["max"] is JsonValue maxV) cfg = cfg.WithMax(ToDouble(maxV));
                if (o["step"] is JsonValue stepV) cfg = cfg.WithStep(ToDouble(stepV));
                if (o["sensitivity"] is JsonValue sensV) cfg = cfg.WithSensitivity(ToDouble(sensV));
                if (ReadBool(o["randomizable"])) cfg = cfg.WithRandomizable();
                if (ReadFormat(o["format"]) is { } fmt2) cfg = cfg.WithFormat(fmt2);
                return cfg;
            }
            case "path":
            {
                var def = ReadString(o["defaultValue"]) ?? string.Empty;
                var cfg = (ReadString(o["target"]) ?? "file") == "folder"
                    ? PathPickerConfig.CreateFolder(def)
                    : PathPickerConfig.CreateFile(def);
                if (o["fileTypes"] is JsonArray fts)
                    foreach (var ft in fts)
                    {
                        var fo = RequireObject(ft, "path.fileTypes[]");
                        cfg = cfg.AppendFileType(new FileTypeFilter(
                            RequireString(fo["name"], "fileTypes[].name"),
                            (fo["patterns"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList() ?? []));
                    }
                if (ReadString(o["pickerTitle"]) is { } title) cfg = cfg.WithPickerTitle(title);
                return cfg;
            }
            case "automation":
            {
                var cfg = o["scale"] != null
                    ? AutomationConfig.Create(ReadScale(o["scale"], "automation.scale"))
                    : AutomationConfig.Create(ReadDouble(o["minValue"], "automation.minValue"), ReadDouble(o["maxValue"], "automation.maxValue"));
                if (o["defaultValue"] != null) cfg = cfg.WithDefault(ReadDouble(o["defaultValue"], "automation.defaultValue"));
                if (ReadString(o["color"]) is { } color) cfg = cfg.WithColor(color);
                if (ReadFormat(o["format"]) is { } fmt3) cfg = cfg.WithFormat(fmt3);
                if (ReadString(o["minLabel"]) is { } mn2) cfg = cfg.WithMinLabel(mn2);
                if (ReadString(o["maxLabel"]) is { } mx2) cfg = cfg.WithMaxLabel(mx2);
                if (ReadBool(o["randomizable"])) cfg = cfg.WithRandomizable();
                return cfg;
            }
            case "object":
            {
                var props = new OrderedMap<PropertyKey, IControllerConfig>();
                if (o["properties"] is JsonArray ps)
                    foreach (var p in ps)
                    {
                        var po = RequireObject(p, "object.properties[]");
                        props[new PropertyKey(RequireString(po["id"], "properties[].id"), ReadString(po["displayText"]))]
                            = ReadConfig(po["config"]);
                    }
                return ObjectConfig.Create(props);
            }
            default:
                throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "config type '{0}' is not constructible from the wire", type));
        }
    }

    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> ReadAutomationMap(JsonNode? node, string what)
    {
        var map = new OrderedMap<PropertyKey, AutomationConfig>();
        if (node is JsonArray arr)
            foreach (var item in arr)
            {
                var o = RequireObject(item, what + "[]");
                var key = new PropertyKey(RequireString(o["id"], what + "[].id"), ReadString(o["displayText"]));
                var cfg = o["config"] != null ? ReadConfig(o["config"]) : ReadConfig(item);
                map[key] = (AutomationConfig)cfg;
            }
        return map;
    }

    // ── 零件 ──

    static INormalizedScale ReadScale(JsonNode? node, string what)
    {
        var o = RequireObject(node, what);
        var kind = RequireString(o["kind"], what + ".kind");
        var min = ReadDouble(o["min"], what + ".min");
        var max = ReadDouble(o["max"], what + ".max");
        return kind switch
        {
            "linear" => NormalizedScale.Linear(min, max),
            "integer" => NormalizedScale.Integer(min, max),
            _ => throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
                "scale kind '{0}' cannot cross the wire (custom scales are in-process code; declare linear|integer)", kind)),
        };
    }

    static INumberFormat? ReadFormat(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var kind = ReadString(o["kind"]);
        if (kind == "decimals") return NumberFormat.Decimals((int)ReadDouble(o["digits"], "format.digits"));
        throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
            "number format kind '{0}' cannot cross the wire (declare decimals)", kind));
    }

    static ComboBoxItem ReadComboItem(JsonNode? node)
    {
        var o = RequireObject(node, "combo.items[]");
        if (ReadBool(o["separator"]))
            return ComboBoxItem.Separator(ReadString(o["displayText"]));   // SDK internal 工厂，宿主经 IVT 可用
        var value = ScriptedWire.ReadPropertyValue(o["value"]);
        var display = ReadString(o["displayText"]);
        if (o["subItems"] is JsonArray subs)
        {
            var label = display;
            if (label == null && value.ToString(out var sv))
                label = sv;
            return new ComboBoxItem(label ?? string.Empty, subs.Select(ReadComboItem).ToList());
        }
        return new ComboBoxItem(value, display);
    }

    // ── 小工具（require 系列：一切坏形都抛带路径的人话）──

    static JsonObject RequireObject(JsonNode? node, string what)
        => node as JsonObject ?? throw new JsonException(string.Format(CultureInfo.InvariantCulture, "'{0}' must be a JSON object", what));

    static string? ReadString(JsonNode? node)
        => node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    static string RequireString(JsonNode? node, string what)
        => ReadString(node) ?? throw new JsonException(string.Format(CultureInfo.InvariantCulture, "'{0}' is required and must be a string", what));

    static double ReadDouble(JsonNode? node, string what)
    {
        if (node is null)
            throw new JsonException(string.Format(CultureInfo.InvariantCulture, "'{0}' is required and must be a number", what));
        return ToDouble((JsonValue)node);
    }

    static double ToDouble(JsonValue v)
        => v.GetValueKind() == JsonValueKind.String
            ? ScriptedWire.ParseNumberString(v.GetValue<string>())          // "NaN"|"Infinity"|"-Infinity"|数字串
            : v.GetValue<double>();

    static bool ReadBool(JsonNode? node, bool def = false)
        => node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : def;
}
