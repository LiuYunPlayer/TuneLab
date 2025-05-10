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

public class DataPropertyObject(IDataObject? parent = null) : DataMap<string, IPropertyValue>(parent), IDataPropertyObject
{
    public IDataPropertyObjectField GetField(string key)
    {
        throw new NotImplementedException();
    }

    void IDataObject<PropertyObject>.SetInfo(PropertyObject info)
    {
        ((IDataObject<IReadOnlyMap<string, IPropertyValue>>)this).SetInfo(info);
    }

    public new PropertyObject GetInfo()
    {
        return new PropertyObject(base.GetInfo());
    }

    class DataPropertyObjectField(DataPropertyObject parent, string key) : DataObject(parent), IDataPropertyObjectField
    {
        public IPropertyValue GetValue(IPropertyValue defaultValue)
        {
            if (parent.ContainsKey(key))
            {
                return parent[key];
            }
            else
            {
                return defaultValue;
            }
        }

        public void SetValue(IPropertyValue value)
        {
            parent[key] = value;
        }
    }
}
