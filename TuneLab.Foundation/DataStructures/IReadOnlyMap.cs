using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TuneLab.Foundation;

[CollectionBuilder(typeof(IReadOnlyMapBuilder), nameof(IReadOnlyMapBuilder.Create))]
public interface IReadOnlyMap<TKey, out TValue> : IReadOnlyCollection<IReadOnlyKeyValuePair<TKey, TValue>> where TKey : notnull
{
    TValue this[TKey key] { get; }
    IReadOnlyCollection<TKey> Keys { get; }
    IReadOnlyCollection<TValue> Values { get; }
    bool ContainsKey(TKey key);
    TValue? GetValue(TKey key, out bool success);
}

public static class IReadOnlyMapExtension
{
    public static bool TryGetValue<TKey, TValue>(this IReadOnlyMap<TKey, TValue> map, TKey key, [MaybeNullWhen(false)] out TValue value) where TKey : notnull
    {
        value = map.GetValue(key, out var success);
        return success;
    }

    public static Map<TKey, TValue> ToMap<TKey, TValue>(this IReadOnlyMap<TKey, TValue> map) where TKey : notnull
    {
        Map<TKey, TValue> newMap = new();
        foreach (var kvp in map)
        {
            newMap.Add(kvp.Key, kvp.Value);
        }
        return newMap;
    }
}

public static class IReadOnlyMapBuilder
{
    public static IReadOnlyMap<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair<TKey, TValue>> values) where TKey : notnull
    {
        if (values.IsEmpty)
            return Map<TKey, TValue>.Empty;

        var map = new Map<TKey, TValue>();
        foreach (var kvp in values)
            map.Add(kvp.Key, kvp.Value);
        return map;
    }
}
