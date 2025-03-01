using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal abstract class EffectDirtyEvent : IEffectDirtyEvent_V1
{
    public bool Handled { get; set; }
    protected abstract EffectDirtyType_V1 DirtyType_V1 { get; }

    public void Accept()
    {
        Handled = true;
    }

    // V1 Adapter
    EffectDirtyType_V1 IEffectDirtyEvent_V1.DirtyType => DirtyType_V1;
    void IEffectDirtyEvent_V1.Accept() => Accept();
}
