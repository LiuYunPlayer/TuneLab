using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Effect;

public interface IEffectEngine
{
    ObjectConfig PropertyConfig { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfig { get; }
    void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> args);
    void Destroy();
    IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output);
}
