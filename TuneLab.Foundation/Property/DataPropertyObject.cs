using System;
using System.Collections;
using System.Collections.Generic;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.DataStructures;

using TuneLab.Primitives.DataStructures;
using TuneLab.Primitives.Property;
namespace TuneLab.Foundation.Property;

public class DataPropertyObject : DataObject, IDataObject<PropertyObject>, IReadOnlyMap<string, PropertyValue>, IDataPropertyObject, DataPropertyObject.ILazyObjectNode
{
    public IReadOnlyCollection<string> Keys => ((IReadOnlyMap<string, DataPropertyValue>)mMap).Keys;

    public IReadOnlyCollection<PropertyValue> Values => ((IReadOnlyMap<string, DataPropertyValue>)mMap).Values.Convert(v => v.Value);

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

        var propertyValue = dataPropertyValue.Value;
        return propertyValue.TypeEquals(defaultValue) ? propertyValue : defaultValue;
    }

    // 本层叶子写：缺键则建叶子。撤销由 DataPropertyValue 自带命令承担。
    public DataPropertyValue SetValue(string key, PropertyValue value)
    {
        var dataPropertyValue = FindValue(key);
        dataPropertyValue.Set(value);
        return dataPropertyValue;
    }

    void IDataPropertyObject.SetValue(string key, PropertyValue value) => SetValue(key, value);

    // 导航到嵌套对象：返回懒视图，读经 FindObject（不创建）、写经 GetOrCreateObject（按需建路径）。
    public IDataPropertyObject Object(string key) => new ObjectView(this, this, key);

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
            var key = kvp.Key;
            var value = kvp.Value.GetInfo();
            if (value.ToObject(out var objectValue))
            {
                var dataPropertyObject = (DataPropertyObject)objectValue.Map;
                value = PropertyValue.Create(dataPropertyObject.GetInfo());
            }
            info.Add(key, value);
        }
        return new PropertyObject(info);
    }

    void IDataObject<PropertyObject>.SetInfo(PropertyObject info)
    {
        var map = new Map<string, DataPropertyValue>();
        foreach (var kvp in info.Map)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            DataPropertyValue dataPropertyValue = new();
            if (value.ToObject(out var objectValue))
            {
                var dataPropertyObject = new DataPropertyObject();
                dataPropertyObject.SetInfo(objectValue);
                value = PropertyValue.Create(new PropertyObject(dataPropertyObject));
            }
            dataPropertyValue.SetInfo(value);
            map.Add(key, dataPropertyValue);
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
        return dataPropertyValue == null ? PropertyValue.Null : dataPropertyValue.Value;
    }

    public IEnumerator<IReadOnlyKeyValuePair<string, PropertyValue>> GetEnumerator()
    {
        return mMap.GetEnumerator().Convert(kvp => new ReadOnlyKeyValuePair<string, PropertyValue>(kvp.Key, kvp.Value.Value));
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // 单选侧懒导航的内部契约：解析真实子对象（不创建）/ find-or-create 子对象。不进公开接口（仅 ObjectView 与本类互链）。
    internal interface ILazyObjectNode
    {
        DataPropertyObject? FindObject(string key);
        DataPropertyObject GetOrCreateObject(string key);
    }

    DataPropertyObject? ILazyObjectNode.FindObject(string key)
    {
        if (mMap.TryGetValue(key, out var dataPropertyValue) && dataPropertyValue.Value.ToObject(out var objectValue))
            return (DataPropertyObject)objectValue.Map;

        return null;
    }

    DataPropertyObject ILazyObjectNode.GetOrCreateObject(string key)
    {
        var dataPropertyValue = FindValue(key);
        if (!dataPropertyValue.Value.ToObject(out var objectValue))
        {
            objectValue = new(new DataPropertyObject());
            dataPropertyValue.Set(objectValue);
        }

        return (DataPropertyObject)objectValue.Map;
    }

    // 嵌套对象节点的懒视图：表示 owner 下 key 处的对象。读经 owner.FindObject 返回 default（不创建），
    // 写经 owner.GetOrCreateObject 按需建路径；借壳 root 转发整套文档身份（撤销/Modified 根锚最外层对象）。
    // 解析出的子对象本身是 ILazyObjectNode（concrete DataPropertyObject），故继续向下导航直接 cast 链。
    sealed class ObjectView(IDataObject root, ILazyObjectNode owner, string key)
        : IDataObject.Wrapper(root), IDataPropertyObject, ILazyObjectNode
    {
        public IDataPropertyObject Object(string subKey) => new ObjectView(root, this, subKey);

        public PropertyValue GetValue(string subKey, PropertyValue defaultValue)
            => owner.FindObject(key)?.GetValue(subKey, defaultValue) ?? defaultValue;

        public void SetValue(string subKey, PropertyValue value)
            => owner.GetOrCreateObject(key).SetValue(subKey, value);

        DataPropertyObject? ILazyObjectNode.FindObject(string subKey) => ((ILazyObjectNode?)owner.FindObject(key))?.FindObject(subKey);
        DataPropertyObject ILazyObjectNode.GetOrCreateObject(string subKey) => ((ILazyObjectNode)owner.GetOrCreateObject(key)).GetOrCreateObject(subKey);
    }

    readonly DataObjectMap<string, DataPropertyValue> mMap = new();
}
