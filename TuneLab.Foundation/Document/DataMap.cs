using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Document;

public class DataMap<TKey, TValue>(IDataObject? parent = null) : DataObject(parent), IDataMap<TKey, TValue> where TKey : notnull
{
    public IActionEvent<TKey, TValue> ItemAdded => mItemAdded;
    public IActionEvent<TKey, TValue> ItemRemoved => mItemRemoved;
    public IActionEvent<TKey, TValue, TValue> ItemReplaced => mItemReplaced;
    public ICollection<TKey> Keys => ((IDictionary<TKey, TValue>)mMap).Keys;
    public ICollection<TValue> Values => ((IDictionary<TKey, TValue>)mMap).Values;
    public int Count => ((ICollection<KeyValuePair<TKey, TValue>>)mMap).Count;
    public TValue this[TKey key]
    {
        get => ((IDictionary<TKey, TValue>)mMap)[key];
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

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)mMap).Contains(item);
    }

    public bool ContainsKey(TKey key)
    {
        return ((IDictionary<TKey, TValue>)mMap).ContainsKey(key);
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<TKey, TValue>>)mMap).CopyTo(array, arrayIndex);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<TKey, TValue>>)mMap).GetEnumerator();
    }

    public bool Remove(TKey key)
    {
        if (!TryGetValue(key, out var value))
            return false;

        PushAndDo(new RemoveCommand(this, key, value));
        return true;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (!((ICollection<KeyValuePair<TKey, TValue>>)mMap).Remove(item))
            return false;

        mItemRemoved.Invoke(item.Key, item.Value);
        Notify();
        Push(new RemoveCommand(this, item.Key, item.Value));

        return true;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        return ((IDictionary<TKey, TValue>)mMap).TryGetValue(key, out value);
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)mMap).IsReadOnly;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => ((IReadOnlyDictionary<TKey, TValue>)mMap).Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => ((IReadOnlyDictionary<TKey, TValue>)mMap).Values;
    IEnumerable<TKey> IReadOnlyMap<TKey, TValue>.Keys => ((IReadOnlyMap<TKey, TValue>)mMap).Keys;
    IEnumerable<TValue> IReadOnlyMap<TKey, TValue>.Values => ((IReadOnlyMap<TKey, TValue>)mMap).Values;

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

    IEnumerator<IReadOnlyKeyWithValue<TKey, TValue>> IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>.GetEnumerator()
    {
        return ((IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>)mMap).GetEnumerator();
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
