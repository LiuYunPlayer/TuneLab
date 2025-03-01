namespace TuneLab.SDK.Effect;

public interface IEffectAutomationDirtyEvent_V1 : IEffectDirtyEvent_V1
{
    EffectDirtyType_V1 IEffectDirtyEvent_V1.DirtyType => EffectDirtyType_V1.Automation;
    string Key { get; }
    double StartTime { get; }
    double EndTime { get; }
}
