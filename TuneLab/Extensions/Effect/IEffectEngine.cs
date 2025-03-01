using TuneLab.Base.Properties;
using TuneLab.SDK.Base;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal interface IEffectEngine : IEffectEngine_V1
{
    string PropertyConfig { get; }
    string AutomationConfig { get; }
    void Initialize(PropertyObject args);
    void Destroy();
    IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output);

    // V1 Adapter
    string IEffectEngine_V1.PropertyConfig => PropertyConfig;
    string IEffectEngine_V1.AutomationConfig => AutomationConfig;
    void IEffectEngine_V1.Initialize(PropertyObject_V1 args) => Initialize(args);
    void IEffectEngine_V1.Destroy() => Destroy();
    IEffectSynthesisTask_V1 IEffectEngine_V1.CreateSynthesisTask(IEffectSynthesisInput_V1 input, IEffectSynthesisOutput_V1 output) => CreateSynthesisTask(input, output);
}
