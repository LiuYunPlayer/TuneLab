using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

internal class ReadOnlyMapWrapper<TKey, T, U>(IReadOnlyMap<TKey, U> map, Func<TKey, U, T> convert) : IReadOnlyMap<TKey, T> where TKey : notnull
{
    public T this[TKey key] => convert(key, map[key]);
    public IEnumerable<TKey> Keys => map.Keys;
    public IEnumerable<T> Values => new EnumerableWrapper<T, IReadOnlyKeyWithValue<TKey, U>>(map, kvp => convert(kvp.Key, kvp.Value));
    public int Count => map.Count;
    public bool ContainsKey(TKey key) => map.ContainsKey(key);
    public IEnumerator<IReadOnlyKeyWithValue<TKey, T>> GetEnumerator() => new EnumeratorWrapper<IReadOnlyKeyWithValue<TKey, T>, IReadOnlyKeyWithValue<TKey, U>>(map.GetEnumerator(), kvp => new KeyWithValue<TKey, T>(kvp.Key, convert(kvp.Key, kvp.Value)));

    public T? GetValue(TKey key, out bool success)
    {
        var value = map.GetValue(key, out success);
        return value == null ? default : convert(key, value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal class ReadOnlyMapWrapper<TKey, T>(IReadOnlyMap<TKey, T> map, Func<TKey, T, T> convert) : ReadOnlyMapWrapper<TKey, T, T>(map, convert) where TKey : notnull { }

public static class ReadOnlyMapWrapperExtension
{
    public static IReadOnlyMap<TKey, T> Convert<TKey, T, U>(this IReadOnlyMap<TKey, U> map, Func<TKey, U, T> convert) where TKey : notnull
    {
        return new ReadOnlyMapWrapper<TKey, T, U>(map, convert);
    }
}