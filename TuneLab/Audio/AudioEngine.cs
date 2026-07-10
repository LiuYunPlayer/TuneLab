using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MeltySynth;
using TuneLab.Audio.SDL2;
using TuneLab.Foundation;
using TuneLab.Utils;

namespace TuneLab.Audio;

internal static class AudioEngine
{
    public static event Action? PlayStateChanged;
    public static event Action? ProgressChanged;
    public static bool IsPlaying => mAudioSampleProvider.IsPlaying;
    public static INotifiableProperty<int> SampleRate { get; } = new NotifiableProperty<int>(44100);
    public static double CurrentTime => mAudioSampleProvider.CurrentTime;
    public static double MasterGain { get; set; } = 0;
    public static double EndTime => AudioGraph.EndTime;
    public static INotifiableProperty<int> BufferSize { get; } = new NotifiableProperty<int>(1024);
    public static INotifiableProperty<string> CurrentDriver { get; } = new NotifiableProperty<string>(string.Empty);
    public static INotifiableProperty<string> CurrentDevice { get; } = new NotifiableProperty<string>(string.Empty);
    public static IReadOnlyList<string> GetAllDrivers() => mAudioPlaybackHandler!.GetAllDrivers();
    public static IReadOnlyList<string> GetAllDevices() => mAudioPlaybackHandler!.GetAllDevices();

    public static void Init()
    {
        mAudioSampleProvider.SampleRate = SampleRate.Value;
        mAudioPlaybackHandler = new SDLPlaybackHandler() { SampleRate = SampleRate.Value, BufferSize = BufferSize.Value, ChannelCount = 2 };
        mAudioPlaybackHandler.Init(mAudioSampleProvider);
        SetDriverToPlaybackHandler();
        SetDeviceToPlaybackHandler();
        mAudioPlaybackHandler.ProgressChanged += () => { if (IsPlaying) ProgressChanged?.Invoke(); };
        mAudioPlaybackHandler.CurrentDeviceChanged += () => 
        {
            CurrentDevice.Value = mAudioPlaybackHandler.CurrentDevice;
            mAudioPlaybackHandler.Start();
        };
        mAudioPlaybackHandler.DevicesChanged += () =>
        {
            SetDeviceToPlaybackHandler();
        };

        InitDefaultSoundFont();

        ProgressChanged += OnProgressChanged;
        SampleRate.Modified.Subscribe(OnSampleRateModified);
        BufferSize.Modified.Subscribe(OnBufferSizeModified);
        CurrentDriver.Modified.Subscribe(OnCurrentDriverModified);
        CurrentDevice.Modified.Subscribe(OnCurrentDeviceModified);

        mAudioPlaybackHandler.Start();
    }

    public static void Destroy()
    {
        if (mAudioPlaybackHandler == null)
        {
            Log.Error("Engine is not inited!");
            return;
        }

        mAudioPlaybackHandler.Stop();

        ProgressChanged -= OnProgressChanged;
        SampleRate.Modified.Unsubscribe(OnSampleRateModified);
        CurrentDriver.Modified.Unsubscribe(OnCurrentDriverModified);
        CurrentDevice.Modified.Unsubscribe(OnCurrentDeviceModified);

        mAudioPlaybackHandler.Destroy();
        mAudioPlaybackHandler = null;
    }

    public static void Play()
    {
        mAudioSampleProvider.IsPlaying = true;
        PlayStateChanged?.Invoke();
    }

    public static void Pause()
    {
        mAudioSampleProvider.IsPlaying = false;
        PlayStateChanged?.Invoke();
    }

    public static void Seek(double time)
    {
        mAudioSampleProvider.Seek(time);
        ProgressChanged?.Invoke();
    }

    public static void AddTrack(IAudioTrack track)
    {
        AudioGraph.AddTrack(track);
    }

    public static void RemoveTrack(IAudioTrack track)
    {
        AudioGraph.RemoveTrack(track);
    }

    public static void ExportTrack(string filePath, IAudioTrack track, bool isStereo)
    {
        ExportTrack(filePath, track, isStereo, SampleRate.Value, AudioEncodeSettings.Wav(16));
    }

    public static void ExportTrack(string filePath, IAudioTrack track, bool isStereo, int outputSampleRate, AudioEncodeSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default, double startTime = 0, double? endTime = null)
    {
        double defaultEndTime = Math.Max(track.EndTime, 0) + 1;
        int startPosition = (Math.Max(startTime, 0) * SampleRate.Value).Floor();
        int endPosition = ((endTime ?? defaultEndTime) * SampleRate.Value).Ceil();
        endPosition = Math.Max(endPosition, startPosition);
        int length = endPosition - startPosition;
        int channelCount = isStereo ? 2 : 1;
        float[] buffer = new float[length * channelCount];

        // Process in chunks for progress reporting
        int chunkSize = BufferSize.Value;
        for (int chunkStart = startPosition; chunkStart < endPosition; chunkStart += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int chunkEnd = Math.Min(chunkStart + chunkSize, endPosition);
            AudioGraph.AddData(track, chunkStart, chunkEnd, isStereo, buffer, (chunkStart - startPosition) * channelCount);
            progress?.Report(length > 0 ? (double)(chunkEnd - startPosition) / length * 0.8 : 0.8);
        }

        progress?.Report(0.8);
        cancellationToken.ThrowIfCancellationRequested();

        if (outputSampleRate != SampleRate.Value)
            buffer = AudioUtils.Resample(buffer, channelCount, SampleRate.Value, outputSampleRate);

        progress?.Report(0.9);
        cancellationToken.ThrowIfCancellationRequested();

        AudioUtils.Encode(filePath, buffer, outputSampleRate, channelCount, settings);
        progress?.Report(1.0);
    }

    public static void ExportMaster(string filePath, bool isStereo)
    {
        ExportMaster(filePath, isStereo, SampleRate.Value, AudioEncodeSettings.Wav(16));
    }

    public static void ExportMaster(string filePath, bool isStereo, int outputSampleRate, AudioEncodeSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default, double startTime = 0, double? endTime = null)
    {
        int startPosition = (Math.Max(startTime, 0) * SampleRate.Value).Floor();
        int endPosition = ((endTime ?? AudioGraph.EndTime) * SampleRate.Value).Ceil();
        endPosition = Math.Max(endPosition, startPosition);
        int length = endPosition - startPosition;
        int channelCount = isStereo ? 2 : 1;
        float[] buffer = new float[length * channelCount];

        // Process in chunks for progress reporting
        int chunkSize = BufferSize.Value;
        for (int chunkStart = startPosition; chunkStart < endPosition; chunkStart += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int chunkEnd = Math.Min(chunkStart + chunkSize, endPosition);
            AudioGraph.MixData(chunkStart, chunkEnd, isStereo, buffer, (chunkStart - startPosition) * channelCount);
            progress?.Report(length > 0 ? (double)(chunkEnd - startPosition) / length * 0.8 : 0.8);
        }

        progress?.Report(0.8);
        cancellationToken.ThrowIfCancellationRequested();

        if (outputSampleRate != SampleRate.Value)
            buffer = AudioUtils.Resample(buffer, channelCount, SampleRate.Value, outputSampleRate);

        progress?.Report(0.9);
        cancellationToken.ThrowIfCancellationRequested();

        AudioUtils.Encode(filePath, buffer, outputSampleRate, channelCount, settings);
        progress?.Report(1.0);
    }

    public static void InvokeRealtimeAmplitude(IAudioTrack track,out Tuple<double,double>? amplitude)
    {
        amplitude = null;

        if (track.IsMute) return;
        bool hasSolo = AudioGraph.Tracks.Where(t => t.IsSolo).Any();
        if (hasSolo && !track.IsSolo) return;

        double Sample2Amplitude(float Sample)
        {
            return Math.Abs(Sample);
        }
        double Amplitude2Db(double amplitude)
        {
            double referenceAmplitude = 1.0;
            double amplitudeRatio = amplitude / referenceAmplitude;
            double db = 20 * Math.Log10(amplitudeRatio);
            return db;
        }

        float[] amp = [0, 0];
        {
            int sampleWindow = 64;
            float[] buffer = new float[sampleWindow * 2];
            int position = (CurrentTime * SampleRate.Value).Ceil();
            AudioGraph.AddData(track, position, position + sampleWindow, true, buffer, 0);
            for (int i = 0; i < sampleWindow * 2; i += 2) { amp[0] = (float)Math.Max(amp[0], Sample2Amplitude(buffer[i])); amp[1] = (float)Math.Max(amp[1], Sample2Amplitude(buffer[i + 1])); };
        }
        amplitude = new Tuple<double, double>(
                Amplitude2Db(amp[0]), //L
                Amplitude2Db(amp[1])  //R
                );
    }

    public static void LoadKeySamples(string path)
    {
        mKeySamplesPath = path;
        try
        {
            mKeySamples.Fill(null);

            // 路径指向 .sf2 文件：作为用户预览音源（经 MeltySynth），优先于内置。
            if (File.Exists(path) && Path.GetExtension(path).Equals(".sf2", StringComparison.OrdinalIgnoreCase))
            {
                SetUserSoundFont(path);
                return;
            }

            // 非 SF2：清除用户音源、回落到内置；目录则按旧机制逐键 WAV。
            SetUserSoundFont(null);
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.GetFiles(path))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, out var keyNumber))
                {
                    int keyIndex = keyNumber - MusicTheory.MIN_PITCH;
                    if ((uint)keyIndex < MusicTheory.PITCH_COUNT)
                    {
                        try
                        {
                            int sampleRate = SampleRate.Value;
                            var data = AudioUtils.Decode(file, ref sampleRate);
                            switch (data.Length)
                            {
                                case 1:
                                    mKeySamples[keyIndex] = new MonoAudioData(data[0]);
                                    break;
                                case 2:
                                    mKeySamples[keyIndex] = new StereoAudioData(data[0], data[1]);
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Failed to decode key sample: " + file + " " + ex);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load key samples: " + ex);
        }
    }

    public static void PlayKeySample(int keyNumber)
    {
        var keyIndex = keyNumber - MusicTheory.MIN_PITCH;
        if ((uint)keyIndex >= mKeySamples.Length)
            return;

        // 优先级：用户逐键 WAV > 预览音源（用户 SF2 或内置 SF2）渲染音。
        var keySample = mKeySamples[keyIndex] ?? GetPreviewKeySample(keyIndex);
        if (keySample == null)
            return;

        AudioPlayer.Play(keySample);
    }

    // 活动预览音源：用户 SF2 优先，否则内置 SF2。
    static SoundFont? ActiveSoundFont => mUserSoundFont ?? mDefaultSoundFont;

    // 加载内置默认预览音源（Upright Piano KW，CC0，随程序分发）。
    static void InitDefaultSoundFont()
    {
        try
        {
            var path = Path.Combine(PathManager.ResourcesFolder, "SoundFonts", DefaultSoundFontFileName);
            if (!File.Exists(path))
                Log.Warning("Default piano soundfont not found: " + path);
            else
                mDefaultSoundFont = new SoundFont(path);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load default piano soundfont: " + ex);
            mDefaultSoundFont = null;
        }
        RebuildPreviewSynthesizer();
    }

    // 用户自定义预览音源（按键采样路径指向的 .sf2 文件）；传 null 清除、回落到内置。
    static void SetUserSoundFont(string? path)
    {
        try
        {
            mUserSoundFont = path != null ? new SoundFont(path) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load user soundfont: " + path + " " + ex);
            mUserSoundFont = null;
        }
        RebuildPreviewSynthesizer();
    }

    // 按活动音源 + 当前采样率重建 Synthesizer，并清空按键渲染缓存（音源或采样率变更后旧渲染音已失效）。
    static void RebuildPreviewSynthesizer()
    {
        lock (mPreviewLock)
        {
            Array.Clear(mPreviewKeySamples);
            var font = ActiveSoundFont;
            mPreviewSynthesizer = font != null ? new Synthesizer(font, SampleRate.Value) : null;
        }
    }

    // 各键首次触发时离线渲染一小段并缓存（懒加载，避开启动期渲染 128 键的开销）。
    static IAudioData? GetPreviewKeySample(int keyIndex)
    {
        lock (mPreviewLock)
        {
            if (mPreviewKeySamples[keyIndex] is { } cached)
                return cached;
            if (mPreviewSynthesizer == null)
                return null;

            int key = keyIndex + MusicTheory.MIN_PITCH;
            int sampleRate = SampleRate.Value;
            int sustainCount = (int)(sampleRate * PreviewSustainSeconds);
            int releaseCount = (int)(sampleRate * PreviewReleaseSeconds);

            var left = new float[sustainCount + releaseCount];
            var right = new float[sustainCount + releaseCount];

            // 每键独立渲染：清残留 → NoteOn 渲染延音段 → NoteOff 渲染释音尾。
            mPreviewSynthesizer.Reset();
            mPreviewSynthesizer.NoteOn(0, key, PreviewVelocity);
            mPreviewSynthesizer.Render(left.AsSpan(0, sustainCount), right.AsSpan(0, sustainCount));
            mPreviewSynthesizer.NoteOff(0, key);
            mPreviewSynthesizer.Render(left.AsSpan(sustainCount, releaseCount), right.AsSpan(sustainCount, releaseCount));

            var data = new StereoAudioData(left, right);
            mPreviewKeySamples[keyIndex] = data;
            return data;
        }
    }

    static void OnProgressChanged()
    {
        if (CurrentTime > AudioGraph.EndTime)
            Pause();
    }

    static void OnSampleRateModified()
    {
        mAudioSampleProvider.SampleRate = SampleRate.Value;
        if (mAudioPlaybackHandler != null)
            mAudioPlaybackHandler.SampleRate = SampleRate.Value;
        if (mKeySamplesPath != null)
            LoadKeySamples(mKeySamplesPath);
        RebuildPreviewSynthesizer();
    }

    static void OnBufferSizeModified()
    {
        if (mAudioPlaybackHandler != null)
            mAudioPlaybackHandler.BufferSize = BufferSize.Value;
    }

    static void OnCurrentDriverModified()
    {
        SetDriverToPlaybackHandler(); 
        SetDeviceToPlaybackHandler();
        mAudioPlaybackHandler?.Start();
    }

    static void OnCurrentDeviceModified()
    {
        SetDeviceToPlaybackHandler();
        mAudioPlaybackHandler?.Start();
    }

    static void SetDriverToPlaybackHandler()
    {
        // 关闭时 handler 可能已被释放，而设备/驱动变更的队列回调仍可能触发 —— 加守卫避免 shutdown 竞态 NRE。
        if (mAudioPlaybackHandler == null)
            return;

        var drivers = mAudioPlaybackHandler.GetAllDrivers();
        if (drivers.IsEmpty())
            return;

        var driver = drivers.Contains(CurrentDriver.Value) ? CurrentDriver.Value : drivers[0];
        if (mAudioPlaybackHandler.CurrentDriver != driver)
            mAudioPlaybackHandler.CurrentDriver = driver;

        CurrentDriver.Value = mAudioPlaybackHandler.CurrentDriver;
    }

    static void SetDeviceToPlaybackHandler()
    {
        if (mAudioPlaybackHandler == null)
            return;

        var devices = mAudioPlaybackHandler.GetAllDevices();
        if (devices.IsEmpty())
            return;

        var device = devices.Contains(CurrentDevice.Value) ? CurrentDevice.Value : devices[0];
        if (mAudioPlaybackHandler.CurrentDevice != device)
            mAudioPlaybackHandler.CurrentDevice = device;

        CurrentDevice.Value = mAudioPlaybackHandler.CurrentDevice;
    }

    class AudioSampleProvider() : IAudioSampleProvider
    {
        public AudioGraph AudioGraph => mAudioGraph;
        public AudioPlayer AudioPlayer => mAudioPlayer;
        public int SampleRate { get => mAudioGraph.SampleRate; set => mAudioGraph.SampleRate = value; }
        public bool IsPlaying {  get; set; }
        public double CurrentTime => (double)mGraphPosition / SampleRate;

        public void Read(float[] buffer, int offset, int count)
        {
            if (IsPlaying)
            {
                lock (mSeekLockObject)
                {
                    int position = mGraphPosition;
                    int endPosition = position + count;
                    try
                    {
                        mAudioGraph.MixData(position, endPosition, true, buffer, offset);
                    }
                    catch { }
                    mGraphPosition = endPosition;
                }
            }
            mAudioPlayer.AddData(count, buffer, offset);
            if (MasterGain == 0)
                return;

            float masterVolume = (float)MusicTheory.dB2Level(MasterGain);
            int startIndex = offset;
            int endIndex = offset + count * 2;
            for (int i = startIndex; i < endIndex; i++)
            {
                buffer[i] *= masterVolume;
            }
        }

        public void Seek(double time)
        {
            lock (mSeekLockObject)
            {
                mGraphPosition = (int)(time * SampleRate);
            }
        }

        readonly AudioPlayer mAudioPlayer = new();
        readonly AudioGraph mAudioGraph = new();
        int mGraphPosition = 0;
        readonly object mSeekLockObject = new();
    }

    static IAudioPlaybackHandler? mAudioPlaybackHandler;
    static AudioSampleProvider mAudioSampleProvider = new();
    static AudioGraph AudioGraph => mAudioSampleProvider.AudioGraph;
    static AudioPlayer AudioPlayer => mAudioSampleProvider.AudioPlayer;

    static readonly IAudioData?[] mKeySamples = new IAudioData?[MusicTheory.PITCH_COUNT];
    static string? mKeySamplesPath = null;

    // 预览音源（经 MeltySynth 渲染）：用户 SF2 优先，否则内置 SF2（Resources/SoundFonts 下随程序分发，CC0 免任何义务）。
    const string DefaultSoundFontFileName = "UprightPianoKW.sf2";
    const int PreviewVelocity = 100;
    const double PreviewSustainSeconds = 0.5;
    const double PreviewReleaseSeconds = 0.8;
    static SoundFont? mDefaultSoundFont = null;
    static SoundFont? mUserSoundFont = null;
    static Synthesizer? mPreviewSynthesizer = null;   // 由 ActiveSoundFont 构建
    static readonly IAudioData?[] mPreviewKeySamples = new IAudioData?[MusicTheory.PITCH_COUNT];
    static readonly object mPreviewLock = new();
}
