using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Effect;

public interface IEffectSynthesisTask_V1
{
    event Action<double>? Progress;
    event Action<SynthesisException_V1?>? Finished;

    void Start();
    void Stop();
    void OnDirtyEvent(IEffectDirtyEvent_V1 dirtyEvent);
}
