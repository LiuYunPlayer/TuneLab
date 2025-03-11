using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.SDK.Base.Synthesizer;

public interface ISynthesisOutput_V1
{
    MonoAudio_V1 Audio { get; set; }
    IMap_V1<string, IReadOnlyList<IReadOnlyList<Point_V1>>> SynthesizedAutomations { get; }
}
