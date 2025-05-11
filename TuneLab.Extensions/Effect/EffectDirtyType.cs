using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal enum EffectDirtyType
{
    Automation,
    Property,
}

internal static class EffectDirtyTypeExtensions
{
    public static EffectDirtyType_V1 ToV1(this EffectDirtyType dirtyType)
    {
        return dirtyType switch
        {
            EffectDirtyType.Automation => EffectDirtyType_V1.Automation,
            EffectDirtyType.Property => EffectDirtyType_V1.Property,
            _ => (EffectDirtyType_V1)dirtyType
        };
    }
}