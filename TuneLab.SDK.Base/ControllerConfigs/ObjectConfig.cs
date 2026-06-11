using TuneLab.Primitives.DataStructures;

namespace TuneLab.SDK.Base.ControllerConfigs;

public class ObjectConfig : IControllerConfig
{
    public string? DisplayText { get; init; }
    public required IReadOnlyOrderedMap<string, IControllerConfig> Properties { get; init; }
}
