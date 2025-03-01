using TuneLab.Foundation.Document;

namespace TuneLab.Base.Properties;

public class DataPropertyValue : DataStruct<PropertyValue>
{
    public DataPropertyValue()
    {
        SetInfo(PropertyValue.Invalid);
    }

    public DataPropertyValue(bool value)
    {
        SetInfo(PropertyValue.Create(value));
    }

    public DataPropertyValue(double value)
    {
        SetInfo(PropertyValue.Create(value));
    }

    public DataPropertyValue(string value)
    {
        SetInfo(PropertyValue.Create(value));
    }

    protected override void SetInfo(PropertyValue info)
    {
        {
            if (Value.ToObject(out var propertyObject))
            {
                if (propertyObject.Map is IDataObject dataObject)
                    dataObject.Detach();
            }
        }
        base.SetInfo(info);
        {
            if (info.ToObject(out var propertyObject))
            {
                if (propertyObject.Map is IDataObject dataObject)
                    dataObject.Attach(this);
            }
        }
    }
}