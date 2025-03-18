using System.Collections;

namespace TuneLab.Foundation.DataStructures;

public class OrderedMap<TKey, TValue> : IOrderedMap<TKey, TValue>, IReadOnlyOrderedMap<TKey, TValue> where TKey : notnull
{
    public int Count => mKeys.Count;
    public TValue this[TKey key] { get => mMap[key]; set { if (mMap.ContainsKey(key)) mMap[key] = value; else { mMap.Add(key, value); mKeys.Add(key); } } }
    public IReadOnlyList<TKey> Keys => mKeys;
    public IReadOnlyList<TValue> Values => new ValueCollection(this);
    public OrderedMap() { }

    public void Add(TKey key, TValue value)
    {
        if (mMap.ContainsKey(key))
            throw new ArgumentException("Key already exists in map");

        mMap.Add(key, value);
        mKeys.Add(key);
    }

    public void Insert(int index, TKey key, TValue value)
    {
        if (mMap.ContainsKey(key))
            throw new ArgumentException("Key already exists in map");

        mMap.Add(key, value);
        mKeys.Insert(index, key);
    }

    public bool Remove(TKey key)
    {
        return mMap.Remove(key) && mKeys.Remove(key);
    }

    public void Clear()
    {
        mKeys.Clear();
        mMap.Clear();
    }

    public bool ContainsKey(TKey key)
    {
        return mMap.ContainsKey(key);
    }

    public Enumerator GetEnumerator() => new(this);

    TKey KeyAt(int index) => mKeys[index];
    TValue ValueAt(int index) => mMap[KeyAt(index)];
    ReadOnlyKeyValuePair<TKey, TValue> At(int index) => new(KeyAt(index), ValueAt(index));

    IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> IEnumerable<IReadOnlyKeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public TValue? GetValue(TKey key, out bool success)
    {
        return mMap.GetValue(key, out success);
    }

    IReadOnlyKeyValuePair<TKey, TValue> IReadOnlyList<IReadOnlyKeyValuePair<TKey, TValue>>.this[int index] => At(index);

    readonly Map<TKey, TValue> mMap = [];
    readonly List<TKey> mKeys = [];

    public struct Enumerator(OrderedMap<TKey, TValue> map) : IEnumerator<ReadOnlyKeyValuePair<TKey, TValue>>, IEnumerator<TValue>
    {
        public readonly ReadOnlyKeyValuePair<TKey, TValue> Current => map.At(mCurrentIndex);

        public bool MoveNext()
        {
            mCurrentIndex++;
            if (mCurrentIndex >= map.Count)
                return false;

            return true;
        }

        public void Reset()
        {
            mCurrentIndex = -1;
        }

        public readonly void Dispose() { }

        readonly TValue IEnumerator<TValue>.Current => map.ValueAt(mCurrentIndex);

        readonly object IEnumerator.Current => Current;

        int mCurrentIndex = -1;
    }

    class ValueCollection(OrderedMap<TKey, TValue> map) : IReadOnlyList<TValue>
    {
        public int Count => map.Count;
        TValue IReadOnlyList<TValue>.this[int index] => map.ValueAt(index);
        public IEnumerator<TValue> GetEnumerator() => map.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public static class OrderedMapBuilder
{
    public static OrderedMap<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair<TKey, TValue>> values) where TKey : notnull
    {
        var map = new OrderedMap<TKey, TValue>();
        foreach (var kvp in values)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }
}
