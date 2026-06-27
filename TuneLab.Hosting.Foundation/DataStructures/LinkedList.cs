using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace TuneLab.Foundation;

public class LinkedList<T> : ILinkedList<T> where T : class, ILinkedNode<T>
{
    public T? First => mFirst;
    public T? Last => mLast;
    public int Count => mCount;

    public void Insert(T item)
    {
        Debug.Assert(item.LinkedList == null, "Item already belongs to a linked list; re-inserting would corrupt the structure of both lists.");

        if (Count == 0)
        {
            mFirst = item;
            mLast = item;

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
            mFirst = next;
        }
        else
        {
            last.Next = next;
        }
        if (next == null)
        {
            mLast = last;
        }
        else
        {
            next.Last = last;
        }
        mCount--;
        if (mLastInsertedItem == item)
        {
            mLastInsertedItem = mLast;
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
        mFirst = null;
        mLast = null;
        mLastInsertedItem = null;
    }

    public bool Contains(T item)
    {
        return item.LinkedList == this;
    }

    public void InsertAfter(T last, T item)
    {
        Debug.Assert(Contains(last), "Anchor item does not belong to this linked list.");
        Debug.Assert(item.LinkedList == null, "Item already belongs to a linked list; re-inserting would corrupt the structure of both lists.");

        if (last == mLast)
            mLast = item;

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
        Debug.Assert(Contains(next), "Anchor item does not belong to this linked list.");
        Debug.Assert(item.LinkedList == null, "Item already belongs to a linked list; re-inserting would corrupt the structure of both lists.");

        if (next == mFirst)
            mFirst = item;

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
        var current = mFirst;
        while (current != null)
        {
            var next = current.Next;
            yield return current;
            current = next;
        }
    }

    public IEnumerator<T> Inverse()
    {
        var current = mLast;
        while (current != null)
        {
            var last = current.Last;
            yield return current;
            current = last;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    T? mFirst = null;
    T? mLast = null;
    int mCount = 0;

    T? mLastInsertedItem = null;
}
