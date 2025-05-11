using System.Collections.Generic;

namespace TuneLab.Extensions.Voice;

internal interface IVoiceExtensionService
{
    IReadOnlyList<IVoiceEngine> VoiceEngines { get; }
    void Load();
}
