using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TuneLab.Foundation;

[CollectionBuilder(typeof(MapBuilder), nameof(MapBuilder.Create))]
public class Map<TKey, TValue> : IMap<TKey, TValue> where TKey : notnull
{
    public static readonly IReadOnlyMap<TKey, TValue> Empty = EmptyMap<TKey, TValue>.Instance;

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

// 真不可变空 map：Map<K,V>.Empty 与空 PropertyObject 底层共用的进程级单例。独立类型（非 Map 子类）——
// 故 (Map<K,V>)Map<K,V>.Empty 下转型抛 InvalidCastException 而非静默拿到可变实例，杜绝
// 「下转型改写共享单例、污染全进程」。无任何写入面。
sealed class EmptyMap<TKey, TValue> : IReadOnlyMap<TKey, TValue> where TKey : notnull
{
    public static readonly EmptyMap<TKey, TValue> Instance = new();
    EmptyMap() { }

    public TValue this[TKey key] => throw new KeyNotFoundException();
    public IReadOnlyCollection<TKey> Keys => Array.Empty<TKey>();
    public IReadOnlyCollection<TValue> Values => Array.Empty<TValue>();
    public int Count => 0;
    public bool ContainsKey(TKey key) => false;
    public TValue? GetValue(TKey key, out bool success) { success = false; return default; }

    public IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> GetEnumerator() { yield break; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
