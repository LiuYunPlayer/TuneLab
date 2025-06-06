using System;
using TuneLab.Extensions.Synthesizer;

namespace TuneLab.Extensions.Voice;

public interface IVoiceSynthesisSegment
{
    event Action? ProgressUpdated;
    event Action<SynthesisError?>? Finished;

    double Progress { get; }
    string Status { get; }

    void StartSynthesis();
    void StopSynthesis();
    void OnDirtyEvent(VoiceDirtyEvent dirtyEvent);
}
