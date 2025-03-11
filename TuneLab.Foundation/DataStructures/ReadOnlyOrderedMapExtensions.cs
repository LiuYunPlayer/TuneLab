using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public static class ReadOnlyOrderedMapExtensions
{
    public static IReadOnlyOrderedMap<TKey, T> Convert<TKey, T, U>(this IReadOnlyOrderedMap<TKey, U> map, Func<U, T> convert) where TKey : notnull
    {
        return new ReadOnlyOrderedMapWrapper<TKey, T, U>(map, convert);
    }

    class ReadOnlyOrderedMapWrapper<TKey, T, U>(IReadOnlyOrderedMap<TKey, U> map, Func<U, T> convert) : IReadOnlyOrderedMap<TKey, T> where TKey : notnull
    {
        public T this[TKey key] => convert(map[key]);
        IReadOnlyKeyValuePair<TKey, T> IReadOnlyList<IReadOnlyKeyValuePair<TKey, T>>.this[int index] { get { var kvp = ((IReadOnlyList<IReadOnlyKeyValuePair<TKey, U>>)map)[index]; return new ReadOnlyKeyValuePair<TKey, T>(kvp.Key, convert(kvp.Value)); } }
        public IReadOnlyList<TKey> Keys => map.Keys;
        public IReadOnlyList<T> Values => map.Values.Convert(convert);
        public int Count => map.Count;
        public bool ContainsKey(TKey key) => map.ContainsKey(key);
        public IEnumerator<IReadOnlyKeyValuePair<TKey, T>> GetEnumerator() => map.GetEnumerator().Convert(kvp => new ReadOnlyKeyValuePair<TKey, T>(kvp.Key, convert(kvp.Value)));

        public T? GetValue(TKey key, out bool success)
        {
            var value = map.GetValue(key, out success);
            return value == null ? default : convert(value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static IReadOnlyOrderedMap<TKey, T> Convert<TKey, T, U>(this IReadOnlyOrderedMap<TKey, U> map, Func<TKey, U, T> convert) where TKey : notnull
    {
        return new ReadOnlyOrderedMapWithKeyWrapper<TKey, T, U>(map, convert);
    }

    class ReadOnlyOrderedMapWithKeyWrapper<TKey, T, U>(IReadOnlyOrderedMap<TKey, U> map, Func<TKey, U, T> convert) : IReadOnlyOrderedMap<TKey, T> where TKey : notnull
    {
        public T this[TKey key] => convert(key, map[key]);
        IReadOnlyKeyValuePair<TKey, T> IReadOnlyList<IReadOnlyKeyValuePair<TKey, T>>.this[int index] { get { var kvp = ((IReadOnlyList<IReadOnlyKeyValuePair<TKey, U>>)map)[index]; return new ReadOnlyKeyValuePair<TKey, T>(kvp.Key, convert(kvp.Key, kvp.Value)); } }
        public IReadOnlyList<TKey> Keys => map.Keys;
        public IReadOnlyList<T> Values => map.Convert(kvp => convert(kvp.Key, kvp.Value));
        public int Count => map.Count;
        public bool ContainsKey(TKey key) => map.ContainsKey(key);
        public IEnumerator<IReadOnlyKeyValuePair<TKey, T>> GetEnumerator() => map.GetEnumerator().Convert(kvp => new ReadOnlyKeyValuePair<TKey, T>(kvp.Key, convert(kvp.Key, kvp.Value)));

        public T? GetValue(TKey key, out bool success)
        {
            var value = map.GetValue(key, out success);
            return value == null ? default : convert(key, value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
