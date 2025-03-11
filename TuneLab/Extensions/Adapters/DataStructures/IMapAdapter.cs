using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.Extensions.Adapters.DataStructures;

internal static class IMapAdapter
{
    public static IMap<TKey, TValue> ToDomain<TKey, TValue>(this IMap_V1<TKey, TValue> v1) where TKey : notnull
    {
        return new IMapAdapter_V1<TKey, TValue>(v1);
    }

    class IMapAdapter_V1<TKey, TValue>(IMap_V1<TKey, TValue> v1) : IMap<TKey, TValue> where TKey : notnull
    {
        public TValue this[TKey key] { get => v1[key]; set => v1[key] = value; }

        public IReadOnlyCollection<TKey> Keys => v1.Keys;

        public IReadOnlyCollection<TValue> Values => v1.Values;

        public int Count => v1.Count;

        public void Add(TKey key, TValue value)
        {
            v1.Add(key, value);
        }

        public void Clear()
        {
            v1.Clear();
        }

        public bool ContainsKey(TKey key)
        {
            return v1.ContainsKey(key);
        }

        public IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return v1.GetEnumerator().Convert(IReadOnlyKeyValuePairAdapter.ToDomain);
        }

        public TValue? GetValue(TKey key, out bool success)
        {
            return v1.GetValue(key, out success);
        }

        public bool Remove(TKey key)
        {
            return v1.Remove(key);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public static IMap_V1<TKey, TValue> ToV1<TKey, TValue>(this IMap<TKey, TValue> domain) where TKey : notnull
    {
        return new IMap_V1Adapter<TKey, TValue>(domain);
    }

    class IMap_V1Adapter<TKey, TValue>(IMap<TKey, TValue> domain) : IMap_V1<TKey, TValue> where TKey : notnull
    {
        public TValue this[TKey key] { get => domain[key]; set => domain[key] = value; }

        public IReadOnlyCollection<TKey> Keys => domain.Keys;

        public IReadOnlyCollection<TValue> Values => domain.Values;

        public int Count => domain.Count;

        public void Add(TKey key, TValue value)
        {
            domain.Add(key, value);
        }

        public void Clear()
        {
            domain.Clear();
        }

        public bool ContainsKey(TKey key)
        {
            return domain.ContainsKey(key);
        }

        public IEnumerator<IReadOnlyKeyValuePair_V1<TKey, TValue>> GetEnumerator()
        {
            return domain.GetEnumerator().Convert(IReadOnlyKeyValuePairAdapter.ToV1);
        }

        public TValue? GetValue(TKey key, out bool success)
        {
            return domain.GetValue(key, out success);
        }

        public bool Remove(TKey key)
        {
            return domain.Remove(key);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
