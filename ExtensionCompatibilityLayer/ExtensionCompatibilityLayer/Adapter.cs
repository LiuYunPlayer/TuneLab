using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Property;

namespace ExtensionCompatibilityLayer;

internal static class Adapter
{
    // Map<string, T> 转换（辅助方法）
    public static TuneLab.Foundation.DataStructures.Map<string, TNew> ToCoreFormat<TOld, TNew>(this TuneLab.Base.Structures.Map<string, TOld> oldMap, System.Func<TOld, TNew> convert)
    {
        var newMap = new TuneLab.Foundation.DataStructures.Map<string, TNew>();
        foreach (var kv in oldMap)
            newMap[kv.Key] = convert(kv.Value);
        return newMap;
    }
    public static TuneLab.Base.Structures.Map<string, TOld> ToOldFormat<TOld, TNew>(this TuneLab.Foundation.DataStructures.Map<string, TNew> newMap, System.Func<TNew, TOld> convert)
    {
        var oldMap = new TuneLab.Base.Structures.Map<string, TOld>();
        foreach (var kv in newMap)
            oldMap[kv.Key] = convert(kv.Value);
        return oldMap;
    }

    public static TuneLab.Foundation.DataStructures.Map<string, T> ToCoreFormat<T>(this TuneLab.Base.Structures.Map<string, T> oldMap)
    {
        var newMap = new TuneLab.Foundation.DataStructures.Map<string, T>();
        foreach (var kv in oldMap)
            newMap[kv.Key] = kv.Value;
        return newMap;
    }
    public static TuneLab.Base.Structures.Map<string, double> ToOldFormat(this TuneLab.Foundation.DataStructures.Map<string, double> newMap)
    {
        var oldMap = new TuneLab.Base.Structures.Map<string, double>();
        foreach (var kv in newMap)
            oldMap[kv.Key] = kv.Value;
        return oldMap;
    }

    // List<Point> 转换
    public static List<TuneLab.Foundation.DataStructures.Point> ToCoreFormat(this List<TuneLab.Base.Structures.Point> oldList)
        => oldList.Select(p => p.ToCoreFormat()).ToList();
    public static List<TuneLab.Base.Structures.Point> ToOldFormat(this List<TuneLab.Foundation.DataStructures.Point> newList)
        => newList.Select(p => p.ToOldFormat()).ToList();

    // Point 转换
    public static TuneLab.Foundation.DataStructures.Point ToCoreFormat(this TuneLab.Base.Structures.Point oldObj)
        => new(oldObj.X, oldObj.Y);
    public static TuneLab.Base.Structures.Point ToOldFormat(this TuneLab.Foundation.DataStructures.Point newObj)
        => new(newObj.X, newObj.Y);

    // PropertyObject: Base -> Foundation
    public static TuneLab.Foundation.Property.PropertyObject ToCoreFormat(this TuneLab.Base.Properties.PropertyObject oldObj)
    {
        var dict = new TuneLab.Foundation.Property.PropertyObject();
        foreach (var kv in oldObj.Map)
        {
            dict[kv.Key] = kv.Value.ToCoreFormat();
        }
        return new TuneLab.Foundation.Property.PropertyObject(dict);
    }

    // PropertyObject: Foundation -> Base
    public static TuneLab.Base.Properties.PropertyObject ToOldFormat(this TuneLab.Foundation.Property.PropertyObject newObj)
    {
        var dict = new TuneLab.Base.Structures.Map<string, TuneLab.Base.Properties.PropertyValue>();
        foreach (var kv in newObj)
        {
            dict[kv.Key] = kv.Value.ToOldFormat();
        }
        return new TuneLab.Base.Properties.PropertyObject(dict);
    }

    // Base -> Foundation
    public static TuneLab.Foundation.Property.IPropertyValue ToCoreFormat(this TuneLab.Base.Properties.PropertyValue oldValue)
    {
        if (oldValue.ToBool(out var b))
            return new PropertyBoolean(b);
        if (oldValue.ToDouble(out var d))
            return new PropertyNumber(d);
        if (oldValue.ToString(out var s))
            return new PropertyString(s ?? string.Empty);
        if (oldValue.ToObject(out var obj))
            return obj.ToCoreFormat();

        return TuneLab.Foundation.Property.PropertyNull.Shared; // Foundation的PropertyValue无效值可用null或默认
    }

    // Foundation -> Base
    public static TuneLab.Base.Properties.PropertyValue ToOldFormat(this TuneLab.Foundation.Property.IPropertyValue newValue)
    {
        if (newValue.IsNull())
            return TuneLab.Base.Properties.PropertyValue.Invalid;
        if (newValue.ToBoolean(out var b))
            return TuneLab.Base.Properties.PropertyValue.Create(b);
        if (newValue.ToNumber(out var n))
            return TuneLab.Base.Properties.PropertyValue.Create(n);
        if (newValue.ToString(out var s))
            return TuneLab.Base.Properties.PropertyValue.Create(s);
        if (newValue.ToObject(out var o))
            return TuneLab.Base.Properties.PropertyValue.Create(new PropertyObject(o).ToOldFormat());

        return TuneLab.Base.Properties.PropertyValue.Invalid;
    }
}
