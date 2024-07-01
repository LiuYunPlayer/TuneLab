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
        mAudioEngine = audioEngine;
        mAudioEngine.Init(mAudioGraph);
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
        lock (mTrackLockObject)
        {
            mTracks.Add(track);
        }
    }

    public static void RemoveTrack(IAudioTrack track)
    {
        lock (mTrackLockObject)
        {
            mTracks.Remove(track);
        }
    }

    public static void ExportTrack(string filePath, IAudioTrack track, bool isStereo)
    {
        double endTime = track.EndTime;
        endTime = Math.Max(endTime, 0);
        endTime += 1;
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        AddData(track, 0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SamplingRate, 16, isStereo ? 2 : 1);
    }

    public static void ExportMaster(string filePath, bool isStereo)
    {
        var endTime = EndTime;
        int endPosition = (endTime * SamplingRate).Ceil();
        float[] buffer = new float[isStereo ? endPosition * 2 : endPosition];
        MixData(0, endPosition, isStereo, buffer, 0);
        AudioUtils.EncodeToWav(filePath, buffer, SamplingRate, 16, isStereo ? 2 : 1);
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
        lock (mTrackLockObject)
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
    }

    static double EndTime
    {
        get
        {
            double endTime = 0;
            foreach (var track in mTracks)
            {
                endTime = Math.Max(endTime, track.EndTime);
            }
            endTime += 1;
            return endTime;
        }
    }

    static AudioEngine()
    {
        ProgressChanged += () =>
        {
            if (CurrentTime > EndTime)
                Pause();
        };
    }

    class AudioGraph : IAudioProcessor
    {
        public void ProcessBlock(float[] buffer, int offset, int position, int count)
        {
            try
            {
                MixData(position, position + count, true, buffer, offset);
            }
            catch { }
        }
    }

    static IAudioEngine? mAudioEngine;
    static List<IAudioTrack> mTracks = new();
    static object mTrackLockObject = new();
    static AudioGraph mAudioGraph = new();
}
