using System.Collections;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public class DataObjectLinkedList<T> : DataObject, IDataObjectLinkedList<T> where T : class, IDataObject, ILinkedNode<T>
{
    public IMergableEvent ListModified => mDataLinkedList.Modified;
    public IActionEvent<T> ItemAdded => mDataLinkedList.ItemAdded;
    public IActionEvent<T> ItemRemoved => mDataLinkedList.ItemRemoved;
    public IEnumerable<T> Items => mDataLinkedList.Items;
    public T? Begin => mDataLinkedList.Begin;
    public T? End => mDataLinkedList.End;
    public int Count => mDataLinkedList.Count;

    public DataObjectLinkedList()
    {
        mDataLinkedList = new(this);
        mDataLinkedList.Attach(this);
        mDataLinkedList.ItemAdded.Subscribe(OnAdd);
        mDataLinkedList.ItemRemoved.Subscribe(OnRemove);
    }

    public List<T> GetInfo()
    {
        return mDataLinkedList.GetInfo();
    }

    public void Insert(T item)
    {
        mDataLinkedList.Insert(item);
    }

    public bool Remove(T item)
    {
        return mDataLinkedList.Remove(item);
    }

    public void InsertAfter(T last, T item)
    {
        mDataLinkedList.InsertAfter(last, item);
    }

    public void InsertBefore(T next, T item)
    {
        mDataLinkedList.InsertBefore(next, item);
    }

    public void Clear()
    {
        mDataLinkedList.Clear();
    }

    public bool Contains(T item)
    {
        return mDataLinkedList.Contains(item);
    }

    public IEnumerator<T> Inverse()
    {
        return mDataLinkedList.Inverse();
    }

    public IEnumerator<T> GetEnumerator()
    {
        return mDataLinkedList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    void IDataObject<IEnumerable<T>>.SetInfo(IEnumerable<T> info)
    {
        IDataObject<IEnumerable<T>>.SetInfo(mDataLinkedList, info);
    }

    protected virtual bool IsInOrder(T prev, T next)
    {
        return true;
    }

    void OnAdd(T item)
    {
        item.Attach(this);
    }

    void OnRemove(T item)
    {
        item.Detach();
    }

    class DataLinkedList(DataObjectLinkedList<T> dataObjectLinkedList) : DataLinkedList<T>
    {
        protected override bool IsInOrder(T prev, T next)
        {
            return dataObjectLinkedList.IsInOrder(prev, next);
        }
    }

    readonly DataLinkedList mDataLinkedList;
}
