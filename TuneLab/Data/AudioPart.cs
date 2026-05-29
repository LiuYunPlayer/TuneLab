using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;
using TuneLab.Extensions.Formats.DataInfo;

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
        // Cancel any in-progress load (Reload is only called on the main thread,
        // so access to mLoadCancelTokenSource is safe without locking)
        mLoadCancelTokenSource?.Cancel();

        var cts = new CancellationTokenSource();
        mLoadCancelTokenSource = cts;

        // Reset state immediately on main thread
        mAudioData = null;
        mWaveforms = [];
        mAudioChanged.Invoke();
        Status.Value = AudioPartStatus.Loading;

        // Capture values on main thread for thread safety
        // (Path and BaseDirectory are DataString/NotifiableProperty and should
        // not be accessed from background threads)
        string path = Path;
        if (path.StartsWith(".."))
        {
            path = System.IO.Path.Combine(BaseDirectory.Value, path[3..]);
        }
        int samplingRate = AudioEngine.SampleRate.Value;

        IAudioData? audioData = null;
        Waveform[]? waveforms = null;

        try
        {
            await Task.Run(() =>
            {
                // Early exit if already canceled before starting expensive work
                if (cts.IsCancellationRequested)
                    return;

                var data = AudioUtils.Decode(path, ref samplingRate);

                // Check again after expensive decode completes
                if (cts.IsCancellationRequested)
                    return;

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
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Task was canceled before it started (token was already signaled
            // when Task.Run tried to schedule the delegate). Safe to return.
            return;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load audio: " + ex);
        }

        // Back on main thread after await
        if (cts.IsCancellationRequested)
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
