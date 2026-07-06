using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

internal class AudioPart : Part, IAudioPart
{
    public INotifiableProperty<AudioPartStatus> Status { get; } = new NotifiableProperty<AudioPartStatus>(AudioPartStatus.Unlinked);
    public IActionEvent AudioChanged => mAudioChanged;
    public INotifiableProperty<string> BaseDirectory { get; } = new NotifiableProperty<string>(string.Empty);
    public override DataString Name { get; }
    public override DataStruct<double> Pos { get; }
    public override DataStruct<double> StartOffset { get; }
    public override DataStruct<double> EndOffset { get; }
    public DataString Path { get; }
    IDataProperty<string> IAudioPart.Path => Path;

    public AudioPart(ITrack track, AudioPartInfo info) : base(track)
    {
        Name = new(this, string.Empty);
        Pos = new(this);
        StartOffset = new(this);
        EndOffset = new(this);
        Path = new(this, string.Empty);
        StartOffset.Modified.Subscribe(mDurationChanged);
        EndOffset.Modified.Subscribe(mDurationChanged);
        Path.Modified.Subscribe(Reload);
        BaseDirectory.Modified.Subscribe(() =>
        { 
            if (Path.Value.StartsWith("..")) 
                Reload(); 
        });
        SetInfo(info);
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
            StartOffset = StartOffset,
            EndOffset = EndOffset,
            Path = path
        };
    }

    public void SetInfo(AudioPartInfo info)
    {
        using var _ = MergeNotify();
        Name.SetInfo(info.Name);
        Pos.SetInfo(info.Pos);
        StartOffset.SetInfo(info.StartOffset);
        EndOffset.SetInfo(info.EndOffset);
        Path.SetInfo(info.Path);
    }

    public override IAudioData GetAudioData(int offset, int count)
    {
        if (mAudioData == null)
            return new EmptyAudioData();

        // 音频样本 0 锚在锚点 Pos：可见起点相对锚点的样本偏移 = headSkip（前向裁剪跳过被裁的头部、揭示后段）。
        // headSkip<0（前向扩展越过锚点）与超出解码长度的部分由 AudioData 包装补静音。
        return mAudioData.GetAudioData(HeadSkipSamples() + offset, count);
    }

    // 可见起点相对锚点的样本数（>0 前向裁剪跳过的头部，<0 锚点前的静音区）。
    int HeadSkipSamples()
    {
        return (int)(((IAudioSource)this).SampleRate * (TempoManager.GetTime(StartPos) - TempoManager.GetTime(Pos.Value)));
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
        // 可见窗长（base）与"从 headSkip 到解码末尾的可用音频"取小：前向裁剪后头部可用音频相应减少。
        return mAudioData == null ? 0 : Math.Min(base.SampleCount(), Math.Max(0, mAudioData.Count - HeadSkipSamples()));
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
            // Relative path is stored relative to the project's directory (BaseDirectory).
            // During project loading the part is constructed (and Path set, triggering this
            // Reload) before BaseDirectory is assigned. Resolving now would combine against an
            // empty base and probe the wrong location, throwing FileNotFoundException. Defer:
            // assigning BaseDirectory re-triggers Reload for relative paths.
            if (string.IsNullOrEmpty(BaseDirectory.Value))
            {
                Status.Value = AudioPartStatus.Unlinked;
                return;
            }

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
