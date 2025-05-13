using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Base.Event;

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
