using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public static class ReadOnlyListExtensions
{
    public static IReadOnlyList<T> Convert<T, U>(this IReadOnlyList<U> list, Func<U, T> convert)
    {
        return new ReadOnlyListWrapper<T, U>(list, convert);
    }

    class ReadOnlyListWrapper<T, U>(IReadOnlyList<U> list, Func<U, T> convert) : IReadOnlyList<T>
    {
        public T this[int index] => convert(list[index]);
        public int Count => list.Count;
        public IEnumerator<T> GetEnumerator() => list.GetEnumerator().Convert(convert);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}