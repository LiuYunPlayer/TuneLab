using System.Collections;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

internal class DataLinkedList<T> : DataObject, IDataLinkedList<T> where T : class, ILinkedNode<T>
{
    public IActionEvent<T> ItemAdded => mItemAdded;
    public IActionEvent<T> ItemRemoved => mItemRemoved;

    public DataLinkedList()
    {
        mList = new(this);
    }

    public T? Begin => mList.Begin;

    public T? End => mList.End;

    public int Count => mList.Count;


    public void Insert(T item)
    {
        mList.Insert(item);
        mItemAdded.Invoke(item);
        Notify();
        Push(new InsertCommand(this, item, item.Last));
    }

    public bool Remove(T item)
    {
        if (!Contains(item))
            return false;

        PushAndDo(new RemoveCommand(this, item, item.Last));
        return true;
    }

    public void Clear()
    {
        BeginMergeNotify();
        foreach (var item in this)
        {
            Remove(item);
        }
        EndMergeNotify();
    }

    public bool Contains(T item)
    {
        return mList.Contains(item);
    }

    public IEnumerator<T> Inverse()
    {
        return mList.Inverse();
    }

    public IEnumerator<T> GetEnumerator()
    {
        return mList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public List<T> GetInfo()
    {
        return [.. mList];
    }

    void IDataObject<IEnumerable<T>>.SetInfo(IEnumerable<T> info)
    {
        foreach (var item in mList)
        {
            mList.Remove(item);
            mItemRemoved.Invoke(item);
        }

        using var it = info.GetEnumerator();
        if (!it.MoveNext())
            return;

        var current = it.Current;
        mList.Insert(current);
        mItemAdded.Invoke(current);
        var last = current;
        while (it.MoveNext())
        {
            current = it.Current;
            mList.InsertAfter(last, current);
            mItemAdded.Invoke(current);
            last = current;
        }
    }

    public void InsertAfter(T last, T item)
    {
        PushAndDo(new InsertCommand(this, item, last));
    }

    public void InsertBefore(T next, T item)
    {
        PushAndDo(new InsertCommand(this, item, next.Last));
    }

    protected virtual bool IsInOrder(T prev, T next)
    {
        return true;
    }

    class InsertCommand(DataLinkedList<T> dataLinkedList, T item, T? last) : ICommand
    {
        public void Redo()
        {
            if (last == null)
                dataLinkedList.mList.AddBegin(item);
            else
                dataLinkedList.mList.InsertAfter(last, item);
            dataLinkedList.mItemAdded.Invoke(item);
            dataLinkedList.Notify();
        }

        public void Undo()
        {
            dataLinkedList.mList.Remove(item);
            dataLinkedList.mItemRemoved.Invoke(item);
            dataLinkedList.Notify();
        }
    }

    class RemoveCommand(DataLinkedList<T> dataLinkedList, T item, T? last) : ICommand
    {
        public void Redo()
        {
            dataLinkedList.mList.Remove(item);
            dataLinkedList.mItemRemoved.Invoke(item);
            dataLinkedList.Notify();
        }

        public void Undo()
        {
            if (last == null)
                dataLinkedList.mList.AddBegin(item);
            else
                dataLinkedList.mList.InsertAfter(last, item);
            dataLinkedList.mItemAdded.Invoke(item);
            dataLinkedList.Notify();
        }
    }

    class LinkedList(DataLinkedList<T> dataLinkedList) : DataStructures.LinkedList<T>
    {
        protected override bool IsInOrder(T prev, T next)
        {
            return dataLinkedList.IsInOrder(prev, next);
        }
    }

    readonly ActionEvent<T> mItemAdded = new();
    readonly ActionEvent<T> mItemRemoved = new();

    readonly LinkedList mList;
}
