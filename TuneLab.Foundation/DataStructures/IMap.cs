using System.Runtime.CompilerServices;

namespace TuneLab.Foundation.DataStructures;

[CollectionBuilder(typeof(MapBuilder), nameof(MapBuilder.Create))]
public interface IMap<TKey, TValue> : IReadOnlyMap<TKey, TValue> where TKey : notnull
{
    new TValue this[TKey key] { get; set; }
    void Add(TKey key, TValue value);
    bool Remove(TKey key);
    void Clear();
}
