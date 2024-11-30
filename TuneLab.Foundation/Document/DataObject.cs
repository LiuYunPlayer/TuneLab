using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
