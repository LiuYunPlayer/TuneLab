using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Voice;
using TuneLab.Extensions.Voices;

namespace ExtensionCompatibilityLayer.Voice;

internal class SynthesisData(IVoiceSynthesisInput input) : ISynthesisData
{
    public IEnumerable<TuneLab.Extensions.Voices.ISynthesisNote> Notes => input.Notes.Select(note => new SynthesisNote(note));

    public PropertyObject PartProperties => throw new NotImplementedException();

    public IAutomationValueGetter Pitch => throw new NotImplementedException();

    public bool GetAutomation(string automationID, [MaybeNullWhen(false), NotNullWhen(true)] out IAutomationValueGetter? automation)
    {
        throw new NotImplementedException();
    }
}
