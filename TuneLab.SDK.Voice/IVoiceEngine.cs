using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.DataStructures;

namespace TuneLab.SDK.Voice;

public interface IVoiceEngine
{
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; }
    bool Init(string enginePath, out string? error);
    void Destroy();
    IVoiceSource CreateVoiceSource(string id);
}
