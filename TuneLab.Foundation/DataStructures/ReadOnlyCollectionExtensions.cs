using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

public static class ReadOnlyCollectionExtensions
{
    public static IReadOnlyCollection<T> Convert<T, U>(this IReadOnlyCollection<U> list, Func<U, T> convert)
    {
        return new ReadOnlyCollectionWrapper<T, U>(list, convert);
    }

    class ReadOnlyCollectionWrapper<T, U>(IReadOnlyCollection<U> list, Func<U, T> convert) : IReadOnlyCollection<T>
    {
        public int Count => list.Count;
        public IEnumerator<T> GetEnumerator() => list.GetEnumerator().Convert(convert);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
