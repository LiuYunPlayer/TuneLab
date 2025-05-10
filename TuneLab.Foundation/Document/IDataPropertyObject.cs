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

public interface IDataPropertyObject : IDataObject<PropertyObject>, IReadOnlyMap<string, IPropertyValue>, IPropertyObject
{
    IDataPropertyObjectField GetField(string key);
}

public interface IDataPropertyObjectField : IDataObject
{
    IPropertyValue GetValue(IPropertyValue defaultValue);
    void SetValue(IPropertyValue value);
}

public static class IDataPropertyObjectFieldExtensions
{
    public static IPropertyValue GetValue(this IDataPropertyObjectField dataPropertyObjectField, PropertyValue defaultValue)
    {
        return dataPropertyObjectField.GetValue(defaultValue.UnBox());
    }

    public static void SetValue(this IDataPropertyObjectField dataPropertyObjectField, PropertyValue value)
    {
         dataPropertyObjectField.SetValue(value.UnBox());
    }

    public static IDataProperty<bool> ToBoolean(this IDataPropertyObjectField dataPropertyObjectField, bool defaultValue)
    {
        return new DataPropertyObjectFieldBoolean(dataPropertyObjectField, defaultValue);
    }

    class DataPropertyObjectFieldBoolean(IDataPropertyObjectField dataPropertyObjectField, bool defaultValue) : IDataObject.Wrapper(dataPropertyObjectField), IDataProperty<bool>
    {
        public bool Value => dataPropertyObjectField.GetValue(defaultValue).ToBoolean(out var value) ? value : defaultValue;

        public bool GetInfo()
        {
            return Value;
        }

        public void SetInfo(bool info)
        {
            dataPropertyObjectField.SetValue(info);
        }
    }

    public static IDataProperty<double> ToNumber(this IDataPropertyObjectField dataPropertyObjectField, double defaultValue)
    {
        return new DataPropertyObjectFieldNumber(dataPropertyObjectField, defaultValue);
    }

    class DataPropertyObjectFieldNumber(IDataPropertyObjectField dataPropertyObjectField, double defaultValue) : IDataObject.Wrapper(dataPropertyObjectField), IDataProperty<double>
    {
        public double Value => dataPropertyObjectField.GetValue(defaultValue).ToNumber(out var value) ? value : defaultValue;

        public double GetInfo()
        {
            return Value;
        }

        public void SetInfo(double info)
        {
            dataPropertyObjectField.SetValue(info);
        }
    }

    public static IDataProperty<string> ToString(this IDataPropertyObjectField dataPropertyObjectField, string defaultValue)
    {
        return new DataPropertyObjectFieldString(dataPropertyObjectField, defaultValue);
    }

    class DataPropertyObjectFieldString(IDataPropertyObjectField dataPropertyObjectField, string defaultValue) : IDataObject.Wrapper(dataPropertyObjectField), IDataProperty<string>
    {
        public string Value => dataPropertyObjectField.GetValue(defaultValue).ToString(out var value) ? value : defaultValue;

        public string GetInfo()
        {
            return Value;
        }

        public void SetInfo(string info)
        {
            dataPropertyObjectField.SetValue(info);
        }
    }

    public static IDataPropertyArray ToArray(this IDataPropertyObjectField dataPropertyObjectField)
    {
        return new DataPropertyObjectFieldArray(dataPropertyObjectField);
    }

    class DataPropertyObjectFieldArray(IDataPropertyObjectField dataPropertyObjectField) : IDataObject.Wrapper(dataPropertyObjectField), IDataPropertyArray
    {
        public IPropertyValue this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IActionEvent<IPropertyValue, IPropertyValue> ItemReplaced => throw new NotImplementedException();

        public IActionEvent<IPropertyValue> ItemAdded => throw new NotImplementedException();

        public IActionEvent<IPropertyValue> ItemRemoved => throw new NotImplementedException();

        public IEnumerable<IPropertyValue> Items => throw new NotImplementedException();

        public int Count => throw new NotImplementedException();

        public bool IsReadOnly => throw new NotImplementedException();

        public void Add(IPropertyValue item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Contains(IPropertyValue item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(IPropertyValue[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<IPropertyValue> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public List<IPropertyValue> GetInfo()
        {
            throw new NotImplementedException();
        }

        public IPropertyValue GetValueAt(int index)
        {
            throw new NotImplementedException();
        }

        public int IndexOf(IPropertyValue item)
        {
            throw new NotImplementedException();
        }

        public void Insert(int index, IPropertyValue item)
        {
            throw new NotImplementedException();
        }

        public bool Remove(IPropertyValue item)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        public void SetInfo(IEnumerable<IPropertyValue> info)
        {
            throw new NotImplementedException();
        }

        public void SetValueAt(int index, IPropertyValue value)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public static IDataPropertyObject ToObject(this IDataPropertyObjectField dataPropertyObjectField)
    {
        return new DataPropertyObjectFieldObject(dataPropertyObjectField);
    }

    class DataPropertyObjectFieldObject(IDataPropertyObjectField dataPropertyObjectField) : IDataObject.Wrapper(dataPropertyObjectField), IDataPropertyObject
    {
        public IPropertyValue this[string key] => throw new NotImplementedException();

        public IReadOnlyCollection<string> Keys => throw new NotImplementedException();

        public IReadOnlyCollection<IPropertyValue> Values => throw new NotImplementedException();

        public int Count => throw new NotImplementedException();

        public bool ContainsKey(string key)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<IReadOnlyKeyValuePair<string, IPropertyValue>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public IDataPropertyObjectField GetField(string key)
        {
            throw new NotImplementedException();
        }

        public PropertyObject GetInfo()
        {
            throw new NotImplementedException();
        }

        public IPropertyValue? GetValue(string key, out bool success)
        {
            throw new NotImplementedException();
        }

        public void SetInfo(PropertyObject info)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public static IDataProperty<IPrimitiveValue> ToPrimitive(this IDataPropertyObjectField dataPropertyObjectField, IPrimitiveValue defaultValue)
    {
        return new DataPropertyObjectFieldPrimitive(dataPropertyObjectField, defaultValue);
    }

    public static IDataProperty<IPrimitiveValue> ToPrimitive(this IDataPropertyObjectField dataPropertyObjectField, PrimitiveValue defaultValue)
    {
        return dataPropertyObjectField.ToPrimitive(defaultValue.UnBoxing());
    }

    class DataPropertyObjectFieldPrimitive(IDataPropertyObjectField dataPropertyObjectField, IPrimitiveValue defaultValue) : IDataObject.Wrapper(dataPropertyObjectField), IDataProperty<IPrimitiveValue>
    {
        public IPrimitiveValue Value => dataPropertyObjectField.GetValue(defaultValue) as IPrimitiveValue ?? defaultValue;

        public IPrimitiveValue GetInfo()
        {
            return Value;
        }

        public void SetInfo(IPrimitiveValue info)
        {
            dataPropertyObjectField.SetValue(info);
        }
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