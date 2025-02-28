using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

public class PropertyObject : IContainerValue, IDictionary<string, PropertyValue>
{
    public PropertyValue this[string key] { get => GetValue(key); set => SetValue(key, value); }
    public ICollection<string> Keys => mProperties == null ? [] : ((IDictionary<string, PropertyValue>)mProperties).Keys;
    public ICollection<PropertyValue> Values => mProperties == null ? Array.Empty<PropertyValue>() : ((IDictionary<string, PropertyValue>)mProperties).Values;
    public int Count => mProperties == null ? 0 : ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).Count;
    public bool IsReadOnly => mProperties == null ? true : ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).IsReadOnly;

    public void Add(string key, PropertyValue value)
    {
        mProperties ??= [];
        ((IDictionary<string, PropertyValue>)mProperties).Add(key, value);
    }

    public bool ContainsKey(string key)
    {
        return mProperties != null && ((IDictionary<string, PropertyValue>)mProperties).ContainsKey(key);
    }

    public bool Remove(string key)
    {
        return mProperties != null && ((IDictionary<string, PropertyValue>)mProperties).Remove(key);
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out PropertyValue value)
    {
        if (mProperties == null)
        {
            value = default;
            return false;
        }

        return ((IDictionary<string, PropertyValue>)mProperties).TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<string, PropertyValue> item)
    {
        mProperties ??= [];
        ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).Add(item);
    }

    public void Clear()
    {
        mProperties?.Clear();
    }

    public bool Contains(KeyValuePair<string, PropertyValue> item)
    {
        return mProperties != null && ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).Contains(item);
    }

    public void CopyTo(KeyValuePair<string, PropertyValue>[] array, int arrayIndex)
    {
        if (mProperties == null)
            return;

        ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<string, PropertyValue> item)
    {
        return mProperties != null && ((ICollection<KeyValuePair<string, PropertyValue>>)mProperties).Remove(item);
    }

    public IEnumerator<KeyValuePair<string, PropertyValue>> GetEnumerator()
    {
        return mProperties == null ? Enumerable.Empty<KeyValuePair<string, PropertyValue>>().GetEnumerator() : ((IEnumerable<KeyValuePair<string, PropertyValue>>)mProperties).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void SetValue(string key, PropertyValue value)
    {
        mProperties ??= [];
        if (mProperties.ContainsKey(key))
            mProperties["key"] = value;
        else
            mProperties.Add(key, value);
    }
    public PropertyValue GetValue(string key, PropertyValue defaultValue = default) => TryGetValue(key, out var value) ? value : defaultValue;
    public PropertyBoolean GetBoolean(string key, PropertyBoolean defaultValue) => TryGetValue(key, out var value) ? value.AsBoolean(defaultValue) : defaultValue;
    public PropertyNumber GetNumber(string key, PropertyNumber defaultValue) => TryGetValue(key, out var value) ? value.AsNumber(defaultValue) : defaultValue;
    public PropertyString GetString(string key, PropertyString defaultValue) => TryGetValue(key, out var value) ? value.AsString(defaultValue) : defaultValue;
    public PropertyArray GetArray(string key, PropertyArray defaultValue) => TryGetValue(key, out var value) ? value.AsArray(defaultValue) : defaultValue;
    public PropertyObject GetObject(string key, PropertyObject defaultValue) => TryGetValue(key, out var value) ? value.AsObject(defaultValue) : defaultValue;

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

    Dictionary<string, PropertyValue>? mProperties = null;
}
