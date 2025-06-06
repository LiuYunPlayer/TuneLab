using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice;

public interface IVoiceEngine
{
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; }
    void Init();
    void Destroy();
    IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context);
    ObjectConfig GetContextPropertyConfig(IEnumerable<IVoiceSynthesisContext> contexts);
    IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEnumerable<IVoiceSynthesisContext> contexts);
}
