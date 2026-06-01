using System.Collections.Generic;

namespace TuneLab.Primitives.DataStructures;

public class Map<TKey, TValue> : Dictionary<TKey, TValue>, IMap<TKey, TValue> where TKey : notnull
{
    public static IReadOnlyMap<TKey, TValue> Empty = new Map<TKey, TValue>();

    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;

    public TValue? GetValue(TKey key, out bool success)
    {
        success = TryGetValue(key, out var value);
        return value;
    }

    IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> IEnumerable<IReadOnlyKeyValuePair<TKey, TValue>>.GetEnumerator()
    {
        foreach (var kvp in (Dictionary<TKey, TValue>)this)
            yield return new ReadOnlyKeyValuePair<TKey, TValue>(kvp.Key, kvp.Value);
    }
}
