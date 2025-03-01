using System.Collections;

namespace TuneLab.Base.Structures;

public class LinkedList<T> : ILinkedList<T> where T : class, ILinkedNode<T>
{
    public T? Begin => mBegin;
    public T? End => mEnd;
    public int Count => mCount;

    public void Insert(T item)
    {
        if (Count == 0)
        {
            mBegin = item;
            mEnd = item;

            item.LinkedList = this;
            mCount++;
            mLastInsertedItem = item;
        }
        else
        {
            bool direction = IsInOrder(mLastInsertedItem!, item);
            if (direction)
            {
                T last = mLastInsertedItem!;
                while (last.Next != null && IsInOrder(last.Next, item))
                {
                    last = last.Next;
                }

                InsertAfter(last, item);
            }
            else
            {
                T next = mLastInsertedItem!;
                while (next.Last != null && !IsInOrder(next.Last, item))
                {
                    next = next.Last;
                }

                InsertBefore(next, item);
            }
        }
    }

    public bool Remove(T item)
    {
        if (!Contains(item))
            return false;

        var last = item.Last;
        var next = item.Next;
        item.Last = null;
        item.Next = null;
        item.LinkedList = null;
        if (last == null)
        {
            mBegin = next;
        }
        else
        {
            last.Next = next;
        }
        if (next == null)
        {
            mEnd = last;
        }
        else
        {
            next.Last = last;
        }
        mCount--;
        if (mLastInsertedItem == item)
        {
            mLastInsertedItem = mEnd;
        }

        return true;
    }

    public void Clear()
    {
        foreach (var item in this)
        {
            item.Last = null;
            item.Next = null;
            item.LinkedList = null;
        }
        mCount = 0;
        mBegin = null;
        mEnd = null;
        mLastInsertedItem = null;
    }

    public bool Contains(T item)
    {
        return item.LinkedList == this;
    }

    public void InsertAfter(T last, T item)
    {
        if (last == mEnd)
            mEnd = item;

        item.Last = last;
        item.Next = last.Next;
        last.Next = item;
        if (item.Next != null)
        {
            item.Next.Last = item;
        }

        item.LinkedList = this;
        mCount++;
        mLastInsertedItem = item;
    }

    public void InsertBefore(T next, T item)
    {
        if (next == mBegin)
            mBegin = item;

        item.Last = next.Last;
        item.Next = next;
        next.Last = item;
        if (item.Last != null)
        {
            item.Last.Next = item;
        }

        item.LinkedList = this;
        mCount++;
        mLastInsertedItem = item;
    }

    protected virtual bool IsInOrder(T prev, T next)
    {
        return true;
    }

    public IEnumerator<T> GetEnumerator()
    {
        var current = mBegin;
        while (current != null)
        {
            var next = current.Next;
            yield return current;
            current = next;
        }
    }

    public IEnumerator<T> Inverse()
    {
        var current = mEnd;
        while (current != null)
        {
            var last = current.Last;
            yield return current;
            current = last;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    T? mBegin = null;
    T? mEnd = null;
    int mCount = 0;

    T? mLastInsertedItem = null;
}
