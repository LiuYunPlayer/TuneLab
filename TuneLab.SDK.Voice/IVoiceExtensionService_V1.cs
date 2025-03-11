using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Voice;

public interface IVoiceExtensionService_V1
{
    IReadOnlyList<IVoiceEngine_V1> VoiceEngines { get; }
    void Load();
}
