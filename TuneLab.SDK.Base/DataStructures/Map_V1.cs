using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base.DataStructures;

[CollectionBuilder(typeof(Map_V1Builder), nameof(Map_V1Builder.Create))]
public class Map_V1<TKey, TValue> : IMap_V1<TKey, TValue> where TKey : notnull
{
    public readonly static IReadOnlyMap_V1<TKey, TValue> Empty = new Map_V1<TKey, TValue>();

    public TValue this[TKey key] { get => mDictionary[key]; set => mDictionary[key] = value; }
    public IReadOnlyCollection<TKey> Keys => mDictionary.Keys;
    public IReadOnlyCollection<TValue> Values => mDictionary.Values;
    public int Count => mDictionary.Count;

    public TValue? GetValue(TKey key, out bool success)
    {
        success = mDictionary.TryGetValue(key, out var value);
        return value;
    }

    public void Add(TKey key, TValue value)
    {
        mDictionary.Add(key, value);
    }

    public bool Remove(TKey key)
    {
        return mDictionary.Remove(key);
    }

    public void Clear()
    {
        mDictionary.Clear();
    }

    public bool ContainsKey(TKey key)
    {
        return mDictionary.ContainsKey(key);
    }

    public IEnumerator<IReadOnlyKeyValuePair_V1<TKey, TValue>> GetEnumerator()
    {
        return mDictionary.Select(ReadOnlyKeyValuePair_V1<TKey, TValue>.FromSystem).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    readonly Dictionary<TKey, TValue> mDictionary = [];
}

public static class Map_V1Extensions
{
    public static Map_V1<TKey, TValue> ToMap_V1<TKey, TValue>(this IReadOnlyCollection<IReadOnlyKeyValuePair_V1<TKey, TValue>> kvps) where TKey : notnull
    {
        Map_V1<TKey, TValue> map = [];
        foreach (var kvp in kvps)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }

    public static Map_V1<TKey, TValue> ToMap<TKey, TValue>(this IReadOnlyMap_V1<TKey, TValue> map) where TKey : notnull
    {
        return ((IReadOnlyCollection<IReadOnlyKeyValuePair_V1<TKey, TValue>>)map).ToMap_V1();
    }
}

public static class Map_V1Builder
{
    public static Map_V1<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair_V1<TKey, TValue>> values) where TKey : notnull
    {
        var map = new Map_V1<TKey, TValue>();
        foreach (var kvp in values)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }
}