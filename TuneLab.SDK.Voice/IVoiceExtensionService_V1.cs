using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.SDK.Voice;

public interface IVoiceExtensionService_V1
{
    IReadOnlyOrderedMap_V1<string, IVoiceEngine_V1> VoiceEngines { get; }
    void Load();
}
