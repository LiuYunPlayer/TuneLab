using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Adapters.DataStructures;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base.ControllerConfigs;

namespace TuneLab.Extensions.Adapters.ControllerConfigs;

internal static class ObjectConfigAdapter
{
    public static ObjectConfig ToDomain(this ObjectConfig_V1 v1)
    {
        return new ObjectConfig() { Configs = v1.Configs.ToDomain().Convert(IControllerConfigAdapter.ToDomain) };
    }
}
