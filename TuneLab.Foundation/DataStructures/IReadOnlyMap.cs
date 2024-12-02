using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.Foundation.DataStructures;

public interface IReadOnlyMap<TKey, out TValue> : IReadOnlyCollection<IReadOnlyKeyWithValue<TKey, TValue>> where TKey : notnull
{
    TValue this[TKey key] { get; }
    IReadOnlyCollection<TKey> Keys { get; }
    IReadOnlyCollection<TValue> Values { get; }
    bool ContainsKey(TKey key);
    TValue? GetValue(TKey key, out bool success);
}

public static class IReadOnlyMapExtension
{
    public static bool TryGetValue<TKey, TValue>(this IReadOnlyMap<TKey, TValue> map, TKey key, [MaybeNullWhen(false)] out TValue value) where TKey : notnull
    {
        value = map.GetValue(key, out var success);
        return success;
    }
}
