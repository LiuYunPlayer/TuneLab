using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.Utils;

namespace TuneLab.Audio;

internal static class AudioEngine
{
    public static event Action? PlayStateChanged;
    public static event Action? ProgressChanged;
    public static bool IsPlaying => mAudioProvider!.IsPlaying;
    public static int SamplingRate => mAudioProvider!.SamplingRate;
    public static double CurrentTime => mAudioProvider!.CurrentTime;
    public static double MasterGain { get; set; } = 0;

    public static void Init(IAudioPlaybackHandler playbackHandler)
    {
        mAudioProvider = new(44100);

        mAudioPlaybackHandler = playbackHandler;
        mAudioPlaybackHandler.Init(mAudioProvider);
        mAudioPlaybackHandler.ProgressChanged += () => { if (IsPlaying) ProgressChanged?.Invoke(); };

        ProgressChanged += OnProgressChanged;

        mAudioPlaybackHandler.Start();
    }

    public static void Destroy()
    {
        if (mAudioPlaybackHandler == null)
            throw new Exception("Engine is not inited!");

        mAudioPlaybackHandler.Stop();

        ProgressChanged -= OnProgressChanged;

        mAudioPlaybackHandler.Destroy();
        mAudioPlaybackHandler = null;
    }

    public static void Play()
    {
        mAudioProvider!.IsPlaying = true;
        PlayStateChanged?.Invoke();
    }

    public static void Pause()
    {
        mAudioProvider!.IsPlaying = false;
        PlayStateChanged?.Invoke();
    }

    public static void Seek(double time)
    {
        mAudioProvider?.Seek(time);
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
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        AudioGraph.AddData(track, 0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SamplingRate, 16, isStereo ? 2 : 1);
    }

    public static void ExportMaster(string filePath, bool isStereo)
    {
        var endTime = AudioGraph.EndTime;
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        AudioGraph.MixData(0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SamplingRate, 16, isStereo ? 2 : 1);
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
            int position = (CurrentTime * SamplingRate).Ceil();
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
                            int samplingRate = SamplingRate;
                            var data = AudioUtils.Decode(file, ref samplingRate);
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

    class AudioProvider(int samplingRate) : IAudioProvider
    {
        public int SamplingRate => mAudioGraph.SamplingRate;
        public int ChannelCount => 2;
        public int SamplesPerChannel => int.MaxValue;

        public AudioGraph AudioGraph => mAudioGraph;
        public AudioPlayer AudioPlayer => mAudioPlayer;

        public bool IsPlaying {  get; set; }
        public double CurrentTime => (double)mGraphPosition / SamplingRate;

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
                    if (MasterGain != 0)
                    {
                        float masterVolume = (float)MusicTheory.dB2Level(MasterGain);
                        int startIndex = offset;
                        int endIndex = offset + count * 2;
                        for (int i = startIndex; i < endIndex; i++)
                        {
                            buffer[i] *= masterVolume;
                        }
                    }
                    mGraphPosition = endPosition;
                }
            }
            mAudioPlayer.AddData(count, buffer, offset);
        }

        public void Seek(double time)
        {
            lock (mSeekLockObject)
            {
                mGraphPosition = (int)(time * SamplingRate);
            }
        }

        readonly AudioPlayer mAudioPlayer = new();
        readonly AudioGraph mAudioGraph = new(samplingRate);
        int mGraphPosition = 0;
        readonly object mSeekLockObject = new();
    }

    static IAudioPlaybackHandler? mAudioPlaybackHandler;
    static AudioProvider? mAudioProvider;
    static AudioGraph AudioGraph => mAudioProvider!.AudioGraph;
    static AudioPlayer AudioPlayer => mAudioProvider!.AudioPlayer;

    static readonly IAudioData?[] mKeySamples = new IAudioData?[MusicTheory.PITCH_COUNT];
}
