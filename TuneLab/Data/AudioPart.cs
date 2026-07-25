using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Extensions.Derivers;
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

        // 返回宿主内部子类 NativeAudioPartInfo：携带派生记录账本随 part 多态流转（仅 native 格式持久化、
        // 通用格式插件只见基类 AudioPartInfo 而无视之）。记录是普通【非撤销】集合，不入回退栈。
        var records = new Map<string, DerivationRecordInfo>();
        foreach (var kvp in mDerivationRecords)
            records.Add(kvp.Key, kvp.Value);

        return new NativeAudioPartInfo()
        {
            Name = Name,
            Pos = Pos,
            StartOffset = StartOffset,
            EndOffset = EndOffset,
            Path = path,
            DerivationRecords = records,
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

        // 派生记录账本经 native 子类流转；普通集合直接重置（非命令、不进回退栈）。通用格式产出的基类 info 无此字段 => 空。
        mDerivationRecords.Clear();
        if (info is NativeAudioPartInfo native)
            foreach (var kvp in native.DerivationRecords)
                mDerivationRecords.Add(kvp.Key, kvp.Value);
        mDerivationRecordsChanged.Invoke();
    }

    // ── 派生记录账本（普通【非撤销】集合，键 = 内容寻址缓存 key）──
    // 增删不进回退栈（派生是与音乐编辑正交的后台作业，见 undo 对称原则）；缓存共享，删记录只移除引用、不删缓存文件。
    public IReadOnlyMap<string, DerivationRecordInfo> DerivationRecords => mDerivationRecords;
    public IActionEvent DerivationRecordsChanged => mDerivationRecordsChanged;

    // 提交时按缓存 key 落记录（已存在同 key = 同一次派生的幂等重触发，覆盖刷新）。
    public void AddDerivationRecord(string cacheKey, DerivationRecordInfo info)
    {
        mDerivationRecords[cacheKey] = info;
        mDerivationRecordsChanged.Invoke();
    }

    // 删记录 = 只移除本引用（缓存内容寻址、跨 part/工程共享，绝不删缓存文件）。
    public void RemoveDerivationRecord(string cacheKey)
    {
        if (mDerivationRecords.Remove(cacheKey))
            mDerivationRecordsChanged.Invoke();
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

    // 位置无关：mAudioData 是解码后的整段文件内容（工程采样率、index 0 = 文件起点），不含 HeadSkip/裁剪/Pos。
    public int SourceSampleCount => mAudioData?.Count ?? 0;

    public void ReadSource(int channel, int offset, Span<float> destination)
    {
        var data = mAudioData;
        for (int i = 0; i < destination.Length; i++)
        {
            int idx = offset + i;
            if (data == null || idx < 0 || idx >= data.Count)
            {
                destination[i] = 0;
                continue;
            }
            // v1 音频源为 mono/stereo：声道 0 取左、其余取右。
            destination[i] = channel == 0 ? data.GetLeft(idx) : data.GetRight(idx);
        }
    }

    Waveform[] mWaveforms = [];
    IAudioData? mAudioData;

    readonly ActionEvent mAudioChanged = new();
    readonly Map<string, DerivationRecordInfo> mDerivationRecords = new();
    readonly ActionEvent mDerivationRecordsChanged = new();
}
