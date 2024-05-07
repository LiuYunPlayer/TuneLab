using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Structures;

internal class ReadOnlyListWrapper<T, U>(IReadOnlyList<U> list, Func<U, T> convert) : IReadOnlyList<T>
{
    public T this[int index] => convert(list[index]);
    public int Count => list.Count;
    public IEnumerator<T> GetEnumerator() => new EnumeratorWrapper<T, U>(list.GetEnumerator(), convert);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal class ReadOnlyListWrapper<T>(IReadOnlyList<T> list, Func<T, T> convert) : ReadOnlyListWrapper<T, T>(list, convert) { }

public static class ReadOnlyListWrapperExtension
{
    public static IReadOnlyList<T> Convert<T, U>(this IReadOnlyList<U> list, Func<U, T> convert)
    {
        return new ReadOnlyListWrapper<T, U>(list, convert);
    }
}