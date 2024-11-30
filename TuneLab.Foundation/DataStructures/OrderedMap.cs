using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

    public TKey KeyAt(int index)
    {
        return mKeys[index];
    }

    public TValue ValueAt(int index)
    {
        return mMap[mKeys[index]];
    }

    public KeyWithValue<TKey, TValue> At(int index)
    {
        TKey key = mKeys[index];
        TValue value = mMap[key];
        return new KeyWithValue<TKey, TValue>(key, value);
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerable<TKey> IReadOnlyMap<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;

    IEnumerator<IReadOnlyKeyWithValue<TKey, TValue>> IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public TValue? GetValue(TKey key, out bool success)
    {
        return mMap.GetValue(key, out success);
    }

    IReadOnlyKeyWithValue<TKey, TValue> IReadOnlyList<IReadOnlyKeyWithValue<TKey, TValue>>.this[int index] => At(index);

    readonly Map<TKey, TValue> mMap = new();
    readonly List<TKey> mKeys = new();

    public struct Enumerator : IEnumerator<KeyWithValue<TKey, TValue>>, IEnumerator<TValue>
    {
        public KeyWithValue<TKey, TValue> Current => mMap.At(mCurrentIndex);

        public Enumerator(OrderedMap<TKey, TValue> map)
        {
            mMap = map;
        }

        public bool MoveNext()
        {
            mCurrentIndex++;
            if (mCurrentIndex >= mMap.Count)
                return false;

            return true;
        }

        public void Reset()
        {
            mCurrentIndex = -1;
        }

        public void Dispose() { }

        TValue IEnumerator<TValue>.Current => mMap.ValueAt(mCurrentIndex);
        object IEnumerator.Current => Current;

        readonly OrderedMap<TKey, TValue> mMap;
        int mCurrentIndex = -1;
    }

    class ValueCollection : IReadOnlyList<TValue>
    {
        public TValue this[int index] => mMap.ValueAt(index);
        public int Count => mMap.Count;
        public IEnumerator<TValue> GetEnumerator() => mMap.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public ValueCollection(OrderedMap<TKey, TValue> map)
        {
            mMap = map;
        }

        readonly OrderedMap<TKey, TValue> mMap;
    }
}
