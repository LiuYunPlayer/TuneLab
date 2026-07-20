using System;
using System.Collections;
using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

internal class SortedDataLinkedList<T> : DataObject, ISortedDataLinkedList<T> where T : class, ILinkedNode<T>
{
    public IActionEvent<T> ItemAdded => mItemAdded;
    public IActionEvent<T> ItemRemoved => mItemRemoved;
    // 结构变更聚合信号 = 自身内容变更事件（链表只在增删时 Notify）。
    public IActionEvent MembershipModified => Modified;
    public IEnumerable<T> Items => this;

    public SortedDataLinkedList(Func<T, T, bool> isInOrder)
    {
        mList = new(isInOrder);
    }

    public T? First => mList.First;

    public T? Last => mList.Last;

    public int Count => mList.Count;


    public void Insert(T item)
    {
        mList.Insert(item);
        mItemAdded.Invoke(item);
        Notify();
        Push(new InsertCommand(this, item, item.Previous));
    }

    public bool Remove(T item)
    {
        if (!Contains(item))
            return false;

        PushAndDo(new RemoveCommand(this, item, item.Previous));
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
        return [..mList];
    }

    // 有序表只按键插入：逐个 Insert 即可定位（输入已有序时游标令其接近 O(n)）。
    public void SetInfo(IEnumerable<T> info)
    {
        using var _ = MergeNotify();
        Clear();
        foreach (var item in info)
        {
            Insert(item);
        }
    }

    // undo/redo 按记录的前驱锚点精确复位（走 Reinsert），而非有序重插：复合撤销时元素排序键可能尚未回滚
    // （如"改键+重排"逆序回放，重排先于改键被撤），此刻只能按结构位置还原，否则会落到错误的有序槽位。
    class InsertCommand(SortedDataLinkedList<T> dataLinkedList, T item, T? previous) : ICommand
    {
        public void Redo()
        {
            dataLinkedList.mList.Reinsert(previous, item);
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

    class RemoveCommand(SortedDataLinkedList<T> dataLinkedList, T item, T? previous) : ICommand
    {
        public void Redo()
        {
            dataLinkedList.mList.Remove(item);
            dataLinkedList.mItemRemoved.Invoke(item);
            dataLinkedList.Notify();
        }

        public void Undo()
        {
            dataLinkedList.mList.Reinsert(previous, item);
            dataLinkedList.mItemAdded.Invoke(item);
            dataLinkedList.Notify();
        }
    }

    readonly ActionEvent<T> mItemAdded = new();
    readonly ActionEvent<T> mItemRemoved = new();

    readonly SortedLinkedList<T> mList;
}
