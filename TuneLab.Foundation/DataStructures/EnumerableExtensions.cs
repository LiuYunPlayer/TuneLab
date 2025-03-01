using System.Collections;

namespace TuneLab.Foundation.DataStructures;

public static class EnumerableExtensions
{
    public static IEnumerable<T> Convert<T, U>(this IEnumerable<U> enumerable, Func<U, T> convert)
    {
        return new EnumerableWrapper<T, U>(enumerable, convert);
    }

    class EnumerableWrapper<T, U>(IEnumerable<U> enumerable, Func<U, T> convert) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => enumerable.GetEnumerator().Convert(convert);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    class EnumerableWrapper<T>(IEnumerable<T> enumerable, Func<T, T> convert) : EnumerableWrapper<T, T>(enumerable, convert) { }
}
