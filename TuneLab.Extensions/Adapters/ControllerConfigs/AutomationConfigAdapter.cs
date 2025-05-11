using TuneLab.Extensions.ControllerConfigs;
using TuneLab.SDK.Base.ControllerConfigs;

namespace TuneLab.Extensions.Adapters.ControllerConfigs;

internal static class AutomationConfigAdapter
{
    public static AutomationConfig ToDomain(this AutomationConfig_V1 v1)
    {
        return new AutomationConfig()
        {
            Name = v1.Name,
            DefaultValue = v1.DefaultValue,
            MaxValue = v1.MaxValue,
            MinValue = v1.MinValue,
        };
    }
}
