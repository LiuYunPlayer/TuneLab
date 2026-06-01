using TuneLab.Primitives.DataStructures;

namespace TuneLab.SDK.Base;

public class ObjectConfig(IReadOnlyOrderedMap<string, IControllerConfig> properties) : IControllerConfig
{
    public IReadOnlyOrderedMap<string, IControllerConfig> Properties { get; private set; } = properties;
}
