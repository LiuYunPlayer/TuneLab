using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Property;

public class ObjectConfig(IReadOnlyOrderedMap<string, IPropertyConfig> properties) : IPropertyConfig
{
    public IReadOnlyOrderedMap<string, IPropertyConfig> Properties { get; private set; } = properties;
}
