using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice;

internal interface IVoiceSynthesisInput
{
    IReadOnlyList<ISynthesisNote> Notes { get; }
    IReadOnlyMap<string, IReadOnlyPropertyValue> Properties { get; }
    bool GetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter? automation);
    IAutomationValueGetter Pitch { get; }
}
