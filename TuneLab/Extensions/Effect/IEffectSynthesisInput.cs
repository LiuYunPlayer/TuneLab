using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Synthesizer;
using TuneLab.SDK.Base;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal interface IEffectSynthesisInput : IEffectSynthesisInput_V1
{
    MonoAudio Audio { get; }
    PropertyObject Properties { get; }
    bool TryGetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter? automation);

    // V1 Adapter
    MonoAudio_V1 IEffectSynthesisInput_V1.Audio => Audio;
    PropertyObject_V1 IEffectSynthesisInput_V1.Properties => Properties;
    bool IEffectSynthesisInput_V1.TryGetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter_V1? automation) => TryGetAutomation(automationID, out automation);
}
