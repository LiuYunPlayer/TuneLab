using System;
using System.Collections.Generic;
using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// JsValue 选项袋 / 数组解析的共用 helper。脚本 API 的写方法多收一个 JS 对象字面量（{pos, dur, ...}）或
// 点数组（[{tick, value}]）；这里把"取字段 + 类型校验 + 清晰报错"集中一处，供根对象 tl 与各句柄方法共用。
// 报错统一抛 ScriptApiException——宿主据其 Message 把干净的用法错误回报给脚本作者（含 agent 模型）。
internal static class ScriptArgs
{
    public static ObjectInstance Obj(JsValue v, string what)
    {
        if (v is null || !v.IsObject())
            throw new ScriptApiException(string.Format("{0} must be an object literal.", what));
        return v.AsObject();
    }

    public static bool Has(ObjectInstance o, string name, out JsValue v)
    {
        v = o.Get(name);
        return !v.IsUndefined() && !v.IsNull();
    }

    public static double ReqNum(ObjectInstance o, string name)
    {
        if (!Has(o, name, out var v) || !v.IsNumber())
            throw new ScriptApiException(string.Format("field \"{0}\" must be a number.", name));
        return v.AsNumber();
    }

    public static int ReqInt(ObjectInstance o, string name) => (int)Math.Round(ReqNum(o, name));
    public static double? OptNum(ObjectInstance o, string name) => Has(o, name, out var v) && v.IsNumber() ? v.AsNumber() : null;
    public static int? OptInt(ObjectInstance o, string name) => OptNum(o, name) is { } d ? (int)Math.Round(d) : null;
    public static bool? OptBool(ObjectInstance o, string name) => Has(o, name, out var v) && v.IsBoolean() ? v.AsBoolean() : null;

    public static string? OptStr(ObjectInstance o, string name)
    {
        if (!Has(o, name, out var v)) return null;
        return v.IsString() ? v.AsString() : v.ToString();
    }

    // 接受可空 JsValue：可选脚本参数在 C# 签名里写成 `JsValue? x = null`（Jint 对缺失尾参不自动补 undefined、
    // 只有形参带默认值才允许省略——见 ScriptArgs 各可选参用法），省略即传 CLR-null，这里同 undefined/null 处理。
    public static string? AsStrOrNull(JsValue? v) => v is null || v.IsUndefined() || v.IsNull() ? null : (v.IsString() ? v.AsString() : v.ToString());
    public static double? AsNumOrNull(JsValue? v) => v is not null && v.IsNumber() ? v.AsNumber() : null;
    public static int? AsIntOrNull(JsValue? v) => AsNumOrNull(v) is { } d ? (int)Math.Round(d) : null;
    public static bool? AsBoolOrNull(JsValue? v) => v is not null && v.IsBoolean() ? v.AsBoolean() : null;

    // 自定义属性（effect / note / part 的 Properties 容器）标量读：读【裸值】→ JS 友好的 double/bool/string；
    // 缺键 / 多值（多选合并态）/ Null 一律返回 null。用 out-success 重载读裸值——另一个 GetValue(key, default) 会
    // 拿 default 当类型门（存值类型与 default 不符即退回 default），值类型不定时会误伤，这里要"存了什么读什么"。
    public static object? ReadScalarProperty(DataPropertyObject props, string key)
    {
        var raw = props.GetValue(key, out bool success);
        if (!success || raw.IsNull() || raw.IsMultiple()) return null;
        if (raw.ToBoolean(out var b)) return b;
        if (raw.ToDouble(out var d)) return d;
        if (raw.ToString(out var s)) return s;
        return null;
    }

    // JS 值 → PropertyValue（仅 number / boolean / string）；其它类型报错。what = 报错文案里的属性域名。
    public static PropertyValue ToScalarProperty(JsValue value, string what)
    {
        if (value.IsBoolean()) return PropertyValue.Create(value.AsBoolean());
        if (value.IsNumber()) return PropertyValue.Create(value.AsNumber());
        if (value.IsString()) return PropertyValue.Create(value.AsString());
        throw new ScriptApiException(string.Format("{0} value must be a number, boolean, or string.", what));
    }

    // points = [{tick, value}]（绝对 tick / 参数绝对值）。返回 (X=tick, Y=value) 的点列表（未排序）。
    public static List<Point> ReadPoints(JsValue points)
    {
        var o = Obj(points, "points");
        var lenVal = o.Get("length");
        if (!lenVal.IsNumber())
            throw new ScriptApiException("points must be an array of {tick, value}.");
        int len = (int)lenVal.AsNumber();
        var list = new List<Point>(len);
        for (int i = 0; i < len; i++)
        {
            var p = Obj(o.Get(i.ToString(CultureInfo.InvariantCulture)), "point");
            list.Add(new Point(ReqNum(p, "tick"), ReqNum(p, "value")));
        }
        return list;
    }
}
