namespace TuneLab.SDK.Base;

public interface ISynthesisOutput_V1
{
    MonoAudio_V1 Audio { get; set; }
    IDictionary<string, IReadOnlyList<IReadOnlyList<Point_V1>>> SynthesizedAutomations { get; }
}
