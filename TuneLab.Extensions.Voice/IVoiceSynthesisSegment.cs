using System;
using TuneLab.Extensions.Synthesizer;

namespace TuneLab.Extensions.Voice;

public interface IVoiceSynthesisSegment
{
    event Action<double>? Progress;
    event Action<SynthesisError?>? Finished;

    void StartSynthesis();
    void StopSynthesis();
    void OnDirtyEvent(VoiceDirtyEvent dirtyEvent);
}
