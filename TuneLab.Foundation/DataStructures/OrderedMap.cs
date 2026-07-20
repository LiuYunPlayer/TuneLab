using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TuneLab.Foundation;

[CollectionBuilder(typeof(OrderedMapBuilder), nameof(OrderedMapBuilder.Create))]
public class OrderedMap<TKey, TValue> : IOrderedMap<TKey, TValue> where TKey : notnull
{
    public int Count => mKeys.Count;

    public TValue this[TKey key]
    {
        get => mMap[key];
        set
        {
            if (mMap.ContainsKey(key))
                mMap[key] = value;
            else
                Add(key, value);
        }
    }

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

    public bool Remove(TKey key)
    {
        mKeys.Remove(key);
        return mMap.Remove(key);
    }

    public void RemoveAt(int index)
    {
        TKey key = mKeys[index];
        mKeys.RemoveAt(index);
        mMap.Remove(key);
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

    public ReadOnlyKeyValuePair<TKey, TValue> At(int index)
    {
        TKey key = mKeys[index];
        TValue value = mMap[key];
        return new ReadOnlyKeyValuePair<TKey, TValue>(key, value);
    }

    public Enumerator GetEnumerator() => new(this);

    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => mKeys;
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;

    IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> IEnumerable<IReadOnlyKeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public TValue? GetValue(TKey key, out bool success)
    {
        return mMap.GetValue(key, out success);
    }

    IReadOnlyKeyValuePair<TKey, TValue> IReadOnlyList<IReadOnlyKeyValuePair<TKey, TValue>>.this[int index] => At(index);

    readonly Map<TKey, TValue> mMap = new();
    readonly List<TKey> mKeys = new();

    public struct Enumerator : IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>>, IEnumerator<TValue>
    {
        public IReadOnlyKeyValuePair<TKey, TValue> Current => mMap.At(mCurrentIndex);

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

public static class OrderedMapBuilder
{
    public static OrderedMap<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<IReadOnlyKeyValuePair<TKey, TValue>> values) where TKey : notnull
    {
        var map = new OrderedMap<TKey, TValue>();
        foreach (var kvp in values)
            map.Add(kvp.Key, kvp.Value);
        return map;
    }
}

// 真不可变空有序 map：IReadOnlyOrderedMapBuilder 空集合的进程级单例。独立类型（非 OrderedMap 子类）——
// 故 (OrderedMap<K,V>)Empty 下转型抛 InvalidCastException 而非静默拿到可变实例。无任何写入面。
sealed class EmptyOrderedMap<TKey, TValue> : IReadOnlyOrderedMap<TKey, TValue> where TKey : notnull
{
    public static readonly EmptyOrderedMap<TKey, TValue> Instance = new();
    EmptyOrderedMap() { }

    public int Count => 0;
    public TValue this[TKey key] => throw new KeyNotFoundException();
    public IReadOnlyList<TKey> Keys => Array.Empty<TKey>();
    public IReadOnlyList<TValue> Values => Array.Empty<TValue>();
    public bool ContainsKey(TKey key) => false;
    public TValue? GetValue(TKey key, out bool success) { success = false; return default; }

    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => Array.Empty<TKey>();
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => Array.Empty<TValue>();
    IReadOnlyKeyValuePair<TKey, TValue> IReadOnlyList<IReadOnlyKeyValuePair<TKey, TValue>>.this[int index]
        => throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> GetEnumerator() { yield break; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
