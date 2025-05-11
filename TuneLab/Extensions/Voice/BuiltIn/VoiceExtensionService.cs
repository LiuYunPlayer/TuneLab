using System;
using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voice.BuiltIn;

internal class VoiceExtensionService : IVoiceExtensionService
{
    public IReadOnlyOrderedMap<string, IVoiceEngine> VoiceEngines => mVoiceEngines;

    public void Load()
    {
        
    }

    OrderedMap<string, IVoiceEngine> mVoiceEngines = [];
}
