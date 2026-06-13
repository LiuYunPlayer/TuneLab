namespace TuneLab.Foundation;

public interface IOrderedMap<TKey, TValue> : IMap<TKey, TValue>, IReadOnlyOrderedMap<TKey, TValue> where TKey : notnull
{
    void Insert(int index, TKey key, TValue value);
    void RemoveAt(int index);
}
