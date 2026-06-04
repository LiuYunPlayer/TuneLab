using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.DataStructures;

using TuneLab.Primitives.DataStructures;
namespace TuneLab.Foundation.Document;

public class DataObjectMap<TKey, TValue> : DataObject, IDataObjectMap<TKey, TValue> where TKey : notnull where TValue : IDataObject
{
    public IModifiedEvent MapModified => mMap.Modified;
    public IActionEvent<TKey, TValue> ItemAdded => mMap.ItemAdded;
    public IActionEvent<TKey, TValue> ItemRemoved => mMap.ItemRemoved;
    public IActionEvent<TKey, TValue, TValue> ItemReplaced => mMap.ItemReplaced;
    public ICollection<TKey> Keys => mMap.Keys;
    public ICollection<TValue> Values => mMap.Values;

    // 收敛集合接口要一元（仅值）的增删事件；映射公开的是二元（键+值），故另持一元投影，在 OnAdd/OnRemove 里同步触发。
    IActionEvent<TValue> IReadOnlyDataCollection<TValue>.ItemAdded => mValueAdded;
    IActionEvent<TValue> IReadOnlyDataCollection<TValue>.ItemRemoved => mValueRemoved;
    public IEnumerable<TValue> Items => mMap.Values;
    public int Count => mMap.Count;
    public TValue this[TKey key] { get => mMap[key]; set => mMap[key] = value; }

    public DataObjectMap(IDataObject? parent = null) : base(parent)
    {
        mMap = new(this);
        mMap.ItemAdded.Subscribe(OnAdd);
        mMap.ItemRemoved.Subscribe(OnRemove);
        mMap.ItemReplaced.Subscribe(OnReplace);
    }

    public void Add(TKey key, TValue value)
    {
        mMap.Add(key, value);
    }

    public bool ContainsKey(TKey key)
    {
        return mMap.ContainsKey(key);
    }

    public bool Remove(TKey key)
    {
        return mMap.Remove(key);
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        return mMap.TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        mMap.Add(item);
    }

    public void Clear()
    {
        mMap.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return mMap.Contains(item);
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        mMap.CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return mMap.Remove(item);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return mMap.GetEnumerator();
    }

    public TValue? GetValue(TKey key, out bool success)
    {
        return mMap.GetValue(key, out success);
    }

    public Map<TKey, TValue> GetInfo()
    {
        return mMap.GetInfo();
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)mMap).IsReadOnly;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
    IReadOnlyCollection<TKey> IReadOnlyMap<TKey, TValue>.Keys => ((IReadOnlyMap<TKey, TValue>)mMap).Keys;
    IReadOnlyCollection<TValue> IReadOnlyMap<TKey, TValue>.Values => ((IReadOnlyMap<TKey, TValue>)mMap).Values;

    IEnumerator<IReadOnlyKeyValuePair<TKey, TValue>> IEnumerable<IReadOnlyKeyValuePair<TKey, TValue>>.GetEnumerator()
    {
        return ((IEnumerable<IReadOnlyKeyValuePair<TKey, TValue>>)mMap).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    void IDataObject<IReadOnlyMap<TKey, TValue>>.SetInfo(IReadOnlyMap<TKey, TValue> info)
    {
        mMap.SetInfo(info);
    }

    void OnAdd(TKey key, TValue item)
    {
        item.Attach(this);
        mValueAdded.Invoke(item);
    }

    void OnRemove(TKey key, TValue item)
    {
        item.Detach();
        mValueRemoved.Invoke(item);
    }

    void OnReplace(TKey key, TValue before, TValue after)
    {
        OnRemove(key, before);
        OnAdd(key, after);
    }

    readonly ActionEvent<TValue> mValueAdded = new();
    readonly ActionEvent<TValue> mValueRemoved = new();
    readonly DataMap<TKey, TValue> mMap;
}
