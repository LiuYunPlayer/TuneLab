namespace TuneLab.SDK.Format.DataInfo;

public class AutomationInfo_V1
{
    public double DefaultValue { get; set; }
    public List<AutomationPointInfo_V1> Points { get; set; } = [];
}
