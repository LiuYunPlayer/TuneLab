using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal class AudioPart : Part, IAudioPart
{
    public INotifiableProperty<AudioPartStatus> Status { get; } = new NotifiableProperty<AudioPartStatus>(AudioPartStatus.Unlinked);
    public IActionEvent AudioChanged => mAudioChanged;
    public INotifiableProperty<string> BaseDirectory { get; } = new NotifiableProperty<string>(string.Empty);
    public override DataString Name { get; }
    public override DataStruct<double> Pos { get; }
    public override DataStruct<double> Dur { get; }
    public DataString Path { get; }
    IDataProperty<string> IAudioPart.Path => Path;

    public AudioPart(ITrack track, AudioPartInfo info) : base(track)
    {
        Name = new(this, string.Empty);
        Pos = new(this);
        Dur = new(this);
        Path = new(this, string.Empty);
        Dur.Modified.Subscribe(mDurationChanged);
        Path.Modified.Subscribe(Reload);
        BaseDirectory.Modified.Subscribe(() =>
        {
            if (Path.Value.StartsWith(".."))
                Reload();
        });
        IDataObject<AudioPartInfo>.SetInfo(this, info);
    }

    public override AudioPartInfo GetInfo()
    {
        var path = Path.Value;
        if (!string.IsNullOrEmpty(BaseDirectory.Value))
        {
            if (path.StartsWith(BaseDirectory.Value))
            {
                path = ".." + path[BaseDirectory.Value.Length..];
            }
        }

        return new()
        {
            Name = Name,
            Pos = Pos,
            Dur = Dur,
            Path = path
        };
    }

    void IDataObject<AudioPartInfo>.SetInfo(AudioPartInfo info)
    {
        IDataObject<AudioPartInfo>.SetInfo(Name, info.Name);
        IDataObject<AudioPartInfo>.SetInfo(Pos, info.Pos);
        IDataObject<AudioPartInfo>.SetInfo(Dur, info.Dur);
        IDataObject<AudioPartInfo>.SetInfo(Path, info.Path);
    }

    public override IAudioData GetAudioData(int offset, int count)
    {
        if (mAudioData == null)
            return new EmptyAudioData();

        return mAudioData.GetAudioData(offset, count);
    }

    public override void OnSampleRateChanged()
    {
        Reload();
    }

    public Waveform GetWaveform(int channelIndex)
    {
        return mWaveforms[channelIndex];
    }

    protected override int SampleCount()
    {
        return mAudioData == null ? 0 : Math.Min(base.SampleCount(), mAudioData.Count);
    }

    public async void Reload()
    {
        if (mLoadCancelTokenSource != null)
        {
            mLoadCancelTokenSource.Cancel();
            mLoadCancelTokenSource = null;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        mAudioData = null;
        mWaveforms = [];
        mAudioChanged.Invoke();
        Status.Value = AudioPartStatus.Loading;
        IAudioData? audioData = null;
        Waveform[]? waveforms = null;
        mLoadCancelTokenSource = cancellationTokenSource;
        await Task.Run(() =>
        {
            try
            {
                string path = Path;
                if (path.StartsWith(".."))
                {
                    path = System.IO.Path.Combine(BaseDirectory.Value, path[3..]);
                }
                int samplingRate = AudioEngine.SampleRate.Value;
                var data = AudioUtils.Decode(path, ref samplingRate);
                switch (data.Length)
                {
                    case 1:
                        audioData = new MonoAudioData(data[0]);
                        waveforms = [new(data[0])];
                        break;
                    case 2:
                        audioData = new StereoAudioData(data[0], data[1]);
                        waveforms = [new(data[0]), new(data[1])];
                        break;
                }
            }
            catch (Exception ex)
            {
                audioData = null;
                waveforms = null;
                Log.Error("Failed to load audio: " + ex);
            }

            if (cancellationTokenSource.IsCancellationRequested)
            {
                audioData = null;
                waveforms = null;
            }
        }, cancellationTokenSource.Token);

        if (cancellationTokenSource.IsCancellationRequested)
            return;

        mLoadCancelTokenSource = null;
        if (audioData == null || waveforms == null)
        {
            Status.Value = AudioPartStatus.Unlinked;
            return;
        }

        mAudioData = audioData;
        mWaveforms = waveforms;
        mAudioChanged.Invoke();
        Status.Value = AudioPartStatus.Linked;
    }
    CancellationTokenSource? mLoadCancelTokenSource = null;

    protected override int SampleRate => AudioEngine.SampleRate.Value;
    public int ChannelCount => mWaveforms.Length;

    Waveform[] mWaveforms = [];
    IAudioData? mAudioData;

    readonly ActionEvent mAudioChanged = new();
}
