using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Adapters.DataStructures;
using TuneLab.Extensions.Adapters.Property;
using TuneLab.Extensions.Adapters.Synthesizer;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base.DataStructures;
using TuneLab.SDK.Base.Property;
using TuneLab.SDK.Base.Synthesizer;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Adapters.Effect;

internal static class IEffectSynthesisInputAdapter
{
    public static IEffectSynthesisInput_V1 ToV1(this IEffectSynthesisInput domain)
    {
        return new IEffectSynthesisInput_V1Adapter(domain);
    }

    class IEffectSynthesisInput_V1Adapter(IEffectSynthesisInput domain) : IEffectSynthesisInput_V1
    {
        public MonoAudio_V1 Audio => domain.Audio.ToV1();

        public IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> Properties => domain.Properties.Convert(IReadOnlyPropertyValueAdapter.ToV1).ToV1();

        public bool TryGetAutomation(string automationID, [MaybeNullWhen(false), NotNullWhen(true)] out IAutomationValueGetter_V1? automation)
        {
            automation = default;
            if (!domain.TryGetAutomation(automationID, out var automationValueGetter))
                return false;

            automation = automationValueGetter.ToV1();
            return true;
        }
    }
}
