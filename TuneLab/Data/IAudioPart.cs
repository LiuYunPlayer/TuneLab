using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.SDK.Format.DataInfo;

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
