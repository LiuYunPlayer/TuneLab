using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Voice;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace ExtensionCompatibilityLayer.Voice;

internal class VoiceEngine(TuneLab.Extensions.Voices.IVoiceEngine voiceEngine, string enginePath) : TuneLab.Extensions.Voice.IVoiceEngine
{
    public IReadOnlyOrderedMap<string, TuneLab.Extensions.Voice.VoiceSourceInfo> VoiceInfos => throw new NotImplementedException();

    public TuneLab.Extensions.Voice.IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context)
    {
        throw new NotImplementedException();
    }

    public void Destroy()
    {
        voiceEngine.Destroy();
    }

    public void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> properties)
    {
        voiceEngine.Init(enginePath, out var error);
        if (error != null)
            throw new Exception($"Failed to initialize voice engine: {error}");
    }
}
