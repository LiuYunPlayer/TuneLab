using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation;

public class PropertyObject(IMap<string, PropertyValue> properties) : IContainerValue, IMap<string, PropertyValue>
{
    public PropertyValue this[string key] { get => GetValue(key); set => SetValue(key, value); }
    public IReadOnlyCollection<string> Keys => mProperties == null ? [] : mProperties.Keys;
    public IReadOnlyCollection<PropertyValue> Values => mProperties == null ? Array.Empty<PropertyValue>() : mProperties.Values;
    public int Count => mProperties == null ? 0 : ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).Count;
    public bool IsReadOnly => mProperties == null ? true : ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).IsReadOnly;

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

    public IEnumerator<IReadOnlyKeyWithValue<string, PropertyValue>> GetEnumerator()
    {
        return mProperties == null ? Enumerable.Empty<KeyWithValue<string, PropertyValue>>().GetEnumerator() : mProperties.GetEnumerator();
    }
    
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void SetValue(string key, PropertyValue value)
    {
        mProperties ??= new Map<string, PropertyValue>();
        if (mProperties.ContainsKey(key))
            mProperties["key"] = value;
        else
            mProperties.Add(key, value);
    }
    public PropertyValue GetValue(string key, PropertyValue defaultValue = default) => this.TryGetValue(key, out var value) ? value : defaultValue;
    public PropertyBoolean GetBoolean(string key, PropertyBoolean defaultValue) => this.TryGetValue(key, out var value) ? value.AsBoolean(defaultValue) : defaultValue;
    public PropertyNumber GetNumber(string key, PropertyNumber defaultValue) => this.TryGetValue(key, out var value) ? value.AsNumber(defaultValue) : defaultValue;
    public PropertyString GetString(string key, PropertyString defaultValue) => this.TryGetValue(key, out var value) ? value.AsString(defaultValue) : defaultValue;
    public PropertyArray GetArray(string key, PropertyArray defaultValue) => this.TryGetValue(key, out var value) ? value.AsArray(defaultValue) : defaultValue;
    public PropertyObject GetObject(string key, PropertyObject defaultValue) => this.TryGetValue(key, out var value) ? value.AsObject(defaultValue) : defaultValue;

    bool IEquatable<IPropertyValue>.Equals(IPropertyValue? other)
    {
        if (other is not PropertyObject property)
            return false;

        if (mProperties == property.mProperties)
            return true;

        if (mProperties == null || property.mProperties == null)
            return false;

        return mProperties.SequenceEqual(property.mProperties);
    }

    internal IMap<string, PropertyValue>? mProperties = properties;
}
