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

    public static string? AsStrOrNull(JsValue v) => v is null || v.IsUndefined() || v.IsNull() ? null : (v.IsString() ? v.AsString() : v.ToString());
    public static double? AsNumOrNull(JsValue v) => v is not null && v.IsNumber() ? v.AsNumber() : null;
    public static int? AsIntOrNull(JsValue v) => AsNumOrNull(v) is { } d ? (int)Math.Round(d) : null;

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
