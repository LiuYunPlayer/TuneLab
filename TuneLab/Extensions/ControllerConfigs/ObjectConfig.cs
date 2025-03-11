using TuneLab.Foundation;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.ControllerConfigs;

public class ObjectConfig : IControllerConfig
{
    public IReadOnlyOrderedMap<string, IControllerConfig> Configs { get; set; } = [];
}
