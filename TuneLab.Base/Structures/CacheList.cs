using System.Collections;

namespace TuneLab.Base.Structures;

internal class CacheList<T> : IList<T>, IReadOnlyList<T> where T : class, new()
{
    public T this[int index] { get => ((IList<T>)mList)[index]; set => ((IList<T>)mList)[index] = value; }

    public int Count => mUserfulCount;

    public bool IsReadOnly => ((ICollection<T>)mList).IsReadOnly;

    public T Next()
    {
        T t;
        if (mUserfulCount == mList.Count)
        {
            t = new();
            mList.Add(t);
        }
        else
        {
            t = mList[mUserfulCount];
        }

        mUserfulCount++;
        return t;
    }

    public void ClearCaches()
    {
        mList.Clear();
        mUserfulCount = 0;
    }

    public void Add(T item)
    {
        if (mUserfulCount == mList.Count)
        {
            mList.Add(item);
        }
        else
        {
            mList[mUserfulCount] = item;
        }

        mUserfulCount++;
    }

    public void Clear()
    {
        mUserfulCount = 0;
    }

    public bool Contains(T item)
    {
        return (uint)IndexOf(item) < (uint)mUserfulCount;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ((ICollection<T>)mList).CopyTo(array, arrayIndex);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return mList.Take(mUserfulCount).GetEnumerator();
    }

    public int IndexOf(T item)
    {
        var index = mList.IndexOf(item);
        if (index >= mUserfulCount)
            index = -1;

        return index;
    }

    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)mUserfulCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        mList.Insert(index, item);
        mUserfulCount++;
    }

    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if ((uint)index >= (uint)mUserfulCount)
            return false;

        mList.RemoveAt(index);
        mUserfulCount--;
        return true;
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)mUserfulCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        mList.RemoveAt(index);
        mUserfulCount--;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    readonly List<T> mList = new();
    int mUserfulCount = 0;
}
