using System.Collections;

namespace TuneLab.Base.Structures;

internal class SafeReadOnlyList<T>(IReadOnlyList<T> list, T defaultValue = default) : IReadOnlyList<T>
{
    public T this[int index] => index >= 0 && index < list.Count ? list[index] : defaultValue;

    public int Count => list.Count;

    public IEnumerator<T> GetEnumerator()
    {
        return list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
