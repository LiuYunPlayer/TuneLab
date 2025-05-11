using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Synthesizer;

public interface ISynthesisOutput
{
    MonoAudio Audio { get; set; }
    IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch { get; set; }
    IMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedAutomations { get; }
}
