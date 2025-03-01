namespace TuneLab.Foundation.DataStructures;

public static class KeyValuePairExtensions
{
    public static KeyWithValue<TKey, TValue> ToKeyWithValue<TKey, TValue>(this KeyValuePair<TKey, TValue> pair)
    {
        return new KeyWithValue<TKey, TValue>(pair);
    }
}
