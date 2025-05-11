using System;
using System.Collections.Generic;

namespace TuneLab.Extensions.Voice.BuiltIn;

internal class VoiceExtensionService : IVoiceExtensionService
{
    public IReadOnlyList<IVoiceEngine> VoiceEngines => throw new NotImplementedException();

    public void Load()
    {
        throw new NotImplementedException();
    }
}
