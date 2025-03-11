using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Synthesizer;

internal interface ISynthesisOutput
{
    MonoAudio Audio { get; set; }
    IMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedAutomations { get; }
}
