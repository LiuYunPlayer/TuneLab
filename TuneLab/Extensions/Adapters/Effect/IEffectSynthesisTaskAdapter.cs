using System;
using TuneLab.Extensions.Adapters.Synthesizer;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Synthesizer;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Adapters.Effect;

internal static class IEffectSynthesisTaskAdapter
{
    public static IEffectSynthesisTask ToDomain(this IEffectSynthesisTask_V1 v1)
    {
        return new IEffectSynthesisTaskAdapater_V1(v1);
    }

    class IEffectSynthesisTaskAdapater_V1 : IEffectSynthesisTask
    {
        public event Action<double>? Progress { add { v1.Progress += value; } remove { v1.Progress -= value; } }
        public event Action<SynthesisError?>? Finished;

        public IEffectSynthesisTaskAdapater_V1(IEffectSynthesisTask_V1 v1)
        {
            this.v1 = v1;
            this.v1.Finished += error => Finished?.Invoke(error?.ToDomain());
        }

        public void OnDirtyEvent(EffectDirtyEvent dirtyEvent) => v1.OnDirtyEvent(dirtyEvent);
        public void Start() => v1.Start();
        public void Stop() => v1.Stop();

        readonly IEffectSynthesisTask_V1 v1;
    }
}
