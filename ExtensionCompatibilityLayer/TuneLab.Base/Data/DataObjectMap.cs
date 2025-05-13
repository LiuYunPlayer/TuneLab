﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using TuneLab.Base.Event;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Data;

public class DataObjectMap<TKey, TValue> : DataObject, IDataObjectMap<TKey, TValue> where TKey : notnull where TValue : IDataObject
{
    public IActionEvent MapModified => mMapModified;
    public IActionEvent<TKey, TValue> ItemAdded => mMap.ItemAdded;
    public IActionEvent<TKey, TValue> ItemRemoved => mMap.ItemRemoved;
    public IActionEvent<TKey, TValue, TValue> ItemReplaced => mMap.ItemReplaced;
    public ICollection<TKey> Keys => mMap.Keys;
    public ICollection<TValue> Values => mMap.Values;
    public int Count => mMap.Count;
    public TValue this[TKey key] { get => mMap[key]; set => mMap[key] = value; }

    public DataObjectMap(IDataObject? parent = null) : base(parent)
    {
        mMap = new(this);
        mMap.ItemAdded.Subscribe(OnAdd);
        mMap.ItemRemoved.Subscribe(OnRemove);
        mMap.ItemReplaced.Subscribe(OnReplace);
    }

    public IEvent<TEvent> Any<TEvent>(ISubscriber<TValue, TEvent> subscriber)
    {
        return new AnyEvent<TEvent>(this, subscriber);
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
    IEnumerable<TKey> IReadOnlyMap<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyMap<TKey, TValue>.Values => Values;

    IEnumerator<IReadOnlyKeyWithValue<TKey, TValue>> IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>.GetEnumerator()
    {
        return ((IEnumerable<IReadOnlyKeyWithValue<TKey, TValue>>)mMap).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    void IDataObject<IReadOnlyMap<TKey, TValue>>.SetInfo(IReadOnlyMap<TKey, TValue> info)
    {
        IDataObject<IReadOnlyMap<TKey, TValue>>.SetInfo(mMap, info);
    }

    void OnAdd(TKey key, TValue item)
    {
        item.Attach(this);
    }

    void OnRemove(TKey key, TValue item)
    {
        item.Detach();
    }

    void OnReplace(TKey key, TValue before, TValue after)
    {
        OnRemove(key, before);
        OnAdd(key, after);
    }

    class AnyEvent<TEvent> : IEvent<TEvent>
    {
        public AnyEvent(DataObjectMap<TKey, TValue> dataObjectMap, ISubscriber<TValue, TEvent> subscriber)
        {
            mDataObjectMap = dataObjectMap;
            mSubscriber = subscriber;

            mDataObjectMap.ItemAdded.Subscribe(OnAdd);
            mDataObjectMap.ItemRemoved.Subscribe(OnRemove);
        }

        public void Subscribe(TEvent invokable)
        {
            foreach (var dataObject in mDataObjectMap.Values)
            {
                mSubscriber.Subscribe(dataObject, invokable);
            }

            mEvents.Add(invokable);
        }

        public void Unsubscribe(TEvent invokable)
        {
            foreach (var dataObject in mDataObjectMap.Values)
            {
                mSubscriber.Unsubscribe(dataObject, invokable);
            }

            mEvents.Remove(invokable);
        }

        void OnAdd(TKey key, TValue item)
        {
            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(item, invokable);
            }
        }

        void OnRemove(TKey key, TValue item)
        {
            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(item, invokable);
            }
        }

        readonly DataObjectMap<TKey, TValue> mDataObjectMap;
        readonly ISubscriber<TValue, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = new();
    }

    readonly DataMap<TKey, TValue> mMap;
    readonly ActionEvent mMapModified = new();
}
