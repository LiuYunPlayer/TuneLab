namespace TuneLab.Foundation.Document;

public class DataObject : IDataObject.Implementation
{
    public DataObject(IDataObject? parent = null)
    {
        if (parent == null)
            return;

        Attach(parent);
    }
}
