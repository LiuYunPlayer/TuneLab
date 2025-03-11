using TuneLab.Extensions.Adapters.ControllerConfigs;
using TuneLab.Extensions.Adapters.DataStructures;
using TuneLab.Extensions.Adapters.Effect;
using TuneLab.Extensions.Adapters.Property;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal interface IEffectEngine
{
    ObjectConfig PropertyConfig { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfig { get; }
    void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> args);
    void Destroy();
    IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output);
}

internal class EffectEngine_V1(IEffectEngine_V1 impl) : IEffectEngine
{
    public ObjectConfig PropertyConfig => impl.PropertyConfig.ToDomain();
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfig => impl.AutomationConfig.ToDomain().Convert(AutomationConfigAdapter.ToDomain);

    public IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output) => impl.CreateSynthesisTask(input.ToV1(), output.ToV1()).ToDomain();
    public void Destroy() => impl.Destroy();
    public void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> args) => impl.Init(args.Convert(IReadOnlyPropertyValueAdapter.ToV1).ToV1());
}