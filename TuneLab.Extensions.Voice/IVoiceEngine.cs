using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice;

public interface IVoiceEngine
{
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; }
    void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> properties);
    void Destroy();
    IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context);
}
