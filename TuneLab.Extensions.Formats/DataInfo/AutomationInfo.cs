using TuneLab.Base.Structures;

namespace TuneLab.Extensions.Formats.DataInfo;

public class AutomationInfo
{
    public double DefaultValue { get; set; }
    public List<Point> Points { get; set; } = new();
}
