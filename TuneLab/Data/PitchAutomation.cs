using System;
using System.Collections.Generic;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;

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
