using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace TuneLab.Foundation;

// 纯双向链表：只负责节点拼接与遍历，对元素顺序无任何约定。写入口为"两端（AddFirst/AddLast）+ 相对节点
// （InsertAfter/InsertBefore）"，空表由 AddFirst/AddLast 承担首元素 seeding。按键自动定位的有序插入见 SortedLinkedList<T>。
public sealed class LinkedList<T> : ILinkedList<T> where T : class, ILinkedNode<T>
{
    public T? First => mFirst;
    public T? Last => mLast;
    public int Count => mCount;

    public void AddFirst(T item)
    {
        if (mCount == 0)
            Seed(item);
        else
            InsertBefore(mFirst!, item);
    }

    public void AddLast(T item)
    {
        if (mCount == 0)
            Seed(item);
        else
            InsertAfter(mLast!, item);
    }

    public bool Remove(T item)
    {
        if (!Contains(item))
            return false;

        var previous = item.Previous;
        var next = item.Next;
        item.Previous = null;
        item.Next = null;
        item.LinkedList = null;
        if (previous == null)
        {
            mFirst = next;
        }
        else
        {
            previous.Next = next;
        }
        if (next == null)
        {
            mLast = previous;
        }
        else
        {
            next.Previous = previous;
        }
        mCount--;

        return true;
    }

    public void Clear()
    {
        foreach (var item in this)
        {
            item.Previous = null;
            item.Next = null;
            item.LinkedList = null;
        }
        mCount = 0;
        mFirst = null;
        mLast = null;
    }

    public bool Contains(T item)
    {
        return item.LinkedList == this;
    }

    public void InsertAfter(T previous, T item)
    {
        Debug.Assert(Contains(previous), "Anchor item does not belong to this linked list.");
        Debug.Assert(item.LinkedList == null, "Item already belongs to a linked list; re-inserting would corrupt the structure of both lists.");

        if (previous == mLast)
            mLast = item;

        item.Previous = previous;
        item.Next = previous.Next;
        previous.Next = item;
        if (item.Next != null)
        {
            item.Next.Previous = item;
        }

        item.LinkedList = this;
        mCount++;
    }

    public void InsertBefore(T next, T item)
    {
        Debug.Assert(Contains(next), "Anchor item does not belong to this linked list.");
        Debug.Assert(item.LinkedList == null, "Item already belongs to a linked list; re-inserting would corrupt the structure of both lists.");

        if (next == mFirst)
            mFirst = item;

        item.Previous = next.Previous;
        item.Next = next;
        next.Previous = item;
        if (item.Previous != null)
        {
            item.Previous.Next = item;
        }

        item.LinkedList = this;
        mCount++;
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
            var previous = current.Previous;
            yield return current;
            current = previous;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void Seed(T item)
    {
        mFirst = item;
        mLast = item;
        item.LinkedList = this;
        mCount++;
    }

    T? mFirst = null;
    T? mLast = null;
    int mCount = 0;
}
