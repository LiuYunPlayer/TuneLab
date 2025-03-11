using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Voice.BuiltIn;

internal class VoiceExtensionService : IVoiceExtensionService
{
    public IReadOnlyList<IVoiceEngine> VoiceEngines => throw new NotImplementedException();

    public void Load()
    {
        throw new NotImplementedException();
    }
}
