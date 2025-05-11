using TuneLab.Audio;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;

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
