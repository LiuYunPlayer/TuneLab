using System;
using System.Collections;
using System.Collections.Generic;

namespace TuneLab.Foundation.DataStructures;

// 与 ReadOnlyListWrapper 对称：在保持 Count 的前提下惰性投影 IReadOnlyCollection。
internal class ReadOnlyCollectionWrapper<T, U>(IReadOnlyCollection<U> collection, Func<U, T> convert) : IReadOnlyCollection<T>
{
    public int Count => collection.Count;
    public IEnumerator<T> GetEnumerator() => new EnumeratorWrapper<T, U>(collection.GetEnumerator(), convert);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal class ReadOnlyCollectionWrapper<T>(IReadOnlyCollection<T> collection, Func<T, T> convert) : ReadOnlyCollectionWrapper<T, T>(collection, convert) { }

public static class ReadOnlyCollectionWrapperExtension
{
    public static IReadOnlyCollection<T> Convert<T, U>(this IReadOnlyCollection<U> collection, Func<U, T> convert)
    {
        return new ReadOnlyCollectionWrapper<T, U>(collection, convert);
    }
}
