using System.Diagnostics.CodeAnalysis;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal interface IEffectSynthesisInput
{
    MonoAudio Audio { get; }
    IReadOnlyMap<string, IReadOnlyPropertyValue> Properties { get; }
    bool TryGetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter? automation);
}
