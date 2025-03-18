using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.Extensions.Adapters.DataStructures;

internal static class IReadOnlyKeyValuePairAdapter
{
    public static IReadOnlyKeyValuePair<TKey, TValue> ToDomain<TKey, TValue>(this IReadOnlyKeyValuePair_V1<TKey, TValue> v1)
    {
        return new IReadOnlyKeyValuePairAdapter_V1<TKey, TValue>(v1);
    }

    class IReadOnlyKeyValuePairAdapter_V1<TKey, TValue>(IReadOnlyKeyValuePair_V1<TKey, TValue> v1) : IReadOnlyKeyValuePair<TKey, TValue>
    {
        public TKey Key => v1.Key;

        public TValue Value => v1.Value;
    }

    public static IReadOnlyKeyValuePair_V1<TKey, TValue> ToV1<TKey, TValue>(this IReadOnlyKeyValuePair<TKey, TValue> domain)
    {
        return new IReadOnlyKeyValuePair_V1Adapter<TKey, TValue>(domain);
    }

    class IReadOnlyKeyValuePair_V1Adapter<TKey, TValue>(IReadOnlyKeyValuePair<TKey, TValue> domain) : IReadOnlyKeyValuePair_V1<TKey, TValue>
    {
        public TKey Key => domain.Key;

        public TValue Value => domain.Value;
    }
}
