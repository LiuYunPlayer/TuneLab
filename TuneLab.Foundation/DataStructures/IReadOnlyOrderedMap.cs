using System.Runtime.CompilerServices;

namespace TuneLab.Foundation.DataStructures;

[CollectionBuilder(typeof(OrderedMapBuilder), nameof(OrderedMapBuilder.Create))]
public interface IReadOnlyOrderedMap<TKey, out TValue> : IReadOnlyMap<TKey, TValue>, IReadOnlyList<IReadOnlyKeyValuePair<TKey, TValue>> where TKey : notnull
{
    new IReadOnlyList<TKey> Keys { get; }
    new IReadOnlyList<TValue> Values { get; }

    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;
}
