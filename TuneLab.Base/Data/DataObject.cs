namespace TuneLab.Base.Data;

public class DataObject : IDataObject.Implementation
{
    public DataObject(IDataObject? parent = null)
    {
        if (parent == null)
            return;

        Attach(parent);
    }
}
