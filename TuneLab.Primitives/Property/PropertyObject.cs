using System;
using TuneLab.Primitives.DataStructures;

namespace TuneLab.Primitives.Property;

// 纯值对象（深相等 + 哈希支撑 undo 去重）：构造时拷入自持第一层键值对，此后与传入 map 的
// 任何变化无关——值语义由构造保证而非调用方纪律。嵌套的 PropertyObject 元素在其自身构造时
// 已拷过自己的第一层（标量值本就不可变），逐层各拷一层即归纳封死整树，无需深拷。
public sealed class PropertyObject : IEquatable<PropertyObject>
{
    public readonly static PropertyObject Empty = new(Map<string, PropertyValue>.Empty);

    public PropertyObject(IReadOnlyMap<string, PropertyValue> map)
    {
        var copy = new Map<string, PropertyValue>();
        foreach (var kvp in map)
        {
            copy.Add(kvp.Key, kvp.Value);
        }
        mMap = copy;
    }

    public IReadOnlyMap<string, PropertyValue> Map => mMap;

    public T GetValue<T>(string key, T defaultValue) where T : notnull
    {
        if (!mMap.TryGetValue(key, out var value))
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
        if (mMap.Count != other.mMap.Count)
            return false;

        foreach (var kvp in mMap)
        {
            if (!other.mMap.TryGetValue(kvp.Key, out var otherValue))
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
        foreach (var kvp in mMap)
        {
            unchecked
            {
                hash += kvp.Key.GetHashCode() ^ kvp.Value.GetHashCode();
            }
        }
        return hash;
    }

    readonly Map<string, PropertyValue> mMap;
}
