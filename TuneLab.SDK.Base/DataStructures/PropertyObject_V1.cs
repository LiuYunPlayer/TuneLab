using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base.DataStructures;

public struct PropertyObject_V1 : IDictionary<string, PropertyValue_V1>
{
    public PropertyValue_V1 this[string key] { get => ((IDictionary<string, PropertyValue_V1>)mProperties!)[key]; set => ((IDictionary<string, PropertyValue_V1>)mProperties!)[key] = value; }
    public ICollection<string> Keys => mProperties == null ? Array.Empty<string>() : ((IDictionary<string, PropertyValue_V1>)mProperties).Keys;
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

    public PropertyBoolean_V1 GetBoolean(string key, PropertyBoolean_V1 defaultValue = default) => TryGetValue(key, out var value) ? value.ToBoolean(out var result) ? result : defaultValue : defaultValue;
    public PropertyNumber_V1 GetNumber(string key, PropertyNumber_V1 defaultValue = default) => TryGetValue(key, out var value) ? value.ToNumber(out var result) ? result : defaultValue : defaultValue;
    public PropertyString_V1 GetString(string key, PropertyString_V1 defaultValue = default) => TryGetValue(key, out var value) ? value.ToString(out var result) ? result : defaultValue : defaultValue;
    public PropertyArray_V1 GetArray(string key, PropertyArray_V1 defaultValue = default) => TryGetValue(key, out var value) ? value.ToArray(out var result) ? result : defaultValue : defaultValue;
    public PropertyObject_V1 GetObject(string key, PropertyObject_V1 defaultValue = default) => TryGetValue(key, out var value) ? value.ToObject(out var result) ? result : defaultValue : defaultValue;

    Dictionary<string, PropertyValue_V1>? mProperties;
}
