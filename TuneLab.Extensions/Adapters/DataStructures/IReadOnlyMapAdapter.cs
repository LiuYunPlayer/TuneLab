using System.Collections;
using System.Collections.Generic;
using System.Text;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.Extensions.Adapters.DataStructures;

internal static class IReadOnlyMapAdapter
{
    public static IReadOnlyMap_V1<TKey, TValue> ToV1<TKey, TValue>(this IReadOnlyMap<TKey, TValue> domain) where TKey : notnull
    {
        return new IReadOnlyMap_V1Adapter<TKey, TValue>(domain);
    }

    class IReadOnlyMap_V1Adapter<TKey, TValue>(IReadOnlyMap<TKey, TValue> domain) : IReadOnlyMap_V1<TKey, TValue> where TKey : notnull
    {
        public TValue this[TKey key] => domain[key];

        public IReadOnlyCollection<TKey> Keys => domain.Keys;

        public IReadOnlyCollection<TValue> Values => domain.Values;

        public int Count => domain.Count;

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

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
