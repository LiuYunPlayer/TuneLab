using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;

using TuneLab.Primitives.DataStructures;
using TuneLab.Primitives.Property;
namespace TuneLab.Foundation.Property;

public class DataPropertyValue : DataStruct<PropertyValue>
{
    public DataPropertyValue()
    {
        SetValue(PropertyValue.Null);
    }

    public DataPropertyValue(bool value)
    {
        SetValue(PropertyValue.Create(value));
    }

    public DataPropertyValue(double value)
    {
        SetValue(PropertyValue.Create(value));
    }

    public DataPropertyValue(string value)
    {
        SetValue(PropertyValue.Create(value));
    }

    // 裸写时维护嵌套对象的 attach/detach：先脱离旧值里的文档对象，落值后再挂上新值里的。
    protected override void SetValue(PropertyValue value)
    {
        if (Value.ToObject(out var oldObject))
        {
            if (oldObject.Map is IDataObject dataObject)
                dataObject.Detach();
        }
        base.SetValue(value);
        if (value.ToObject(out var newObject))
        {
            if (newObject.Map is IDataObject dataObject)
                dataObject.Attach(this);
        }
    }
}
