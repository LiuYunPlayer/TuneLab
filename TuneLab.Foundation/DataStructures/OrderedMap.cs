using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public class OrderedMap<TKey, TValue> : IReadOnlyOrderedMap<TKey, TValue> where TKey : notnull
{
    public int Count => mKeys.Count;
    public TValue this[TKey key] => mMap[key];
    public IReadOnlyList<TKey> Keys => mKeys;
    public IReadOnlyList<TValue> Values => new ValueCollection(this);
    public OrderedMap() { }

    public void Add(TKey key, TValue value)
    {
        if (mMap.ContainsKey(key))
            Remove(key);

        mMap.Add(key, value);
        mKeys.Add(key);
    }

    public void Insert(int index, TKey key, TValue value)
    {
        if (mMap.ContainsKey(key))
            Remove(key);

        mMap.Add(key, value);
        mKeys.Insert(index, key);
    }

    public void Remove(TKey key)
    {
        mMap.Remove(key);
        mKeys.Remove(key);
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
    KeyWithValue<TKey, TValue> At(int index) => new(KeyAt(index), ValueAt(index));

    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;

    IEnumerator<IReadOnlyKeyWithValue<TKey, TValue>> IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public TValue? GetValue(TKey key, out bool success)
    {
        return mMap.GetValue(key, out success);
    }

    IReadOnlyKeyWithValue<TKey, TValue> IReadOnlyList<IReadOnlyKeyWithValue<TKey, TValue>>.this[int index] => At(index);

    readonly Map<TKey, TValue> mMap = new();
    readonly List<TKey> mKeys = new();

    public struct Enumerator(OrderedMap<TKey , TValue> map) : IEnumerator<KeyWithValue<TKey, TValue>>, IEnumerator<TValue>
    {
        public KeyWithValue<TKey, TValue> Current => map.At(mCurrentIndex);

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

        public void Dispose() { }

        TValue IEnumerator<TValue>.Current => map.ValueAt(mCurrentIndex);
        object IEnumerator.Current => Current;

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
