using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

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
    public static void AddFirst<T>(this ILinkedList<T> linkedList, T item) where T : class, ILinkedNode<T>
    {
        if (linkedList.First == null)
        {
            linkedList.Insert(item);
        }
        else
        {
            linkedList.InsertBefore(linkedList.First, item);
        }
    }

    public static void AddLast<T>(this ILinkedList<T> linkedList, T item) where T : class, ILinkedNode<T>
    {
        if (linkedList.Last == null)
        {
            linkedList.Insert(item);
        }
        else
        {
            linkedList.InsertAfter(linkedList.Last, item);
        }
    }
}
