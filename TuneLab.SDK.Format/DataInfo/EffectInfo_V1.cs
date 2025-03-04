using TuneLab.SDK.Base;

namespace TuneLab.SDK.Format.DataInfo;

public class EffectInfo_V1
{
    public string Type { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Map_V1<string, AutomationInfo_V1> Automations { get; set; } = [];
    public PropertyObject_V1 Properties { get; set; } = [];
}
