using System.Collections;

namespace TuneLab.Foundation.DataStructures;

public static class ReadOnlyMapExtensions
{
    public static IReadOnlyMap<TKey, T> Convert<TKey, T, U>(this IReadOnlyMap<TKey, U> map, Func<U, T> convert) where TKey : notnull
    {
        return new ReadOnlyMapWrapper<TKey, T, U>(map, convert);
    }

    class ReadOnlyMapWrapper<TKey, T, U>(IReadOnlyMap<TKey, U> map, Func<U, T> convert) : IReadOnlyMap<TKey, T> where TKey : notnull
    {
        public T this[TKey key] => convert(map[key]);
        public IReadOnlyCollection<TKey> Keys => map.Keys;
        public IReadOnlyCollection<T> Values => map.Values.Convert(convert);
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

    public static IReadOnlyMap<TKey, T> Convert<TKey, T, U>(this IReadOnlyMap<TKey, U> map, Func<TKey, U, T> convert) where TKey : notnull
    {
        return new ReadOnlyMapWithKeyWrapper<TKey, T, U>(map, convert);
    }

    class ReadOnlyMapWithKeyWrapper<TKey, T, U>(IReadOnlyMap<TKey, U> map, Func<TKey, U, T> convert) : IReadOnlyMap<TKey, T> where TKey : notnull
    {
        public T this[TKey key] => convert(key, map[key]);
        public IReadOnlyCollection<TKey> Keys => map.Keys;
        public IReadOnlyCollection<T> Values => map.Convert(kvp => convert(kvp.Key, kvp.Value));
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