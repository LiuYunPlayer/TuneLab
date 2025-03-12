using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
