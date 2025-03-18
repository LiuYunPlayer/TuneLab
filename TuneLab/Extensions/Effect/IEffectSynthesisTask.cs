﻿using System;
using TuneLab.Extensions.Synthesizer;

namespace TuneLab.Extensions.Effect;

internal interface IEffectSynthesisTask
{
    event Action<double>? Progress;
    event Action<SynthesisError?>? Finished;

    void Start();
    void Stop();
    void OnDirtyEvent(EffectDirtyEvent dirtyEvent);
}
