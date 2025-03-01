using System;
using System.Collections.Generic;

namespace TuneLab.Audio;

internal class AudioGraph()
{
    public int SampleRate
    {
        get => mSampleRate;
        set
        {
            mSampleRate = value;
            lock (mTrackLockObject)
            {
                foreach (var track in mTracks)
                {
                    foreach (var audioSource in track.AudioSources)
                    {
                        audioSource.OnSampleRateChanged();
                    }
                }
            }
        }
    }

    public IReadOnlyCollection<IAudioTrack> Tracks => mTracks;

    public void AddTrack(IAudioTrack track)
    {
        lock (mTrackLockObject)
        {
            mTracks.Add(track);
        }
    }

    public void RemoveTrack(IAudioTrack track)
    {
        lock (mTrackLockObject)
        {
            mTracks.Remove(track);
        }
    }

    public void AddData(IAudioTrack track, int position, int endPosition, bool isStereo, float[] buffer, int offset)
    {
        double volume = track.Volume;
        double pan = track.Pan;
        float leftVolume = (float)(volume * (1 - pan));
        float rightVolume = (float)(volume * (1 + pan));
        foreach (var audioSource in track.AudioSources)
        {
            int audioSourceStart = (int)(audioSource.StartTime * SampleRate);
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

    public void MixData(int position, int endPosition, bool isStereo, float[] buffer, int offset)
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

    public double EndTime
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

    int mSampleRate = 44100;
    List<IAudioTrack> mTracks = [];

    readonly object mTrackLockObject = new();
}
