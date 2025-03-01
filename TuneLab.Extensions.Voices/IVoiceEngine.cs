using TuneLab.Base.Structures;

namespace TuneLab.Extensions.Voices;

public interface IVoiceEngine
{
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; }
    bool Init(string enginePath, out string? error);
    void Destroy();
    IVoiceSource CreateVoiceSource(string id);
}
