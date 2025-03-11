namespace TuneLab.SDK.Base.DataStructures;

public class ReadOnlyKeyValuePair_V1<TKey, TValue>(TKey key, TValue value) : IReadOnlyKeyValuePair_V1<TKey, TValue>
{
    public TKey Key { get; } = key;
    public TValue Value { get; } = value;

    public ReadOnlyKeyValuePair_V1(KeyValuePair<TKey, TValue> pair) : this(pair.Key, pair.Value) { }

    public static implicit operator ReadOnlyKeyValuePair_V1<TKey, TValue>(KeyValuePair<TKey, TValue> pair)
    {
        return new ReadOnlyKeyValuePair_V1<TKey, TValue>(pair);
    }

    public static ReadOnlyKeyValuePair_V1<TKey, TValue> FromSystem(KeyValuePair<TKey, TValue> pair)
    {
        return new ReadOnlyKeyValuePair_V1<TKey, TValue>(pair);
    }
}
