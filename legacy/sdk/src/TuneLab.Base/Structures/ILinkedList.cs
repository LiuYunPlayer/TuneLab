using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Structures;

public interface ILinkedList<T> : IReadOnlyLinkedList<T> where T : class, ILinkedNode<T>
{
    void Insert(T item);
    bool Remove(T item);
    void InsertAfter(T last, T item);
    void InsertBefore(T next, T item);
    void Clear();
    bool Contains(T item);
}

public static class ILinkedListExtension
{
    public static void AddBegin<T>(this ILinkedList<T> linkedList, T item) where T : class, ILinkedNode<T>
    {
        if (linkedList.Begin == null)
        {
            linkedList.Insert(item);
        }
        else
        {
            linkedList.InsertBefore(linkedList.Begin, item);
        }
    }

    public static void AddEnd<T>(this ILinkedList<T> linkedList, T item) where T : class, ILinkedNode<T>
    {
        if (linkedList.End == null)
        {
            linkedList.Insert(item);
        }
        else
        {
            linkedList.InsertAfter(linkedList.End, item);
        }
    }
}
