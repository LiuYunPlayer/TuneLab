using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.SDK.Base.ControllerConfigs;

namespace TuneLab.Extensions.Adapters.ControllerConfigs;

internal static class AutomationConfigAdapter
{
    public static AutomationConfig ToDomain(this AutomationConfig_V1 v1)
    {
        return new AutomationConfig()
        {
            DefaultValue = v1.DefaultValue,
            MaxValue = v1.MaxValue,
            MinValue = v1.MinValue,
        };
    }
}
