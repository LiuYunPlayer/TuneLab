using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;

namespace TuneLab.Base.Properties;

public class DataPropertyObject : DataObject, IDataObject<PropertyObject>, IReadOnlyMap<string, PropertyValue>
{
    //public IActionEvent<PropertyPath, IPropertyValue> PropertyAdded => mPropertyAdded;
    //public IActionEvent<PropertyPath, IPropertyValue> PropertyRemoved => mPropertyRemoved;
    //public IActionEvent<PropertyPath, IPropertyValue, IPropertyValue> PropertyReplaced => mPropertyReplaced;
    public IActionEvent<PropertyPath> PropertyModified => mPropertyModified;

    public IReadOnlyCollection<string> Keys => mMap.Keys;

    public IReadOnlyCollection<PropertyValue> Values => mMap.Values.Convert(v => v.Value);

    public int Count => mMap.Count;

    public PropertyValue this[string key] => throw new NotImplementedException();

    public DataPropertyObject() : this(null) { }

    public DataPropertyObject(IDataObject? parent = null) : base(parent)
    {
        mMap.Attach(this);

        mMap.ItemAdded.Subscribe(OnAdd);
        mMap.ItemRemoved.Subscribe(OnRemove);
        mMap.ItemReplaced.Subscribe(OnReplace);
    }

    public PropertyValue GetValue(PropertyPath.Key key, PropertyValue defaultValue)
    {
        if (!mMap.TryGetValue(key, out var dataPropertyValue))
            return defaultValue;

        var propertyValue = dataPropertyValue.Value;
        if (propertyValue.TypeEquals(defaultValue))
            return propertyValue;

        if (propertyValue.ToObject(out var propertyObject) && key.IsObject)
            return ((DataPropertyObject)propertyObject.Map).GetValue(key.Next, defaultValue);

        return defaultValue;
    }

    public PropertyValue GetValue(PropertyPath.Key key)
    {
        if (!mMap.TryGetValue(key, out var dataPropertyValue))
            return PropertyValue.Invalid;

        var propertyValue = dataPropertyValue.Value;
        if (!key.IsObject)
            return propertyValue;

        if (propertyValue.ToObject(out var propertyObject))
            return ((DataPropertyObject)propertyObject.Map).GetValue(key.Next);

        return PropertyValue.Invalid;
    }

    public DataPropertyValue SetValue(PropertyPath.Key key, PropertyValue value)
    {
        var dataPropertyValue = FindValue(key);
        if (key.IsObject)
        {
            var propertyValue = dataPropertyValue.Value;
            if (!propertyValue.ToObject(out var propertyObject))
            {
                propertyObject = new(new DataPropertyObject());
                dataPropertyValue.Set(propertyObject);
            }

            var dataPropertyObject = (DataPropertyObject)propertyObject.Map;
            dataPropertyObject.SetValue(key.Next, value);

        }
        else
        {
            dataPropertyValue.Set(value);
        }
        return dataPropertyValue;
    }

    DataPropertyValue FindValue(string key)
    {
        if (!mMap.TryGetValue(key, out var dataPropertyValue))
        {
            dataPropertyValue = new DataPropertyValue();
            mMap.Add(key, dataPropertyValue);
        }

        return dataPropertyValue;
    }

    void OnAdd(string key, DataPropertyValue value)
    {
        if (value.Value.ToObject(out var objectValue))
        {
            var dataPropertyObject = (DataPropertyObject)objectValue.Map;
            //dataPropertyObject.PropertyAdded.Subscribe((path, value) => { mPropertyAdded.Invoke(new PropertyPath(key).Combine(path), value); });
            //dataPropertyObject.PropertyRemoved.Subscribe((path, value) => { mPropertyRemoved.Invoke(new PropertyPath(key).Combine(path), value); });
            //dataPropertyObject.PropertyReplaced.Subscribe((path, before, after) => { mPropertyReplaced.Invoke(new PropertyPath(key).Combine(path), before, after); });
            Action<PropertyPath> modified = (path) => { mPropertyModified.Invoke(new PropertyPath(key).Combine(path)); };
            dataPropertyObject.PropertyModified.Subscribe(modified);
            mPropertyModifiedActions.Add(key, modified);
        }
        else
        {
            Action modified = () => { mPropertyModified.Invoke(new PropertyPath(key)); };
            value.Modified.Subscribe(modified);
            mModifiedActions.Add(key, modified);
        }
    }

    void OnRemove(string key, DataPropertyValue value)
    {
        if (value.Value.ToObject(out var objectValue))
        {
            var dataPropertyObject = (DataPropertyObject)objectValue.Map;
            dataPropertyObject.PropertyModified.Unsubscribe(mPropertyModifiedActions[key]);
            mPropertyModifiedActions.Remove(key);
        }
        else
        {
            value.Modified.Unsubscribe(mModifiedActions[key]);
            mModifiedActions.Remove(key);
        }
    }

    void OnReplace(string key, DataPropertyValue before, DataPropertyValue after)
    {
        OnRemove(key, before);
        OnAdd(key, after);
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
                IDataObject<IReadOnlyMap<string, PropertyValue>>.SetInfo(dataPropertyObject, objectValue);
                value = PropertyValue.Create(new PropertyObject(dataPropertyObject));
            }
            IDataObject<IReadOnlyMap<string, PropertyValue>>.SetInfo(dataPropertyValue, value);
            map.Add(key, dataPropertyValue);
        }
        IDataObject<IReadOnlyMap<string, PropertyValue>>.SetInfo(mMap, map);
    }

    public bool ContainsKey(string key)
    {
        return mMap.ContainsKey(key);
    }

    public PropertyValue GetValue(string key, out bool success)
    {
        var dataPropertyValue = mMap.GetValue(key, out success);
        return dataPropertyValue == null ? PropertyValue.Invalid : dataPropertyValue.Value;
    }

    public IEnumerator<IReadOnlyKeyValuePair<string, PropertyValue>> GetEnumerator()
    {
        return mMap.GetEnumerator().Convert(kvp => new ReadOnlyKeyValuePair<string, PropertyValue>(kvp.Key, kvp.Value.Value));
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    readonly Map<string, Action<PropertyPath>> mPropertyModifiedActions = new();
    readonly Map<string, Action> mModifiedActions = new();

    readonly DataObjectMap<string, DataPropertyValue> mMap = new();

    //ActionEvent<PropertyPath, IPropertyValue> mPropertyAdded = new();
    //ActionEvent<PropertyPath, IPropertyValue> mPropertyRemoved = new();
    //ActionEvent<PropertyPath, IPropertyValue, IPropertyValue> mPropertyReplaced = new();
    readonly ActionEvent<PropertyPath> mPropertyModified = new();
}
