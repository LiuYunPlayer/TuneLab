using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

internal enum AudioPartStatus
{
    Linked,
    Loading,
    Unlinked,
}

internal interface IAudioPart : IPart, IDataObject<AudioPartInfo>
{
    INotifiableProperty<AudioPartStatus> Status { get; }
    IActionEvent AudioChanged { get; }
    INotifiableProperty<string> BaseDirectory { get; }
    IDataProperty<string> Path { get; }
    int ChannelCount { get; }
    Waveform GetWaveform(int channelIndex);
    void Reload();
}
