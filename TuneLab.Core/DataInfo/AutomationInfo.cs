using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Core.DataInfo;

public class AutomationInfo
{
    public double DefaultValue { get; set; }
    public List<Point> Points { get; set; } = new();
}
