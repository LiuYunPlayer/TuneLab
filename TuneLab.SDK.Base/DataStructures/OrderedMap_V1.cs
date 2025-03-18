using System.Collections;

namespace TuneLab.SDK.Base.DataStructures;


public class OrderedMap_V1<TKey, TValue> : IOrderedMap_V1<TKey, TValue>, IReadOnlyOrderedMap_V1<TKey, TValue> where TKey : notnull
{
    public int Count => mKeys.Count;
    public TValue this[TKey key] { get => mMap[key]; set { if (mMap.ContainsKey(key)) mMap[key] = value; else { mMap.Add(key, value); mKeys.Add(key); } } }
    public IReadOnlyList<TKey> Keys => mKeys;
    public IReadOnlyList<TValue> Values => new ValueCollection(this);
    public OrderedMap_V1() { }

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
    ReadOnlyKeyValuePair_V1<TKey, TValue> At(int index) => new(KeyAt(index), ValueAt(index));

    IEnumerator<IReadOnlyKeyValuePair_V1<TKey, TValue>> IEnumerable<IReadOnlyKeyValuePair_V1<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public TValue? GetValue(TKey key, out bool success)
    {
        return mMap.GetValue(key, out success);
    }

    IReadOnlyKeyValuePair_V1<TKey, TValue> IReadOnlyList<IReadOnlyKeyValuePair_V1<TKey, TValue>>.this[int index] => At(index);

    readonly Map_V1<TKey, TValue> mMap = [];
    readonly List<TKey> mKeys = [];

    public struct Enumerator(OrderedMap_V1<TKey, TValue> map) : IEnumerator<ReadOnlyKeyValuePair_V1<TKey, TValue>>, IEnumerator<TValue>
    {
        public readonly ReadOnlyKeyValuePair_V1<TKey, TValue> Current => map.At(mCurrentIndex);

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

    class ValueCollection(OrderedMap_V1<TKey, TValue> map) : IReadOnlyList<TValue>
    {
        public int Count => map.Count;
        TValue IReadOnlyList<TValue>.this[int index] => map.ValueAt(index);
        public IEnumerator<TValue> GetEnumerator() => map.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public static class OrderedMap_V1Builder
{
    public static OrderedMap_V1<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair_V1<TKey, TValue>> values) where TKey : notnull
    {
        var map = new OrderedMap_V1<TKey, TValue>();
        foreach (var kvp in values)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }
}
