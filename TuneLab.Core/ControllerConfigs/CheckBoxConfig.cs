using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.ControllerConfigs;

public class CheckBoxConfig : IControllerConfig
{
    public required bool DefaultValue { get; set; }
}
