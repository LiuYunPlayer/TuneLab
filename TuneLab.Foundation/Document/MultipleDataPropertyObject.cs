using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Foundation.Document;

public class MultipleDataPropertyObject(IDataObject parent, IEnumerable<IDataPropertyObject> dataPropertyObjects) : IDataObject.Wrapper(parent), IDataPropertyObject
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
