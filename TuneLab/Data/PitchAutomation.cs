using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;

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

    void IDataObject<IReadOnlyList<AutomationInfo>>.SetInfo(IReadOnlyList<AutomationInfo> info)
    {
        throw new NotImplementedException();
    }
}
