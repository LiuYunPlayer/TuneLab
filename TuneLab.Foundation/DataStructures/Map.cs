using TuneLab.SDK.Base;

namespace TuneLab.Foundation.DataStructures;

public class Map<TKey, TValue> : Dictionary<TKey, TValue>, IMap<TKey, TValue>, IMap_V1<TKey, TValue> where TKey : notnull
{
    public readonly static IReadOnlyMap<TKey, TValue> Empty = new Map<TKey, TValue>();
    public new IReadOnlyCollection<TKey> Keys => Keys;
    public new IReadOnlyCollection<TValue> Values => base.Values;

    public TValue? GetValue(TKey key, out bool success)
    {
        success = TryGetValue(key, out var value);
        return value;
    }

    IEnumerator<IReadOnlyKeyWithValue<TKey, TValue>> IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>.GetEnumerator() => GetEnumerator().Convert(KeyValuePairExtensions.ToKeyWithValue);
    IEnumerator<IReadOnlyKeyValuePair_V1<TKey, TValue>> IEnumerable<IReadOnlyKeyValuePair_V1<TKey, TValue>>.GetEnumerator() => GetEnumerator().Convert(KeyValuePairExtensions.ToKeyWithValue);
}

public static class MapExtensions
{
    public static Map<TKey, TValue> ToMap<TKey, TValue>(this IReadOnlyCollection<IReadOnlyKeyWithValue<TKey, TValue>> kvps) where TKey : notnull
    {
        Map<TKey, TValue> map = new();
        foreach (var kvp in kvps)
        {
            map.Add(kvp.Key, kvp.Value);
        }
        return map;
    }

    public static Map<TKey, TValue> ToMap<TKey, TValue>(this IReadOnlyMap<TKey, TValue> map) where TKey : notnull
    {
        return ((IReadOnlyCollection<IReadOnlyKeyWithValue<TKey, TValue>>)map).ToMap();
    }
}
