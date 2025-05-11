using System.Diagnostics.CodeAnalysis;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Effect;

public interface IEffectSynthesisInput
{
    MonoAudio Audio { get; }
    IReadOnlyMap<string, IReadOnlyPropertyValue> Properties { get; }
    bool TryGetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter? automation);
}
