using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Property;

namespace TuneLab.Foundation.Document;

public interface IDataPropertyArray : IDataList<IPropertyValue>
{
    IPropertyValue GetValueAt(int index);
    void SetValueAt(int index, IPropertyValue value);
}

public interface IDataPropertyObject : IDataObject<PropertyObject>, IReadOnlyMap<string, IPropertyValue>, IPropertyValue
{
    PropertyType IReadOnlyPropertyValue.Type => PropertyType.Object;

    IReadOnlyMap<string, IReadOnlyPropertyValue>? IReadOnlyPropertyValue.Object => this;

    IDataPropertyObjectField GetField(string key);
}

public interface IDataPropertyObjectField
{
    IPropertyValue GetValue(IPropertyValue defaultValue);
    void SetValue(IPropertyValue value);
    IDataProperty<bool> ToBoolean();
    IDataProperty<double> ToNumber();
    IDataProperty<string> ToString();
    IDataPropertyArray ToArray();
    IDataPropertyObject ToObject();
    IDataProperty<ReadOnlyPrimitiveValue> ToPrimitive();
}

public static class IDataPropertyObjectFieldExtensions
{
    public static IPropertyValue GetValue(this IDataPropertyObjectField dataPropertyObjectField, PropertyValue defaultValue)
    {
        return dataPropertyObjectField.GetValue(defaultValue.UnBox());
    }
}
/*
public static class IDataPropertyObjectExtensions
{
    public static IDataPropertyObjectField GetField(this IDataMap<string, IDataProperty<IPropertyValue>> map, string key)
    {
        return new DataPropertyObjectField(map, key);
    }

    class DataPropertyObjectField(IDataMap<string, IPropertyValue> map, string key) : IDataObject.Wrapper(map), IDataPropertyObjectField
    {
        public IPropertyValue GetValue(IPropertyValue defaultValue)
        {
            return map.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public void SetValue(IPropertyValue value)
        {
            if (map.ContainsKey(key))
            {
                map[key] = value;
            }
            else
            {
                map.Add(key, value);
            }
        }

        public IDataList<IDataProperty<IPropertyValue>> ToArray()
        {
            throw new NotImplementedException();
        }

        public IDataProperty<bool> ToBoolean()
        {
            throw new NotImplementedException();
        }

        public IDataProperty<double> ToNumber()
        {
            throw new NotImplementedException();
        }

        public new IDataProperty<string> ToString()
        {
            throw new NotImplementedException();
        }

        public IDataMap<string, IDataProperty<IPropertyValue>> ToObject()
        {
            return new DataPropertyObjectObjectField(this);
        }

        class DataPropertyObjectObjectField(DataPropertyObjectField dataPropertyObjectField) : IDataObject.Wrapper(dataPropertyObjectField), IDataMap<string, IReadOnlyPropertyValue>
        {
            public IReadOnlyPropertyValue this[string key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            IReadOnlyPropertyValue IReadOnlyMap<string, IReadOnlyPropertyValue>.this[string key] => throw new NotImplementedException();

            public IActionEvent<string, IReadOnlyPropertyValue> ItemAdded => throw new NotImplementedException();

            public IActionEvent<string, IReadOnlyPropertyValue> ItemRemoved => throw new NotImplementedException();

            public IActionEvent<string, IReadOnlyPropertyValue, IReadOnlyPropertyValue> ItemReplaced => throw new NotImplementedException();

            public IReadOnlyCollection<string> Keys => throw new NotImplementedException();

            public IReadOnlyCollection<IReadOnlyPropertyValue> Values => throw new NotImplementedException();

            public int Count => throw new NotImplementedException();

            public void Add(string key, IReadOnlyPropertyValue value)
            {
                throw new NotImplementedException();
            }

            public void Clear()
            {
                throw new NotImplementedException();
            }

            public bool ContainsKey(string key)
            {
                throw new NotImplementedException();
            }

            public IEnumerator<IReadOnlyKeyValuePair<string, IReadOnlyPropertyValue>> GetEnumerator()
            {
                throw new NotImplementedException();
            }

            public Map<string, IReadOnlyPropertyValue> GetInfo()
            {
                throw new NotImplementedException();
            }

            public IReadOnlyPropertyValue? GetValue(string key, out bool success)
            {
                throw new NotImplementedException();
            }

            public bool Remove(string key)
            {
                throw new NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                throw new NotImplementedException();
            }

            void IDataObject<IReadOnlyMap<string, IReadOnlyPropertyValue>>.SetInfo(IReadOnlyMap<string, IReadOnlyPropertyValue> info)
            {
                throw new NotImplementedException();
            }
        }
    }
}*/