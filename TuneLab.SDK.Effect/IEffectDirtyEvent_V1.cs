namespace TuneLab.SDK.Effect;

public interface IEffectDirtyEvent_V1
{
    EffectDirtyType_V1 DirtyType { get; }
    void Accept();
}
