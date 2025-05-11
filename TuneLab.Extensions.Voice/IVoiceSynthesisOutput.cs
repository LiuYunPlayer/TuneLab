using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voice;

public interface IVoiceSynthesisOutput : ISynthesisOutput
{
    IReadOnlyMap<ISynthesisNote, SynthesizedPhoneme[]> SynthesizedPhonemes { get; set; }
}
