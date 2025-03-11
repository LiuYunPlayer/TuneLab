using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.SDK.Base.ControllerConfigs;

public class ObjectConfig_V1 : IControllerConfig_V1
{
    public IReadOnlyOrderedMap_V1<string, IControllerConfig_V1> Configs { get; set; } = [];
}
