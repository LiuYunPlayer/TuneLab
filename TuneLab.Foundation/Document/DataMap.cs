using System.Collections;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public class DataMap<TKey, TValue>(IDataObject? parent = null) : DataObject(parent), IDataMap<TKey, TValue> where TKey : notnull
{
    public IActionEvent<TKey, TValue> ItemAdded => mItemAdded;
    public IActionEvent<TKey, TValue> ItemRemoved => mItemRemoved;
    public IActionEvent<TKey, TValue, TValue> ItemReplaced => mItemReplaced;
    public IReadOnlyCollection<TKey> Keys => mMap.Keys;
    public IReadOnlyCollection<TValue> Values => mMap.Values;
    public int Count => mMap.Count;
    public TValue this[TKey key]
    {
        get => mMap[key];
        set
        {
            PushAndDo(new ModifiedCommand(this, key, mMap[key], value));
        }
    }

    public void Add(TKey key, TValue value)
    {
        PushAndDo(new AddCommand(this, key, value));
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        if (Count == 0)
            return;

        BeginMergeNotify();
        var keys = mMap.Keys;
        foreach (var key in keys)
        {
            Remove(key);
        }
        EndMergeNotify();
    }

    public bool ContainsKey(TKey key)
    {
        return mMap.ContainsKey(key);
    }

    public bool Remove(TKey key)
    {
        if (!this.TryGetValue(key, out var value))
            return false;

        PushAndDo(new RemoveCommand(this, key, value));
        return true;
    }

    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => ((IReadOnlyMap<TKey, TValue>)mMap).Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => ((IReadOnlyMap<TKey, TValue>)mMap).Values;

    public Map<TKey, TValue> GetInfo()
    {
        return mMap.ToMap();
    }

    void IDataObject<IReadOnlyMap<TKey, TValue>>.SetInfo(IReadOnlyMap<TKey, TValue> info)
    {
        foreach (var kvp in mMap)
        {
            mItemRemoved.Invoke(kvp.Key, kvp.Value);
        }

        mMap.Clear();

        foreach (var kvp in info)
        {
            mMap.Add(kvp.Key, kvp.Value);
            mItemAdded.Invoke(kvp.Key, kvp.Value);
        }
    }

    IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> IEnumerable<IReadOnlyKeyValuePair<TKey, TValue>>.GetEnumerator()
    {
        return ((IEnumerable<IReadOnlyKeyValuePair<TKey, TValue>>)mMap).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)mMap).GetEnumerator();
    }

    public TValue? GetValue(TKey key, out bool success)
    {
        return ((IReadOnlyMap<TKey, TValue>)mMap).GetValue(key, out success);
    }

    class AddCommand(DataMap<TKey, TValue> dataMap, TKey key, TValue value) : ICommand
    {
        public void Redo()
        {
            dataMap.mMap.Add(key, value);
            dataMap.mItemAdded.Invoke(key, value);
            dataMap.Notify();
        }

        public void Undo()
        {
            dataMap.mMap.Remove(key);
            dataMap.mItemRemoved.Invoke(key, value);
            dataMap.Notify();
        }
    }

    class RemoveCommand(DataMap<TKey, TValue> dataMap, TKey key, TValue value) : ICommand
    {
        public void Redo()
        {
            dataMap.mMap.Remove(key);
            dataMap.mItemRemoved.Invoke(key, value);
            dataMap.Notify();
        }

        public void Undo()
        {
            dataMap.mMap.Add(key, value);
            dataMap.mItemAdded.Invoke(key, value);
            dataMap.Notify();
        }
    }

    class ModifiedCommand(DataMap<TKey, TValue> dataMap, TKey key, TValue before, TValue after) : ICommand
    {
        public void Redo()
        {
            dataMap.mMap[key] = after;
            dataMap.mItemReplaced.Invoke(key, before, after);
            dataMap.Notify();
        }

        public void Undo()
        {
            dataMap.mMap[key] = before;
            dataMap.mItemReplaced.Invoke(key, after, before);
            dataMap.Notify();
        }
    }

    readonly Map<TKey, TValue> mMap = new();
    ActionEvent<TKey, TValue> mItemAdded = new();
    ActionEvent<TKey, TValue> mItemRemoved = new();
    ActionEvent<TKey, TValue, TValue> mItemReplaced = new();
}
