using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.DataStructures;

internal class EnumerableWrapper<T, U>(IEnumerable<U> enumerable, Func<U, T> convert) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => new EnumeratorWrapper<T, U>(enumerable.GetEnumerator(), convert);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal class EnumerableWrapper<T>(IEnumerable<T> enumerable, Func<T, T> convert) : EnumerableWrapper<T, T>(enumerable, convert) { }

public static class EnumerableWrapperExtension
{
    public static IEnumerable<T> Convert<T, U>(this IEnumerable<U> enumerable, Func<U, T> convert)
    {
        return new EnumerableWrapper<T, U>(enumerable, convert);
    }
}
