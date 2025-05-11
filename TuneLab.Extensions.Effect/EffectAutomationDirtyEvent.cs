namespace TuneLab.Extensions.Effect;

public class EffectAutomationDirtyEvent : EffectDirtyEvent
{
    public string Key { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
}
