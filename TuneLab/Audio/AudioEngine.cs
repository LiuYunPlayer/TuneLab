using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TuneLab.Base.Science;
using TuneLab.Utils;

namespace TuneLab.Audio;

internal static class AudioEngine
{
    public static event Action? PlayStateChanged;
    public static event Action? ProgressChanged;
    public static bool IsPlaying => mAudioEngine!.IsPlaying;
    public static int SamplingRate => mAudioEngine!.SamplingRate;
    public static double CurrentTime => mAudioEngine!.CurrentTime;

    public static void Init(IAudioEngine audioEngine)
    {
        int samplingRate = audioEngine.SamplingRate;
        mAudioGraph = new AudioGraph(samplingRate);
        mAudioEngine = audioEngine;
        mAudioEngine.Init(mAudioProcessor);
        mAudioEngine.PlayStateChanged += () => { PlayStateChanged?.Invoke(); };
        mAudioEngine.ProgressChanged += () => { ProgressChanged?.Invoke(); };
    }

    public static void Destroy()
    {
        if (mAudioEngine == null)
            throw new Exception("Engine is not inited!");

        mAudioEngine.Destroy();
        mAudioEngine = null;
    }

    public static void Play()
    {
        mAudioEngine!.Play();
    }

    public static void Pause()
    {
        mAudioEngine!.Pause();
    }

    public static void Seek(double time)
    {
        mAudioEngine!.Seek(time);
    }

    public static void AddTrack(IAudioTrack track)
    {
        mAudioGraph.AddTrack(track);
    }

    public static void RemoveTrack(IAudioTrack track)
    {
        mAudioGraph.RemoveTrack(track);
    }

    public static void ExportTrack(string filePath, IAudioTrack track, bool isStereo)
    {
        double endTime = track.EndTime;
        endTime = Math.Max(endTime, 0);
        endTime += 1;
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        mAudioGraph.AddData(track, 0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SamplingRate, 16, isStereo ? 2 : 1);
    }

    public static void ExportMaster(string filePath, bool isStereo)
    {
        var endTime = mAudioGraph.EndTime;
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        mAudioGraph.MixData(0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SamplingRate, 16, isStereo ? 2 : 1);
    }

    public static void InvokeRealtimeAmplitude(IAudioTrack track,out Tuple<double,double>? amplitude)
    {
        amplitude = null;

        if (track.IsMute) return;
        bool hasSolo = mAudioGraph.Tracks.Where(t => t.IsSolo).Any();
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
            mAudioGraph.AddData(track, position, position + sampleWindow, true, buffer, 0);
            for (int i = 0; i < sampleWindow * 2; i = i + 2) { amp[0] = (float)Math.Max(amp[0], Sample2Amplitude(buffer[i])); amp[1] = (float)Math.Max(amp[1], Sample2Amplitude(buffer[i + 1])); };
        }
        amplitude = new Tuple<double, double>(
                Amplitude2Db(amp[0]), //L
                Amplitude2Db(amp[1])  //R
                );
    }

    static AudioEngine()
    {
        ProgressChanged += () =>
        {
            if (CurrentTime > mAudioGraph.EndTime)
                Pause();
        };
    }

    class AudioProcessor : IAudioProcessor
    {
        public void ProcessBlock(float[] buffer, int offset, int position, int count)
        {
            try
            {
                mAudioGraph.MixData(position, position + count, true, buffer, offset);
            }
            catch { }
        }
    }

    static IAudioEngine? mAudioEngine;
    static AudioGraph mAudioGraph;
    static AudioProcessor mAudioProcessor = new();
}
