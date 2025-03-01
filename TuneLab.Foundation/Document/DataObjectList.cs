using System.Collections;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public class DataObjectList<T> : DataObject, IDataObjectList<T> where T : class, IDataObject
{
    public IActionEvent ListModified => mListModified;
    public IActionEvent<T> ItemAdded => ((IReadOnlyDataList<T>)mDataList).ItemAdded;
    public IActionEvent<T> ItemRemoved => ((IReadOnlyDataList<T>)mDataList).ItemRemoved;
    public IActionEvent<T, T> ItemReplaced => ((IReadOnlyDataList<T>)mDataList).ItemReplaced;

    public int Count => ((ICollection<T>)mDataList).Count;
    public bool IsReadOnly => ((ICollection<T>)mDataList).IsReadOnly;

    public T this[int index] { get => ((IList<T>)mDataList)[index]; set => ((IList<T>)mDataList)[index] = value; }

    public DataObjectList(IDataObject? parent = null) : base(parent)
    {
        mDataList = new(this);
        mDataList.Modified.Subscribe(mListModified);
        mDataList.ItemAdded.Subscribe(OnAdd);
        mDataList.ItemRemoved.Subscribe(OnRemove);
        mDataList.ItemReplaced.Subscribe(OnReplace);
    }

    public IEvent<TEvent> Any<TEvent>(ISubscriber<T, TEvent> subscriber)
    {
        return new AnyEvent<TEvent>(this, subscriber);
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

    void IDataObject<IEnumerable<T>>.SetInfo(IEnumerable<T> info)
    {
        IDataObject<IEnumerable<T>>.SetInfo(mDataList, info);
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

    class AnyEvent<TEvent> : IEvent<TEvent>
    {
        public AnyEvent(DataObjectList<T> dataObjectList, ISubscriber<T, TEvent> subscriber)
        {
            mDataObjectList = dataObjectList;
            mSubscriber = subscriber;

            mDataObjectList.ItemAdded.Subscribe(OnAdd);
            mDataObjectList.ItemRemoved.Subscribe(OnRemove);
        }

        public void Subscribe(TEvent invokable)
        {
            foreach (var dataObject in mDataObjectList)
            {
                mSubscriber.Subscribe(dataObject, invokable);
            }

            mEvents.Add(invokable);
        }

        public void Unsubscribe(TEvent invokable)
        {
            foreach (var dataObject in mDataObjectList)
            {
                mSubscriber.Unsubscribe(dataObject, invokable);
            }

            mEvents.Remove(invokable);
        }

        void OnAdd(T item)
        {
            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(item, invokable);
            }
        }

        void OnRemove(T item)
        {
            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(item, invokable);
            }
        }

        readonly DataObjectList<T> mDataObjectList;
        readonly ISubscriber<T, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = new();
    }

    readonly ActionEvent mListModified = new();
    readonly DataList<T> mDataList;
}
