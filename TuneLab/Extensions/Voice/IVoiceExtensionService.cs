using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Voice;

internal interface IVoiceExtensionService
{
    IReadOnlyList<IVoiceEngine> VoiceEngines { get; }
    void Load();
}
