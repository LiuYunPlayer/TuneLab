using TuneLab.Extensions.Synthesizer;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal abstract class EffectDirtyEvent : DirtyEvent, IEffectDirtyEvent_V1
{
    protected abstract EffectDirtyType_V1 DirtyType_V1 { get; }

    // V1 Adapter
    EffectDirtyType_V1 IEffectDirtyEvent_V1.DirtyType => DirtyType_V1;
    void IEffectDirtyEvent_V1.Accept() => Accept();
}
