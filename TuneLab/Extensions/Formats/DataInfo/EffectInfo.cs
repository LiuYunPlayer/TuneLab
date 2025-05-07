
using TuneLab.Foundation.Property;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Formats.DataInfo;

public class EffectInfo
{
    public string Type { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Map<string, AutomationInfo> Automations { get; set; } = [];
    public PropertyObject Properties { get; set; } = [];
}
