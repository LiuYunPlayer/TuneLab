namespace TuneLab.SDK.Base;

public interface IMap_V1<TKey, TValue> : IReadOnlyMap_V1<TKey, TValue> where TKey : notnull
{
    new TValue this[TKey key] { get; set; }
    void Add(TKey key, TValue value);
    bool Remove(TKey key);
    void Clear();
}
