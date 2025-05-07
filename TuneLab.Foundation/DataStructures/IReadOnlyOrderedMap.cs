using System.Runtime.CompilerServices;

namespace TuneLab.Foundation.DataStructures;

[CollectionBuilder(typeof(IReadOnlyOrderedMapBuilder), nameof(IReadOnlyOrderedMapBuilder.Create))]
public interface IReadOnlyOrderedMap<TKey, out TValue> : IReadOnlyMap<TKey, TValue>, IReadOnlyList<IReadOnlyKeyValuePair<TKey, TValue>> where TKey : notnull
{
    new IReadOnlyList<TKey> Keys { get; }
    new IReadOnlyList<TValue> Values { get; }

    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;
}

public static class IReadOnlyOrderedMapBuilder
{
    public static IReadOnlyOrderedMap<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair<TKey, TValue>> values) where TKey : notnull
    {
        if (values.IsEmpty)
            return EmptyOrderedMap<TKey, TValue>.Value;

        var map = new OrderedMap<TKey, TValue>();
        foreach (var kvp in values)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }

    class EmptyOrderedMap<TKey, TValue> where TKey : notnull
    {
        public static readonly IReadOnlyOrderedMap<TKey, TValue> Value = new OrderedMap<TKey, TValue>();
    }
}