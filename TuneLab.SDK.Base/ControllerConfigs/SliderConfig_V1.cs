using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public sealed class SliderConfig_V1 : IControllerConfig_V1
{
    public required PropertyNumber_V1 DefaultValue { get; set; }
    public required PropertyNumber_V1 MinValue { get; set; }
    public required PropertyNumber_V1 MaxValue { get; set; }
    public bool IsInteger { get; set; } = false;

    PropertyValue_V1 IControllerConfig_V1.DefaultValue => DefaultValue;
}
