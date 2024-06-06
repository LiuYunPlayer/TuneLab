using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioEngine
{
    event Action? PlayStateChanged;
    event Action? ProgressChanged;

    bool IsPlaying { get; }
    int SamplingRate { get; }
    double CurrentTime { get; }

    void Init(IAudioProcessor processor);
    void Destroy();

    void Play();
    void Pause();
    void Seek(double time);
}
