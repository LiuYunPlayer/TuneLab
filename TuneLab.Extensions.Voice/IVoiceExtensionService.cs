using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voice;

public interface IVoiceExtensionService
{
    IEnumerable<VoiceExtensionEntry> VoiceExtensions { get; }
    void Load();
}
