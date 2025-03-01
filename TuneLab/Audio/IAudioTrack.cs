using System.Collections.Generic;

namespace TuneLab.Audio;

internal interface IAudioTrack
{
    bool IsMute { get; }
    bool IsSolo { get; }
    double Volume { get; }
    double Pan { get; }
    double EndTime { get; }
    IEnumerable<IAudioSource> AudioSources { get; }
}
