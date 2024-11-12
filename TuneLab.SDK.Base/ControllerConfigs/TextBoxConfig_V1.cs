using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class TextBoxConfig_V1 : IControllerConfig_V1
{
    public PropertyString_V1 DefaultValue { get; set; } = string.Empty;
    public bool IsSingleLine { get; set; } = true;

    PropertyValue_V1 IControllerConfig_V1.DefaultValue => DefaultValue;
}
