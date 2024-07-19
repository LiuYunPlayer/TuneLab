using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioPlaybackHandler
{
    event Action? PlayStateChanged;
    event Action? ProgressChanged;

    event Action? CurrentDeviceChanged;
    event Action? DevicesChanged;

    bool IsPlaying { get; }
    double CurrentTime { get; }

    string CurrentDriver { get; set; }
    string CurrentDevice { get; set; }

    void Init(IAudioProvider audioProvider);
    void Destroy();

    void Start();
    void Stop();

    IReadOnlyList<string> GetAllDevices();
    IReadOnlyList<string> GetAllDrivers();
}
