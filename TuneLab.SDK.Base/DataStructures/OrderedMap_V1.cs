using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class OrderedMap_V1<TKey, TValue> : IOrderedMap_V1<TKey, TValue> where TKey : notnull
{
    public TValue this[TKey key] { get => ((IMap_V1<TKey, TValue>)impl)[key]; set => ((IMap_V1<TKey, TValue>)impl)[key] = value; }

    public IReadOnlyKeyValuePair_V1<TKey, TValue> this[int index] => ((IReadOnlyList<IReadOnlyKeyValuePair_V1<TKey, TValue>>)impl)[index];

    TValue IReadOnlyMap_V1<TKey, TValue>.this[TKey key] => ((IReadOnlyMap_V1<TKey, TValue>)impl)[key];

    public IReadOnlyList<TKey> Keys => impl.Keys;

    public IReadOnlyList<TValue> Values => impl.Values;

    public int Count => impl.Count;

    public void Add(TKey key, TValue value)
    {
        impl.Add(key, value);
    }

    public void Clear()
    {
        impl.Clear();
    }

    public bool ContainsKey(TKey key)
    {
        return impl.ContainsKey(key);
    }

    public IEnumerator<IReadOnlyKeyValuePair_V1<TKey, TValue>> GetEnumerator()
    {
        return impl.GetEnumerator();
    }

    public TValue? GetValue(TKey key, out bool success)
    {
        return impl.GetValue(key, out success);
    }

    public void Insert(int index, TKey key, TValue value)
    {
        impl.Insert(index, key, value);
    }

    public bool Remove(TKey key)
    {
        return impl.Remove(key);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)impl).GetEnumerator();
    }

    readonly IOrderedMap_V1<TKey, TValue> impl = Factory_V1.Create<IOrderedMap_V1<TKey, TValue>>();
}
