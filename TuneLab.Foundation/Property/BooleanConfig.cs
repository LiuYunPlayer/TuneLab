using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Property;

public class BooleanConfig(bool defaultValue = false) : IValueConfig<bool>
{
    public bool DefaultValue { get; set; } = defaultValue;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
