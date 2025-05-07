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
        // TODO: Implement
    }

    public PropertyObject GetInfo()
    {
        return new PropertyObject(base.GetInfo());
    }

    readonly DataMap<string, IDataProperty<IReadOnlyPropertyValue>> mMap = [];
}
