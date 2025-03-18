using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.SDK.Voice;

public interface IVoiceExtensionService_V1
{
    IReadOnlyOrderedMap_V1<string, IVoiceEngine_V1> VoiceEngines { get; }
    void Load();
}
