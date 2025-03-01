using System;
using System.Collections.Generic;

namespace TuneLab.Audio;

internal interface IAudioPlaybackHandler
{
    public static readonly string AutoDeviceName = "(Auto)";

    event Action? PlayStateChanged;
    event Action? ProgressChanged;

    event Action? CurrentDeviceChanged;
    event Action? DevicesChanged;

    bool IsPlaying { get; }

    string CurrentDriver { get; set; }
    string CurrentDevice { get; set; }

    int BufferSize { get; set; }
    int SampleRate { get; set; }
    int ChannelCount { get; set; }

    void Init(IAudioSampleProvider audioSampleProvider);
    void Destroy();

    void Start();
    void Stop();

    IReadOnlyList<string> GetAllDevices();
    IReadOnlyList<string> GetAllDrivers();
}
