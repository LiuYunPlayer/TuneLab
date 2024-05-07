using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ExCSS;
using Microsoft.VisualBasic;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TuneLab.Base.Science;
using TuneLab.Utils;

namespace TuneLab.Audio;

internal static class AudioEngine
{
    public static event Action? PlayStateChanged;
    public static event Action? Progress;
    public static bool IsPlaying => mIsPlaying;
    public static int SamplingRate => 44100;
    public static double CurrentTime => (double)(mPlayer.GetPosition() / sizeof(float) / mPlayer.OutputWaveFormat.Channels + mStartPosition) / SamplingRate;

    public static void Init()
    {
        var context = SynchronizationContext.Current;
        if (context == null)
        {
            Log.Error("AudioEngine init failed!");
            return;
        }

        mPlayer.NumberOfBuffers = 20;
        mPlayer.DesiredLatency = 320;
        mPlayer.Init(mAudioGraph);

        System.Timers.Timer timer = new(16);
        timer.Elapsed += (s, e) => { context.Post(_ => { if (mIsPlaying) Progress?.Invoke(); }, null); };
        timer.Start();
    }

    public static void Destroy()
    {
        Pause();
    }

    public static void Play()
    {
        if (IsPlaying)
            return;

        mIsPlaying = true;
        PlayStateChanged?.Invoke();
        mPlayer.Play();
    }

    public static void Pause()
    {
        if (!IsPlaying)
            return;

        mIsPlaying = false;
        PlayStateChanged?.Invoke();
        mPlayer.Pause();
    }

    public static void Seek(double time)
    {
        mPlayer.Stop();
        mStartPosition = (int)(time * SamplingRate);
        mPosition = mStartPosition;

        if (IsPlaying)
        {
            mPlayer.Play();
        }

        Progress?.Invoke();
    }

    public static void AddTrack(IAudioTrack track)
    {
        mTracks.Add(track);
    }

    public static void RemoveTrack(IAudioTrack track)
    {
        mTracks.Remove(track);
    }

    public static void ExportTrack(string filePath, IAudioTrack track, bool isStereo)
    {
        double endTime = track.EndTime;
        endTime = Math.Max(endTime, 0);
        endTime += 1;
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        WaveFormat waveFormat = new WaveFormat(SamplingRate, 16, isStereo ? 2 : 1);
        using (WaveFileWriter writer = new WaveFileWriter(filePath, waveFormat))
        {
            AddData(track, 0, endPosition, isStereo, buffer, 0);
            var bytes = To16BitsBytes(buffer);
            writer.Write(bytes, 0, bytes.Length);
        }
    }

    public static void ExportMaster(string filePath, bool isStereo)
    {
        double endTime = 0;
        foreach (var track in mTracks)
        {
            endTime = Math.Max(endTime, track.EndTime);
        }
        endTime += 1;
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        WaveFormat waveFormat = new WaveFormat(SamplingRate, 16, isStereo ? 2 : 1);
        using (WaveFileWriter writer = new WaveFileWriter(filePath, waveFormat))
        {
            MixData(0, endPosition, isStereo, buffer, 0);
            var bytes = To16BitsBytes(buffer);
            writer.Write(bytes, 0, bytes.Length);
        }
    }

    static byte[] To16BitsBytes(float[] data)
    {
        byte[] results = new byte[data.Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            short shortValue = (short)(data[i] * 32768);
            byte[] shortBytes = BitConverter.GetBytes(shortValue);
            results[i * 2] = shortBytes[0];
            results[i * 2 + 1] = shortBytes[1];
        }
        return results;
    }

    static void AddData(IAudioTrack track, int position, int endPosition, bool isStereo, float[] buffer, int offset)
    {
        double volume = track.Volume;
        double pan = track.Pan;
        float leftVolume = (float)(volume * (1 - pan));
        float rightVolume = (float)(volume * (1 + pan));
        foreach (var audioSource in track.AudioSources)
        {
            int audioSourceStart = (int)(audioSource.StartTime * SamplingRate);
            int audioSourceEnd = audioSourceStart + audioSource.SampleCount;
            if (audioSourceEnd < position)
                continue;

            if (audioSourceStart > endPosition)
                break;

            int start = Math.Max(position, audioSourceStart);
            int end = Math.Min(endPosition, audioSourceEnd);
            if (start == end)
                continue;

            var audioData = audioSource.GetAudioData(start - audioSourceStart, end - start);
            if (isStereo)
            {
                for (int i = start; i < end; i++)
                {
                    buffer[2 * (i - position) + offset] += leftVolume * audioData.GetLeft(i - start);
                    buffer[2 * (i - position) + offset + 1] += rightVolume * audioData.GetRight(i - start);
                }
            }
            else
            {
                for (int i = start; i < end; i++)
                {
                    buffer[i - position + offset] += (leftVolume * audioData.GetLeft(i - start) + rightVolume * audioData.GetRight(i - start)) / 2;
                }
            }
        }
    }

    static void MixData(int position, int endPosition, bool isStereo, float[] buffer, int offset)
    {
        bool hasSolo = false;
        foreach (var track in mTracks)
        {
            if (track.IsSolo)
            {
                hasSolo = true;
                break;
            }
        }

        foreach (var track in mTracks)
        {
            if (!track.IsSolo && (track.IsMute || hasSolo))
                continue;

            AddData(track, position, endPosition, isStereo, buffer, offset);
        }
    }

    class AudioGraph : ISampleProvider
    {
        public WaveFormat WaveFormat => mWaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int position = mPosition;
            int length = count / 2;
            int endPosition = position + length;

            mPosition += length;

            for (int i = offset; i < offset + count; i++)
            {
                buffer[i] = 0;
            }

            try // TODO: 使用线程同步机制避免异常
            {
                MixData(position, endPosition, true, buffer, offset);
            }
            catch { }

            return endPosition;
        }
    }

    static bool mIsPlaying = false;
    static int mPosition = 0;
    static int mStartPosition = 0;
    static WaveOutEvent mPlayer = new();
    static AudioGraph mAudioGraph = new();
    static WaveFormat mWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SamplingRate, 2);
    static List<IAudioTrack> mTracks = new();
}
