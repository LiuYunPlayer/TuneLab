using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TuneLab.Foundation;

[CollectionBuilder(typeof(MapBuilder), nameof(MapBuilder.Create))]
public class Map<TKey, TValue> : IMap<TKey, TValue> where TKey : notnull
{
    public static readonly IReadOnlyMap<TKey, TValue> Empty = new Map<TKey, TValue>();

    public TValue this[TKey key] { get => mDictionary[key]; set => mDictionary[key] = value; }
    public IReadOnlyCollection<TKey> Keys => mDictionary.Keys;
    public IReadOnlyCollection<TValue> Values => mDictionary.Values;
    public int Count => mDictionary.Count;

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

    public TValue? GetValue(TKey key, out bool success)
    {
        success = mDictionary.TryGetValue(key, out var value);
        return value;
    }

    public IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var kvp in mDictionary)
            yield return new ReadOnlyKeyValuePair<TKey, TValue>(kvp.Key, kvp.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    readonly Dictionary<TKey, TValue> mDictionary = new();
}

public static class MapBuilder
{
    public static Map<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair<TKey, TValue>> values) where TKey : notnull
    {
        var map = new Map<TKey, TValue>();
        foreach (var kvp in values)
            map.Add(kvp.Key, kvp.Value);
        return map;
    }
}
