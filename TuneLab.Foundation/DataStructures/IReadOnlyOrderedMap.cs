using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TuneLab.Foundation;

[CollectionBuilder(typeof(IReadOnlyOrderedMapBuilder), nameof(IReadOnlyOrderedMapBuilder.Create))]
public interface IReadOnlyOrderedMap<TKey, out TValue> : IReadOnlyMap<TKey, TValue>, IReadOnlyList<IReadOnlyKeyValuePair<TKey, TValue>> where TKey : notnull
{
    // 有序版收紧：顺序注册表的 Keys/Values 是有索引的 IReadOnlyList。
    new IReadOnlyList<TKey> Keys { get; }
    new IReadOnlyList<TValue> Values { get; }
    TKey KeyAt(int index);
    TValue ValueAt(int index);
}

public static class IReadOnlyOrderedMapBuilder
{
    public static IReadOnlyOrderedMap<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair<TKey, TValue>> values) where TKey : notnull
    {
        if (values.IsEmpty)
            return EmptyOrderedMap<TKey, TValue>.Value;

        var map = new OrderedMap<TKey, TValue>();
        foreach (var kvp in values)
            map.Add(kvp.Key, kvp.Value);
        return map;
    }

    static class EmptyOrderedMap<TKey, TValue> where TKey : notnull
    {
        public static readonly IReadOnlyOrderedMap<TKey, TValue> Value = new OrderedMap<TKey, TValue>();
    }
}
