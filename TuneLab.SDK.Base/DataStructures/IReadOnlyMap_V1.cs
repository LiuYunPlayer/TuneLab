using System.Diagnostics.CodeAnalysis;

namespace TuneLab.SDK.Base;

public interface IReadOnlyMap_V1<TKey, out TValue> : IReadOnlyCollection<IReadOnlyKeyValuePair_V1<TKey, TValue>> where TKey : notnull
{
    TValue this[TKey key] { get; }
    IReadOnlyCollection<TKey> Keys { get; }
    IReadOnlyCollection<TValue> Values { get; }
    bool ContainsKey(TKey key);
    TValue? GetValue(TKey key, out bool success);
}

public static class IReadOnlyMap_V1Extension
{
    public static bool TryGetValue<TKey, TValue>(this IReadOnlyMap_V1<TKey, TValue> map, TKey key, [MaybeNullWhen(false)] out TValue value) where TKey : notnull
    {
        value = map.GetValue(key, out var success);
        return success;
    }
}
