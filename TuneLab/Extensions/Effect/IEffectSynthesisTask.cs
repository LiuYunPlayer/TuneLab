using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Effect;
using TuneLab.Extensions.Synthesizer;

namespace TuneLab.Extensions.Effect;

internal interface IEffectSynthesisTask
{
    event Action<double>? Progress;
    event Action<SynthesisException?>? Finished;

    void Start();
    void Stop();
    void OnDirtyEvent(EffectDirtyEvent dirtyEvent);
}
