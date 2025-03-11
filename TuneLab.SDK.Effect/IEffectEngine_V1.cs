using TuneLab.SDK.Base.ControllerConfigs;
using TuneLab.SDK.Base.DataStructures;
using TuneLab.SDK.Base.Property;

namespace TuneLab.SDK.Effect;

public interface IEffectEngine_V1
{
    ObjectConfig_V1 PropertyConfig { get; }
    IReadOnlyOrderedMap_V1<string, AutomationConfig_V1> AutomationConfig { get; }
    void Init(IReadOnlyMap_V1<string, IReadOnlyPropertyValue_V1> args);
    void Destroy();
    IEffectSynthesisTask_V1 CreateSynthesisTask(IEffectSynthesisInput_V1 input, IEffectSynthesisOutput_V1 output);
}
