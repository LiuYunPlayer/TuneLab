using System;
using System.Collections;
using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public class DataPropertyObject : DataObject, IDataObject<PropertyObject>, IReadOnlyMap<string, PropertyValue>, IDataPropertyObject, ILazyObjectNode
{
    public IReadOnlyCollection<string> Keys => ((IReadOnlyMap<string, DataPropertyValue>)mMap).Keys;

    public IReadOnlyCollection<PropertyValue> Values => ((IReadOnlyMap<string, DataPropertyValue>)mMap).Values.Convert(v => v.Value.ToPropertyValue());

    public int Count => mMap.Count;

    public PropertyValue this[string key] => throw new NotImplementedException();

    public DataPropertyObject() : this(null) { }

    public DataPropertyObject(IDataObject? parent = null) : base(parent)
    {
        mMap.Attach(this);
    }

    // 本层叶子读：缺键或存值类型与 default 不符（如配置改过类型）一律退回 default。嵌套对象由 Object(key) 导航，不在此下钻。
    public PropertyValue GetValue(string key, PropertyValue defaultValue)
    {
        if (!mMap.TryGetValue(key, out var dataPropertyValue))
            return defaultValue;

        var propertyValue = dataPropertyValue.Value.ToPropertyValue();
        return propertyValue.TypeEquals(defaultValue) ? propertyValue : defaultValue;
    }

    // 本层叶子写：缺键则建叶子。撤销由 DataPropertyValue 自带命令承担。
    public DataPropertyValue SetValue(string key, PropertyValue value)
    {
        var dataPropertyValue = FindValue(key);
        dataPropertyValue.Set(PropertySlot.Canonicalize(value));
        return dataPropertyValue;
    }

    void IDataPropertyObject.SetValue(string key, PropertyValue value) => SetValue(key, value);

    // 移除某键（撤销由底层 DataObjectMap.Remove 命令承担）。缺键 = no-op。
    public void RemoveValue(string key) => mMap.Remove(key);

    // 导航到嵌套对象：返回懒视图，读经 FindObject（不创建）、写经 GetOrCreateObject（按需建路径）。
    public IDataPropertyObject Object(string key) => new ObjectView(this, this, key);

    // 导航到嵌套数组：同对象懒视图，读经 FindArray（不创建）、写经 GetOrCreateArray（按需建路径）。
    public IDataPropertyArray Array(string key) => new ArrayView(this, this, key);

    DataPropertyValue FindValue(string key)
    {
        if (!mMap.TryGetValue(key, out var dataPropertyValue))
        {
            dataPropertyValue = new DataPropertyValue();
            mMap.Add(key, dataPropertyValue);
        }

        return dataPropertyValue;
    }

    public PropertyObject GetInfo()
    {
        var info = new Map<string, PropertyValue>();
        foreach (var kvp in mMap)
        {
            info.Add(kvp.Key, kvp.Value.Value.ToPropertyValue());   // 对象槽经子树值快照递归触底
        }
        return new PropertyObject(info);
    }

    public void SetInfo(PropertyObject info)
    {
        var map = new Map<string, DataPropertyValue>();
        foreach (var kvp in info.Map)
        {
            DataPropertyValue dataPropertyValue = new();
            dataPropertyValue.SetInfo(PropertySlot.Canonicalize(kvp.Value));
            map.Add(kvp.Key, dataPropertyValue);
        }
        mMap.SetInfo(map);
    }

    public bool ContainsKey(string key)
    {
        return mMap.ContainsKey(key);
    }

    public PropertyValue GetValue(string key, out bool success)
    {
        var dataPropertyValue = mMap.GetValue(key, out success);
        return dataPropertyValue == null ? PropertyValue.Null : dataPropertyValue.Value.ToPropertyValue();
    }

    public IEnumerator<IReadOnlyKeyValuePair<string, PropertyValue>> GetEnumerator()
    {
        return mMap.GetEnumerator().Convert(kvp => new ReadOnlyKeyValuePair<string, PropertyValue>(kvp.Key, kvp.Value.Value.ToPropertyValue()));
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // ILazyObjectNode（key=字段键）：解析真实子对象/子数组（不创建）/ find-or-create。契约与视图见 LazyPropertyNavigation.cs。
    DataPropertyObject? ILazyObjectNode.FindObject(string key)
    {
        return mMap.TryGetValue(key, out var dataPropertyValue) ? dataPropertyValue.Value.Object : null;
    }

    DataPropertyObject ILazyObjectNode.GetOrCreateObject(string key)
    {
        var dataPropertyValue = FindValue(key);
        if (dataPropertyValue.Value.Object is { } existing)
            return existing;

        var child = new DataPropertyObject();
        dataPropertyValue.Set(new PropertySlot(child));
        return child;
    }

    DataPropertyArray? ILazyObjectNode.FindArray(string key)
    {
        return mMap.TryGetValue(key, out var dataPropertyValue) ? dataPropertyValue.Value.Array : null;
    }

    DataPropertyArray ILazyObjectNode.GetOrCreateArray(string key)
    {
        var dataPropertyValue = FindValue(key);
        if (dataPropertyValue.Value.Array is { } existing)
            return existing;

        var child = new DataPropertyArray();
        dataPropertyValue.Set(new PropertySlot(child));
        return child;
    }

    readonly DataObjectMap<string, DataPropertyValue> mMap = new();
}
