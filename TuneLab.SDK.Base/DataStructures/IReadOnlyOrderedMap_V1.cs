using System.Runtime.CompilerServices;

namespace TuneLab.SDK.Base.DataStructures;

[CollectionBuilder(typeof(OrderedMap_V1Builder), nameof(OrderedMap_V1Builder.Create))]
public interface IReadOnlyOrderedMap_V1<TKey, out TValue> : IReadOnlyMap_V1<TKey, TValue>, IReadOnlyList<IReadOnlyKeyValuePair_V1<TKey, TValue>> where TKey : notnull
{
    new IReadOnlyList<TKey> Keys { get; }
    new IReadOnlyList<TValue> Values { get; }

    IReadOnlyCollection<TKey> IReadOnlyMap_V1<TKey, TValue>.Keys => Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap_V1<TKey, TValue>.Values => Values;
}
