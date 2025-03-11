using TuneLab.SDK.Base.Synthesizer;

namespace TuneLab.SDK.Effect;

public interface IEffectSynthesisTask_V1
{
    event Action<double>? Progress;
    event Action<SynthesisError_V1?>? Finished;

    void Start();
    void Stop();
    void OnDirtyEvent(IEffectDirtyEvent_V1 dirtyEvent);
}
