using TuneLab.Primitives.DataStructures;

namespace TuneLab.SDK.Base;

public class ObjectConfig : IControllerConfig
{
    public required IReadOnlyOrderedMap<string, IControllerConfig> Properties { get; init; }
}
