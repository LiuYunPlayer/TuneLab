using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Data;

internal class PitchAutomation : DataObject, IDataObject<IReadOnlyList<AutomationInfo>>
{
    public PitchAutomation()
    {

    }

    public void AddLine(IReadOnlyList<Point> points)
    {

    }

    public IReadOnlyList<AutomationInfo> GetInfo()
    {
        throw new NotImplementedException();
    }

    public void SetInfo(IReadOnlyList<AutomationInfo> info)
    {
        throw new NotImplementedException();
    }
}
