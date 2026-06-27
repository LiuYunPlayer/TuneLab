using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public class DataList<T>(IDataObject? parent = null) : DataObject(parent), IDataList<T>
{
    public IActionEvent<T> ItemAdded => mItemAdded;
    public IActionEvent<T> ItemRemoved => mItemRemoved;
    public IActionEvent<T, T> ItemReplaced => mItemReplaced;
    // 结构变更聚合信号 = 自身内容变更事件（列表只在增删/替换时 Notify）。
    public IActionEvent StructureModified => Modified;
    public IEnumerable<T> Items => this;
    public int Count => mList.Count;

    public T this[int index]
    {
        get => mList[index];
        set => PushAndDo(new ReplaceCommand(this, index, mList[index], value));
    }

    public int IndexOf(T item)
    {
        return mList.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        PushAndDo(new InsertCommand(this, index, item));
    }

    public void RemoveAt(int index)
    {
        var item = mList[index];
        PushAndDo(new RemoveCommand(this, index, item));
    }

    public void Add(T item)
    {
        Insert(Count, item);
    }

    public void Clear()
    {
        if (this.IsEmpty())
            return;

        BeginMergeNotify();
        for (int i = Count - 1; i >= 0; i--)
        {
            RemoveAt(i);
        }
        EndMergeNotify();
    }

    public bool Contains(T item)
    {
        return mList.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        mList.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if (index == -1)
            return false;

        PushAndDo(new RemoveCommand(this, index, item));
        return true;
    }

    public void AddRange(IEnumerable<T> items)
    {
        BeginMergeNotify();
        foreach (var item in items)
        {
            Add(item);
        }
        EndMergeNotify();
    }

    public IEnumerator<T> GetEnumerator() => mList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => mList.GetEnumerator();
    bool ICollection<T>.IsReadOnly => ((ICollection<T>)mList).IsReadOnly;

    public List<T> GetInfo()
    {
        return [.. mList];
    }

    public void SetInfo(IEnumerable<T> info)
    {
        var items = info as IReadOnlyCollection<T> ?? info.ToArray();
        using var _ = MergeNotify();
        Clear();
        AddRange(items);
    }

    class InsertCommand(DataList<T> dataList, int index, T item) : ICommand
    {
        public void Redo()
        {
            dataList.mList.Insert(index, item);
            dataList.mItemAdded.Invoke(item);
            dataList.Notify();
        }

        public void Undo()
        {
            dataList.mList.RemoveAt(index);
            dataList.mItemRemoved.Invoke(item);
            dataList.Notify();
        }
    }

    class RemoveCommand(DataList<T> dataList, int index, T item) : ICommand
    {
        public void Redo()
        {
            dataList.mList.RemoveAt(index);
            dataList.mItemRemoved.Invoke(item);
            dataList.Notify();
        }

        public void Undo()
        {
            dataList.mList.Insert(index, item);
            dataList.mItemAdded.Invoke(item);
            dataList.Notify();
        }
    }

    class ReplaceCommand(DataList<T> dataList, int index, T before, T after) : ICommand
    {
        public void Redo()
        {
            dataList.mList[index] = after;
            dataList.mItemReplaced.Invoke(before, after);
            dataList.Notify();
        }

        public void Undo()
        {
            dataList.mList[index] = before;
            dataList.mItemReplaced.Invoke(after, before);
            dataList.Notify();
        }
    }

    ActionEvent<T> mItemAdded = new();
    ActionEvent<T> mItemRemoved = new();
    ActionEvent<T, T> mItemReplaced = new();

    readonly List<T> mList = new();
}
