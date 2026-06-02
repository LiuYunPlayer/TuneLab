using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Properties;

public class ObjectConfig(IReadOnlyOrderedMap<string, IPropertyConfig> properties) : IPropertyConfig
{
    public IReadOnlyOrderedMap<string, IPropertyConfig> Properties { get; private set; } = properties;
}
