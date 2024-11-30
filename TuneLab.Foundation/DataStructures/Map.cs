using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public class Map<TKey, TValue> : Dictionary<TKey, TValue>, IReadOnlyMap<TKey, TValue> where TKey : notnull
{
    public readonly static IReadOnlyMap<TKey, TValue> Empty = new Map<TKey, TValue>();
    IEnumerable<TKey> IReadOnlyMap<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;

    public TValue? GetValue(TKey key, out bool success)
    {
        success = TryGetValue(key, out var value);
        return value;
    }

    IEnumerator<IReadOnlyKeyWithValue<TKey, TValue>> IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>.GetEnumerator() =>
        new EnumeratorWrapper<IReadOnlyKeyWithValue<TKey, TValue>, KeyValuePair<TKey, TValue>>(GetEnumerator(), t => new KeyWithValue<TKey, TValue>(t));
}
