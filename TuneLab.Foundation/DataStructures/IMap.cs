namespace TuneLab.Foundation.DataStructures;

public interface IMap<TKey, TValue> : IReadOnlyMap<TKey, TValue> where TKey : notnull
{
    new TValue this[TKey key] { get; set; }
    void Add(TKey key, TValue value);
    bool Remove(TKey key);
    void Clear();
}
