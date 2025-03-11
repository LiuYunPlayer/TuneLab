using System.Collections;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Foundation;

public class PropertyObject(IMap<string, PropertyValue>? properties = null) : IContainerValue, IMap<string, PropertyValue>
{
    public PropertyType Type => PropertyType.Object;
    public PropertyValue this[string key] { get => GetValue(key); set => SetValue(key, value); }
    public IReadOnlyCollection<string> Keys => mProperties == null ? [] : mProperties.Keys;
    public IReadOnlyCollection<PropertyValue> Values => mProperties == null ? [] : mProperties.Values;
    public int Count => mProperties == null ? 0 : mProperties.Count;

    public void Add(string key, PropertyValue value)
    {
        mProperties ??= new Map<string, PropertyValue>();
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

    public PropertyValue GetValue(string key, out bool success)
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

    public IEnumerator<IReadOnlyKeyValuePair<string, PropertyValue>> GetEnumerator()
    {
        return mProperties == null ? Enumerable.Empty<ReadOnlyKeyValuePair<string, PropertyValue>>().GetEnumerator() : mProperties.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void SetValue(string key, PropertyValue value)
    {
        mProperties ??= new Map<string, PropertyValue>();
        if (mProperties.ContainsKey(key))
            mProperties[key] = value;
        else
            mProperties.Add(key, value);
    }
    public PropertyValue GetValue(string key, PropertyValue defaultValue = default) => this.TryGetValue(key, out var value) ? value : defaultValue;
    public PropertyBoolean GetBoolean(string key, PropertyBoolean defaultValue) => this.TryGetValue(key, out var value) ? value.AsBoolean(defaultValue) : defaultValue;
    public PropertyNumber GetNumber(string key, PropertyNumber defaultValue) => this.TryGetValue(key, out var value) ? value.AsNumber(defaultValue) : defaultValue;
    public PropertyString GetString(string key, PropertyString defaultValue) => this.TryGetValue(key, out var value) ? value.AsString(defaultValue) : defaultValue;
    public PropertyArray GetArray(string key, PropertyArray defaultValue) => this.TryGetValue(key, out var value) ? value.AsArray(defaultValue) : defaultValue;
    public PropertyObject GetObject(string key, PropertyObject defaultValue) => this.TryGetValue(key, out var value) ? value.AsObject(defaultValue) : defaultValue;
    /*
    bool IEquatable<IPropertyValue>.Equals(IPropertyValue? other)
    {
        if (other is not PropertyObject property)
            return false;

        if (mProperties == property.mProperties)
            return true;

        if (mProperties == null || property.mProperties == null)
            return false;

        return mProperties.SequenceEqual(property.mProperties);
    }*/

    internal IMap<string, PropertyValue>? mProperties = properties;
}
