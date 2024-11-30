using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public class KeyWithValue<TKey, TValue>(TKey key, TValue value) : IReadOnlyKeyWithValue<TKey, TValue>
{
    public TKey Key { get; set; } = key;
    public TValue Value { get; set; } = value;

    public KeyWithValue(KeyValuePair<TKey, TValue> pair) : this(pair.Key, pair.Value) { }

    public static implicit operator KeyWithValue<TKey, TValue>(KeyValuePair<TKey, TValue> pair)
    {
        return new KeyWithValue<TKey, TValue>(pair);
    }
}
