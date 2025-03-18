using System;
using TuneLab.Extensions.Synthesizer;

namespace TuneLab.Extensions.Voice;

internal interface IVoiceSynthesisSegment
{
    event Action<double>? Progress;
    event Action<SynthesisError?>? Finished;

    void StartSynthesis();
    void StopSynthesis();
    void OnDirtyEvent(VoiceDirtyEvent dirtyEvent);
}
