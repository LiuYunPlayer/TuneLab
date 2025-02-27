using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.SDK.Base;

namespace TuneLab.Base.Properties;

public sealed class PropertyObject(IReadOnlyMap<string, PropertyValue> map)
{
    public readonly static PropertyObject Empty = new(Map<string, PropertyValue>.Empty);

    public IReadOnlyMap<string, PropertyValue> Map => map;

    public T GetValue<T>(string key, T defaultValue) where T : notnull
    {
        if (map == null)
            return defaultValue;

        if (!map.TryGetValue(key, out var value))
            return defaultValue;

        if (!value.To<T>(out var result))
            return defaultValue;

        return result;
    }

    public PropertyObject GetObject(string key)
    {
        return GetValue(key, Empty);
    }

    public PropertyObject GetObject(string key, PropertyObject defaultValue)
    {
        return GetValue(key, defaultValue);
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        return GetValue(key, defaultValue);
    }

    public double GetDouble(string key, double defaultValue = 0)
    {
        return GetValue(key, defaultValue);
    }

    public string GetString(string key, string defaultValue = "")
    {
        return GetValue(key, defaultValue);
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        return GetDouble(key, defaultValue).Round();
    }

    public T GetEnum<T>(string key, T defaultValue = default) where T : struct, Enum
    {
        return GetValue(key, string.Empty).ToEnum(defaultValue);
    }

    // V1 Adapter
    public static implicit operator PropertyObject_V1(PropertyObject propertyObject)
    {
        PropertyObject_V1 propertyObject_V1 = [];
        foreach (var property in propertyObject.Map)
        {
            propertyObject_V1.Add(property.Key, property.Value);
        }
        return propertyObject_V1;
    }
}
