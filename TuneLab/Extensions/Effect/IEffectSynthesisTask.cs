using System;
using TuneLab.Extensions.Adapters.Synthesizer;
using TuneLab.Extensions.Synthesizer;
using TuneLab.SDK.Base;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Effect;

internal interface IEffectSynthesisTask
{
    event Action<double>? Progress;
    event Action<SynthesisError?>? Finished;

    void Start();
    void Stop();
    void OnDirtyEvent(EffectDirtyEvent dirtyEvent);
}
