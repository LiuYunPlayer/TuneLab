using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voice;

public interface IVoiceExtensionService
{
    IReadOnlyOrderedMap<string, IVoiceEngine> VoiceEngines { get; }
    void Load();
}
