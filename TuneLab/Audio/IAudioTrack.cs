using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

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
