using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.Utils;

namespace TuneLab.Audio;

internal static class AudioEngine
{
    public static event Action? PlayStateChanged;
    public static event Action? ProgressChanged;
    public static bool IsPlaying => mAudioSampleProvider!.IsPlaying;
    public static int SampleRate => mAudioSampleProvider!.SampleRate;
    public static double CurrentTime => mAudioSampleProvider!.CurrentTime;
    public static double MasterGain { get; set; } = 0;
    public static int BufferSize { get => mAudioPlaybackHandler!.BufferSize; set => mAudioPlaybackHandler!.BufferSize = value; }
    public static INotifiableProperty<string> CurrentDriver { get; } = new NotifiableProperty<string>(string.Empty);
    public static INotifiableProperty<string> CurrentDevice { get; } = new NotifiableProperty<string>(string.Empty);
    public static IReadOnlyList<string> GetAllDrivers() => mAudioPlaybackHandler!.GetAllDrivers();
    public static IReadOnlyList<string> GetAllDevices() => mAudioPlaybackHandler!.GetAllDevices();

    public static void Init(IAudioPlaybackHandler playbackHandler)
    {
        mAudioSampleProvider = new(playbackHandler.SampleRate);

        mAudioPlaybackHandler = playbackHandler;
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

        ProgressChanged += OnProgressChanged;
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
        CurrentDriver.Modified.Unsubscribe(OnCurrentDriverModified);
        CurrentDevice.Modified.Unsubscribe(OnCurrentDeviceModified);

        mAudioPlaybackHandler.Destroy();
        mAudioPlaybackHandler = null;
    }

    public static void Play()
    {
        mAudioSampleProvider!.IsPlaying = true;
        PlayStateChanged?.Invoke();
    }

    public static void Pause()
    {
        mAudioSampleProvider!.IsPlaying = false;
        PlayStateChanged?.Invoke();
    }

    public static void Seek(double time)
    {
        mAudioSampleProvider?.Seek(time);
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
        double endTime = track.EndTime;
        endTime = Math.Max(endTime, 0);
        endTime += 1;
        int endPosition = (endTime * SampleRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        AudioGraph.AddData(track, 0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SampleRate, 16, isStereo ? 2 : 1);
    }

    public static void ExportMaster(string filePath, bool isStereo)
    {
        var endTime = AudioGraph.EndTime;
        int endPosition = (endTime * SampleRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        AudioGraph.MixData(0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SampleRate, 16, isStereo ? 2 : 1);
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
            int position = (CurrentTime * SampleRate).Ceil();
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
        try
        {
            mKeySamples.Fill(null);
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
                            int sampleRate = SampleRate;
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

        var keySample = mKeySamples[keyIndex];
        if (keySample == null)
            return;

        AudioPlayer.Play(keySample);
    }

    static void OnProgressChanged()
    {
        if (CurrentTime > AudioGraph.EndTime)
            Pause();
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
        var drivers = mAudioPlaybackHandler!.GetAllDrivers();
        if (drivers.IsEmpty())
            return;

        var driver = drivers.Contains(CurrentDriver.Value) ? CurrentDriver.Value : drivers[0];
        if (mAudioPlaybackHandler.CurrentDriver != driver)
            mAudioPlaybackHandler.CurrentDriver = driver;

        CurrentDriver.Value = mAudioPlaybackHandler.CurrentDriver;
    }

    static void SetDeviceToPlaybackHandler()
    {
        var devices = mAudioPlaybackHandler!.GetAllDevices();
        if (devices.IsEmpty())
            return;

        var device = devices.Contains(CurrentDevice.Value) ? CurrentDevice.Value : devices[0];
        if (mAudioPlaybackHandler.CurrentDevice != device)
            mAudioPlaybackHandler.CurrentDevice = device;

        CurrentDevice.Value = mAudioPlaybackHandler.CurrentDevice;
    }

    class AudioSampleProvider(int sampleRate) : IAudioSampleProvider
    {
        public AudioGraph AudioGraph => mAudioGraph;
        public AudioPlayer AudioPlayer => mAudioPlayer;
        public int SampleRate => mAudioGraph.SampleRate;
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
        readonly AudioGraph mAudioGraph = new(sampleRate);
        int mGraphPosition = 0;
        readonly object mSeekLockObject = new();
    }

    static IAudioPlaybackHandler? mAudioPlaybackHandler;
    static AudioSampleProvider? mAudioSampleProvider;
    static AudioGraph AudioGraph => mAudioSampleProvider!.AudioGraph;
    static AudioPlayer AudioPlayer => mAudioSampleProvider!.AudioPlayer;

    static readonly IAudioData?[] mKeySamples = new IAudioData?[MusicTheory.PITCH_COUNT];
}
