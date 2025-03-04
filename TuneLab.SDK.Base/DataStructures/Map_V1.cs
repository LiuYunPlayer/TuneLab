using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class Map_V1<TKey, TValue> : IMap_V1<TKey, TValue> where TKey : notnull
{
    public TValue this[TKey key] { get => impl[key]; set => impl[key] = value; }

    TValue IReadOnlyMap_V1<TKey, TValue>.this[TKey key] => ((IReadOnlyMap_V1<TKey, TValue>)impl)[key];

    public IReadOnlyCollection<TKey> Keys => impl.Keys;

    public IReadOnlyCollection<TValue> Values => impl.Values;

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

    public bool Remove(TKey key)
    {
        return impl.Remove(key);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)impl).GetEnumerator();
    }

    public Map_V1() : this(Factory_V1.Create<IMap_V1<TKey, TValue>>()) { }
    Map_V1(IMap_V1<TKey, TValue> impl) { this.impl = impl; }

    readonly IMap_V1<TKey, TValue> impl;
}
