using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.SDK.Format.DataInfo;

public class EffectInfo_V1
{
    public string Type { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, AutomationInfo_V1> Automations { get; set; } = [];
    public PropertyObject_V1 Properties { get; set; } = [];
}
