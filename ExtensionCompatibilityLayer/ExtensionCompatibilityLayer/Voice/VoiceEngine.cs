using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace ExtensionCompatibilityLayer.Voice;

internal class VoiceEngine(TuneLab.Extensions.Voices.IVoiceEngine voiceEngine, string enginePath) : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context)
    {
        throw new NotImplementedException();
    }

    public void Destroy()
    {
        voiceEngine.Destroy();
    }

    public void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> properties)
    {
        if (!voiceEngine.Init(enginePath, out var error))
            throw new Exception(error);

        var voiceInfos = voiceEngine.VoiceInfos;
        foreach (var kvp in voiceInfos)
        {
            var voiceInfo = kvp.Value;
            mVoiceInfos.Add(kvp.Key, new VoiceSourceInfo() { Name = voiceInfo.Name, Description = voiceInfo.Description });
        }
    }

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = [];
}
