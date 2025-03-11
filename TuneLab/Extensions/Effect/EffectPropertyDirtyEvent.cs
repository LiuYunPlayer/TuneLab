using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal class EffectPropertyDirtyEvent : EffectDirtyEvent, IEffectPropertyDirtyEvent_V1
{
    protected override EffectDirtyType_V1 DirtyType_V1 => ((IEffectPropertyDirtyEvent_V1)this).DirtyType;

}
