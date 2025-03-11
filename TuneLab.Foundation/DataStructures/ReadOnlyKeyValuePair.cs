namespace TuneLab.Foundation.DataStructures;

public class ReadOnlyKeyValuePair<TKey, TValue>(TKey key, TValue value) : IReadOnlyKeyValuePair<TKey, TValue>
{
    public TKey Key { get; } = key;
    public TValue Value { get; } = value;

    public ReadOnlyKeyValuePair(KeyValuePair<TKey, TValue> pair) : this(pair.Key, pair.Value) { }

    public static implicit operator ReadOnlyKeyValuePair<TKey, TValue>(KeyValuePair<TKey, TValue> pair)
    {
        return new ReadOnlyKeyValuePair<TKey, TValue>(pair);
    }

    public static ReadOnlyKeyValuePair<TKey, TValue> FromSystem(KeyValuePair<TKey, TValue> pair)
    {
        return new ReadOnlyKeyValuePair<TKey, TValue>(pair);
    }
}
