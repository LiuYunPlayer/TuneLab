using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.ControllerConfigs;

public class ObjectConfig(IReadOnlyOrderedMap<string, IControllerConfig> propertyConfigs) : IControllerConfig
{
    public ObjectConfig() : this([]) { }

    public IReadOnlyOrderedMap<string, IControllerConfig> PropertyConfigs { get; set; } = propertyConfigs;
}
