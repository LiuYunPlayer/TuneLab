using System.Collections;
using System.Runtime.CompilerServices;

namespace TuneLab.Foundation.DataStructures;

[CollectionBuilder(typeof(MapBuilder), nameof(MapBuilder.Create))]
public class Map<TKey, TValue> : IMap<TKey, TValue> where TKey : notnull
{
    public readonly static IReadOnlyMap<TKey, TValue> Empty = new Map<TKey, TValue>();

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

    public IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return mDictionary.GetEnumerator().Convert(ReadOnlyKeyValuePair<TKey, TValue>.FromSystem);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    readonly Dictionary<TKey, TValue> mDictionary = [];
}

public static class MapExtensions
{
    public static Map<TKey, TValue> ToMap<TKey, TValue>(this IReadOnlyCollection<IReadOnlyKeyValuePair<TKey, TValue>> kvps) where TKey : notnull
    {
        Map<TKey, TValue> map = [];
        foreach (var kvp in kvps)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }

    public static Map<TKey, TValue> ToMap<TKey, TValue>(this IReadOnlyMap<TKey, TValue> map) where TKey : notnull
    {
        return ((IReadOnlyCollection<IReadOnlyKeyValuePair<TKey, TValue>>)map).ToMap();
    }
}

public static class MapBuilder
{
    public static Map<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair<TKey, TValue>> values) where TKey : notnull
    {
        var map = new Map<TKey, TValue>();
        foreach (var kvp in values)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }
}
