using System.Diagnostics.CodeAnalysis;
using TuneLab.SDK.Base.DataStructures;
using TuneLab.SDK.Base.Property;
using TuneLab.SDK.Base.Synthesizer;

namespace TuneLab.SDK.Effect;

public interface IEffectSynthesisInput_V1
{
    MonoAudio_V1 Audio { get; }
    IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> Properties { get; }
    bool TryGetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter_V1? automation);
}
