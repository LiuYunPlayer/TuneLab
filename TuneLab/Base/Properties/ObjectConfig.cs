using TuneLab.Foundation.DataStructures;

namespace TuneLab.Base.Properties;

public class ObjectConfig(IReadOnlyOrderedMap<string, IPropertyConfig> properties) : IPropertyConfig
{
    public IReadOnlyOrderedMap<string, IPropertyConfig> Properties { get; private set; } = properties;
}
