using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public class DataObjectList<T> : DataObject, IDataObjectList<T> where T : class, IDataObject
{
    public IModifiedEvent StructureModified => mDataList.Modified;
    public IActionEvent<T> ItemAdded => ((IReadOnlyDataList<T>)mDataList).ItemAdded;
    public IActionEvent<T> ItemRemoved => ((IReadOnlyDataList<T>)mDataList).ItemRemoved;
    public IActionEvent<T, T> ItemReplaced => ((IReadOnlyDataList<T>)mDataList).ItemReplaced;
    public IEnumerable<T> Items => this;

    public int Count => ((ICollection<T>)mDataList).Count;
    public bool IsReadOnly => ((ICollection<T>)mDataList).IsReadOnly;

    public T this[int index] { get => ((IList<T>)mDataList)[index]; set => ((IList<T>)mDataList)[index] = value; }

    public DataObjectList(IDataObject? parent = null) : base(parent)
    {
        mDataList = new(this);
        mDataList.ItemAdded.Subscribe(OnAdd);
        mDataList.ItemRemoved.Subscribe(OnRemove);
        mDataList.ItemReplaced.Subscribe(OnReplace);
    }

    public List<T> GetInfo()
    {
        return mDataList.GetInfo();
    }

    public int IndexOf(T item)
    {
        return ((IList<T>)mDataList).IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        ((IList<T>)mDataList).Insert(index, item);
    }

    public void RemoveAt(int index)
    {
        ((IList<T>)mDataList).RemoveAt(index);
    }

    public void Add(T item)
    {
        ((ICollection<T>)mDataList).Add(item);
    }

    public void Clear()
    {
        ((ICollection<T>)mDataList).Clear();
    }

    public bool Contains(T item)
    {
        return ((ICollection<T>)mDataList).Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ((ICollection<T>)mDataList).CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        return ((ICollection<T>)mDataList).Remove(item);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)mDataList).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)mDataList).GetEnumerator();
    }

    public void SetInfo(IEnumerable<T> info)
    {
        mDataList.SetInfo(info);
    }

    void OnAdd(T item)
    {
        item.Attach(this);
    }

    void OnRemove(T item)
    {
        item.Detach();
    }

    void OnReplace(T before, T after)
    {
        OnRemove(before);
        OnAdd(after);
    }

    readonly DataList<T> mDataList;
}
