using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;

namespace TuneLab.Extensions.Formats.DataInfo;

public class EffectInfo
{
    public string Type { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Map<string, AutomationInfo> Automations { get; set; } = new();
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
}
