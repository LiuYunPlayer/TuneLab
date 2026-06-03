using System;
using TuneLab.Primitives.DataStructures;

namespace TuneLab.Primitives.Property;

public sealed class PropertyObject(IReadOnlyMap<string, PropertyValue> map) : IEquatable<PropertyObject>
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
        return (int)Math.Round(GetDouble(key, defaultValue));
    }

    public T GetEnum<T>(string key, T defaultValue = default) where T : struct, Enum
    {
        var name = GetValue(key, string.Empty);
        return Enum.TryParse<T>(name, out var result) ? result : defaultValue;
    }

    // 深相等性：同键集 + 每个 PropertyValue 深比较，支撑 undo 去重。
    public bool Equals(PropertyObject? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (map.Count != other.Map.Count)
            return false;

        foreach (var kvp in map)
        {
            if (!other.Map.TryGetValue(kvp.Key, out var otherValue))
                return false;
            if (!kvp.Value.Equals(otherValue))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is PropertyObject other && Equals(other);
    }

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var kvp in map)
        {
            unchecked
            {
                hash += kvp.Key.GetHashCode() ^ kvp.Value.GetHashCode();
            }
        }
        return hash;
    }
}
