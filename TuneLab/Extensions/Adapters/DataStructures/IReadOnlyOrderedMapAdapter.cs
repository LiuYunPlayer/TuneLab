using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.Extensions.Adapters.DataStructures;

internal static class IReadOnlyOrderedMapAdapter
{
    public static IReadOnlyOrderedMap<TKey, TValue> ToDomain<TKey, TValue>(this IReadOnlyOrderedMap_V1<TKey, TValue> v1) where TKey : notnull
    {
        return new IReadOnlyOrderedMapAdapter_V1<TKey, TValue>(v1);
    }

    class IReadOnlyOrderedMapAdapter_V1<TKey, TValue>(IReadOnlyOrderedMap_V1<TKey, TValue> v1) : IReadOnlyOrderedMap<TKey, TValue> where TKey : notnull
    {
        public TValue this[TKey key] => v1[key];

        public IReadOnlyKeyValuePair<TKey, TValue> this[int index] => ((IReadOnlyList<IReadOnlyKeyValuePair_V1<TKey, TValue>>)v1)[index].ToDomain();

        public IReadOnlyList<TKey> Keys => v1.Keys;

        public IReadOnlyList<TValue> Values => v1.Values;

        public int Count => v1.Count;

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

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
