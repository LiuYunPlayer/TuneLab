using System.Collections;
using System.Diagnostics.CodeAnalysis;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.SDK.Base.Property;

public class PropertyObject_V1(IMap_V1<string, PropertyValue_V1>? properties = null) : IContainerValue_V1, IMap_V1<string, PropertyValue_V1>
{
    public PropertyValue_V1 this[string key] { get => GetValue(key); set => SetValue(key, value); }
    public IReadOnlyCollection<string> Keys => mProperties == null ? [] : mProperties.Keys;
    public IReadOnlyCollection<PropertyValue_V1> Values => mProperties == null ? [] : mProperties.Values;
    public int Count => mProperties == null ? 0 : mProperties.Count;

    public void Add(string key, PropertyValue_V1 value)
    {
        mProperties ??= new Map_V1<string, PropertyValue_V1>();
        mProperties.Add(key, value);
    }

    public bool ContainsKey(string key)
    {
        return mProperties != null && mProperties.ContainsKey(key);
    }

    public bool Remove(string key)
    {
        return mProperties != null && mProperties.Remove(key);
    }

    public PropertyValue_V1 GetValue(string key, [MaybeNullWhen(false)] out bool success)
    {
        if (mProperties == null)
        {
            success = false;
            return default;
        }

        return mProperties.GetValue(key, out success);
    }

    public void Clear()
    {
        mProperties?.Clear();
    }

    public IEnumerator<IReadOnlyKeyValuePair_V1<string, PropertyValue_V1>> GetEnumerator()
    {
        return mProperties == null ? Enumerable.Empty<IReadOnlyKeyValuePair_V1<string, PropertyValue_V1>>().GetEnumerator() : mProperties.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void SetValue(string key, PropertyValue_V1 value)
    {
        mProperties ??= new Map_V1<string, PropertyValue_V1>();
        if (mProperties.ContainsKey(key))
            mProperties[key] = value;
        else
            mProperties.Add(key, value);
    }
    public PropertyValue_V1 GetValue(string key, PropertyValue_V1 defaultValue = default) => this.TryGetValue(key, out var value) ? value : defaultValue;
    public PropertyBoolean_V1 GetBoolean(string key, PropertyBoolean_V1 defaultValue) => this.TryGetValue(key, out var value) ? value.AsBoolean(defaultValue) : defaultValue;
    public PropertyNumber_V1 GetNumber(string key, PropertyNumber_V1 defaultValue) => this.TryGetValue(key, out var value) ? value.AsNumber(defaultValue) : defaultValue;
    public PropertyString_V1 GetString(string key, PropertyString_V1 defaultValue) => this.TryGetValue(key, out var value) ? value.AsString(defaultValue) : defaultValue;
    public PropertyArray_V1 GetArray(string key, PropertyArray_V1 defaultValue) => this.TryGetValue(key, out var value) ? value.AsArray(defaultValue) : defaultValue;
    public PropertyObject_V1 GetObject(string key, PropertyObject_V1 defaultValue) => this.TryGetValue(key, out var value) ? value.AsObject(defaultValue) : defaultValue;
    /*
    bool IEquatable<IPropertyValue_V1>.Equals(IPropertyValue_V1? other)
    {
        if (other is not PropertyObject_V1 property)
            return false;

        if (mProperties == property.mProperties)
            return true;

        if (mProperties == null || property.mProperties == null)
            return false;

        return mProperties.SequenceEqual(property.mProperties);
    }*/

    IMap_V1<string, PropertyValue_V1>? mProperties = properties;
}
