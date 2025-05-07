using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voice;

internal interface IVoiceSynthesisOutput : ISynthesisOutput
{
    IReadOnlyMap<ISynthesisNote, SynthesizedPhoneme[]> SynthesizedPhonemes { get; set; }
}
