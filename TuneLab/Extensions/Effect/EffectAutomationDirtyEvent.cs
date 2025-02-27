using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal class EffectAutomationDirtyEvent : EffectDirtyEvent, IEffectAutomationDirtyEvent_V1
{
    protected override EffectDirtyType_V1 DirtyType_V1 => ((IEffectAutomationDirtyEvent_V1)this).DirtyType;
    public string Key { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
}
