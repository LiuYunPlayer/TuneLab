using LProp = TuneLab.Base.Properties;
using LStruct = TuneLab.Base.Structures;
using PProp = TuneLab.Primitives.Property;
using PStruct = TuneLab.Primitives.DataStructures;

namespace TuneLab.Hosting.Compat.Legacy.Conversion;

// PropertyObject / PropertyValue 跨代转换（DTO eager 深拷贝、转移所有权、无别名）。
// Legacy（TuneLab.Base.Properties，多 box）↔ V1（TuneLab.Primitives.Property，单 box + PropertyType）。
internal static class PropertyConvert
{
    public static PProp.PropertyObject ToV1(this LProp.PropertyObject old)
    {
        var map = new PStruct.Map<string, PProp.PropertyValue>();
        foreach (var kv in old.Map)
            map[kv.Key] = kv.Value.ToV1();
        return new PProp.PropertyObject(map);
    }

    public static LProp.PropertyObject ToLegacy(this PProp.PropertyObject neo)
    {
        var map = new LStruct.Map<string, LProp.PropertyValue>();
        foreach (var kv in neo.Map)
            map[kv.Key] = kv.Value.ToLegacy();
        return new LProp.PropertyObject(map);
    }

    public static PProp.PropertyValue ToV1(this LProp.PropertyValue old)
    {
        if (old.ToBool(out var b)) return b;
        if (old.ToDouble(out var d)) return d;
        if (old.ToString(out var s)) return s ?? string.Empty;
        if (old.ToObject(out var o)) return PProp.PropertyValue.Create(o.ToV1());
        return PProp.PropertyValue.Null;
    }

    public static LProp.PropertyValue ToLegacy(this PProp.PropertyValue neo)
    {
        if (neo.ToBool(out var b)) return b;
        if (neo.ToDouble(out var d)) return d;
        if (neo.ToString(out var s)) return s ?? string.Empty;
        if (neo.ToObject(out var o)) return LProp.PropertyValue.Create(o.ToLegacy());
        return LProp.PropertyValue.Invalid;
    }
}
