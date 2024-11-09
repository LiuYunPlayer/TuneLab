using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class PropertyObject_V1 : IContainerValue_V1, IDictionary<string, PropertyValue_V1>
{
    public PropertyValue_V1 this[string key] { get => GetValue(key); set => SetValue(key, value); }
    public ICollection<string> Keys => mProperties == null ? [] : ((IDictionary<string, PropertyValue_V1>)mProperties).Keys;
    public ICollection<PropertyValue_V1> Values => mProperties == null ? Array.Empty<PropertyValue_V1>() : ((IDictionary<string, PropertyValue_V1>)mProperties).Values;
    public int Count => mProperties == null ? 0 : ((ICollection<KeyValuePair<string, PropertyValue_V1>>)mProperties).Count;
    public bool IsReadOnly => mProperties == null ? true : ((ICollection<KeyValuePair<string, PropertyValue_V1>>)mProperties).IsReadOnly;

    public void Add(string key, PropertyValue_V1 value)
    {
        mProperties ??= [];
        ((IDictionary<string, PropertyValue_V1>)mProperties).Add(key, value);
    }

    public bool ContainsKey(string key)
    {
        return mProperties != null && ((IDictionary<string, PropertyValue_V1>)mProperties).ContainsKey(key);
    }

    public bool Remove(string key)
    {
        return mProperties != null && ((IDictionary<string, PropertyValue_V1>)mProperties).Remove(key);
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out PropertyValue_V1 value)
    {
        if (mProperties == null)
        {
            value = default;
            return false;
        }

        return ((IDictionary<string, PropertyValue_V1>)mProperties).TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<string, PropertyValue_V1> item)
    {
        mProperties ??= [];
        ((ICollection<KeyValuePair<string, PropertyValue_V1>>)mProperties).Add(item);
    }

    public void Clear()
    {
        mProperties?.Clear();
    }

    public bool Contains(KeyValuePair<string, PropertyValue_V1> item)
    {
        return mProperties != null && ((ICollection<KeyValuePair<string, PropertyValue_V1>>)mProperties).Contains(item);
    }

    public void CopyTo(KeyValuePair<string, PropertyValue_V1>[] array, int arrayIndex)
    {
        if (mProperties == null)
            return;

        ((ICollection<KeyValuePair<string, PropertyValue_V1>>)mProperties).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<string, PropertyValue_V1> item)
    {
        return mProperties != null && ((ICollection<KeyValuePair<string, PropertyValue_V1>>)mProperties).Remove(item);
    }

    public IEnumerator<KeyValuePair<string, PropertyValue_V1>> GetEnumerator()
    {
        return mProperties == null ? Enumerable.Empty<KeyValuePair<string, PropertyValue_V1>>().GetEnumerator() : ((IEnumerable<KeyValuePair<string, PropertyValue_V1>>)mProperties).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void SetValue(string key, PropertyValue_V1 value)
    {
        mProperties ??= [];
        if (mProperties.ContainsKey(key))
            mProperties["key"] = value;
        else
            mProperties.Add(key, value);
    }
    public PropertyValue_V1 GetValue(string key, PropertyValue_V1 defaultValue = default) => TryGetValue(key, out var value) ? value : defaultValue;
    public PropertyBoolean_V1 GetBoolean(string key, PropertyBoolean_V1 defaultValue) => TryGetValue(key, out var value) ? value.AsBoolean(defaultValue) : defaultValue;
    public PropertyNumber_V1 GetNumber(string key, PropertyNumber_V1 defaultValue) => TryGetValue(key, out var value) ? value.AsNumber(defaultValue) : defaultValue;
    public PropertyString_V1 GetString(string key, PropertyString_V1 defaultValue) => TryGetValue(key, out var value) ? value.AsString(defaultValue) : defaultValue;
    public PropertyArray_V1 GetArray(string key, PropertyArray_V1 defaultValue) => TryGetValue(key, out var value) ? value.AsArray(defaultValue) : defaultValue;
    public PropertyObject_V1 GetObject(string key, PropertyObject_V1 defaultValue) => TryGetValue(key, out var value) ? value.AsObject(defaultValue) : defaultValue;

    bool IEquatable<IPropertyValue_V1>.Equals(IPropertyValue_V1? other)
    {
        if (other is not PropertyObject_V1 property)
            return false;

        if (mProperties == property.mProperties)
            return true;

        if (mProperties == null || property.mProperties == null)
            return false;

        return mProperties.SequenceEqual(property.mProperties);
    }

    Dictionary<string, PropertyValue_V1>? mProperties = null;
}
