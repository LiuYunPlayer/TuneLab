using System;
using System.Globalization;
using System.Text.Json;

namespace TuneLab.Agent;

// agent 工具解析模型给出的参数 JSON 的小助手：把"取必需/可选字段"收成一处，缺字段时抛带字段名的清晰错误
// （会被 AgentRunner 转成给模型的错误文本，供其自行纠正）。
//
// 容错：不少模型/endpoint 会把数字写成 JSON 字符串（"1"）、把整数写成小数（1.0）、或给可选字段塞 null。
// 故这里对 number/string 互转、整数四舍五入、null/空串当缺省做强制兼容——配合工具 schema 把数字字段放宽为
// 接受 string、可选字段接受 null（endpoint 的严格校验才放行），两端合起来让弱模型也能稳定调用。
internal static class ToolJson
{
    public static JsonElement Require(this JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            throw new ArgumentException("missing required field \"" + name + "\".");
        return v;
    }

    public static int GetInt(this JsonElement obj, string name) => ToInt(obj.Require(name), name);
    public static double GetDouble(this JsonElement obj, string name) => ToDouble(obj.Require(name), name);

    public static string GetString(this JsonElement obj, string name)
    {
        var v = obj.Require(name);
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.GetRawText();
    }

    public static int? GetIntOrNull(this JsonElement obj, string name)
        => TryGet(obj, name, out var v) ? ToInt(v, name) : null;

    public static double? GetDoubleOrNull(this JsonElement obj, string name)
        => TryGet(obj, name, out var v) ? ToDouble(v, name) : null;

    public static bool? GetBoolOrNull(this JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
        return null;
    }

    public static string? GetStringOrNull(this JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    // 取出存在且非 null 的字段；null / 缺失 / 空字符串都视作"未提供"返回 false。
    static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v))
            return false;
        if (v.ValueKind == JsonValueKind.Null)
            return false;
        if (v.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(v.GetString()))
            return false;
        value = v;
        return true;
    }

    static int ToInt(JsonElement v, string name)
    {
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i)) return i;
            if (v.TryGetDouble(out var d)) return (int)Math.Round(d);
        }
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i)) return i;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return (int)Math.Round(d);
            throw NotLiteral(name, s, "an integer");
        }
        throw new ArgumentException("field \"" + name + "\" must be an integer.");
    }

    static double ToDouble(JsonElement v, string name)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String)
        {
            if (double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) return ds;
            throw NotLiteral(name, v.GetString(), "a number");
        }
        throw new ArgumentException("field \"" + name + "\" must be a number.");
    }

    // 模型常把 "${...}"/函数调用之类的表达式当参数（误以为有内联求值）。给出明确纠正指引而非泛泛报错。
    static ArgumentException NotLiteral(string name, string? value, string kind)
    {
        if (!string.IsNullOrEmpty(value) && (value.Contains("${") || value.Contains("(") || value.Contains("=>")))
            return new ArgumentException(string.Format(
                "field \"{0}\" must be a literal {1}, not an expression/placeholder ({2}). Call the read tool first, then pass the actual returned value as a plain {1}.",
                name, kind, value));
        return new ArgumentException("field \"" + name + "\" must be " + kind + ".");
    }
}
