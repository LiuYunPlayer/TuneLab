using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace ExtensionCompatibilityLayer.Voice;

internal class VoiceEngine(TuneLab.Extensions.Voices.IVoiceEngine voiceEngine, string enginePath) : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context)
    {
        return new VoiceSource(voiceEngine.CreateVoiceSource(context.VoiceID), context);
    }

    public void Destroy()
    {
        voiceEngine.Destroy();
    }

    public void Init()
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

    public ObjectConfig GetContextPropertyConfig(IEnumerable<IVoiceSynthesisContext> contexts)
    {
        return new ObjectConfig();
    }

    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEnumerable<IVoiceSynthesisContext> contexts)
    {
        throw new NotImplementedException();
    }

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = [];
}
