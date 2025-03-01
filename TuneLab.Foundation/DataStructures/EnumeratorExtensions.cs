using System.Collections;

namespace TuneLab.Foundation.DataStructures;

public static class EnumeratorExtensions
{
    public static IEnumerator<T> Convert<T, U>(this IEnumerator<U> enumerator, Func<U, T> convert)
    {
        return new EnumeratorWrapper<T, U>(enumerator, convert);
    }

    struct EnumeratorWrapper<T, U>(IEnumerator<U> enumerator, Func<U, T> convert) : IEnumerator<T>
    {
        public T Current => convert(enumerator.Current);
        object? IEnumerator.Current => Current;

        public void Dispose()
        {
            enumerator.Dispose();
        }

        public bool MoveNext()
        {
            return enumerator.MoveNext();
        }

        public void Reset()
        {
            enumerator.Reset();
        }
    }
}
