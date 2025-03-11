using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.ControllerConfigs;

public class AutomationConfig : IControllerConfig
{
    public required double DefaultValue { get; set; }
    public required double MinValue { get; set; }
    public required double MaxValue { get; set; }
}
