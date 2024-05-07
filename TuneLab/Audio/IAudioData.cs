using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioData
{
    int Count { get; }
    float GetLeft(int index);
    float GetRight(int index);
}

internal static class IAudioDataExtension
{
    public static IAudioData GetAudioData(this IAudioData audioData, int offset, int count)
    {
        return new AudioData(audioData, offset, count);
    }

    class AudioData(IAudioData audioData, int offset, int count) : IAudioData
    {
        public int Count => count;

        public float GetLeft(int index)
        {
            int i = index + offset;
            return i >= 0 && i < audioData.Count ? audioData.GetLeft(i) : 0;
        }

        public float GetRight(int index)
        {
            int i = index + offset;
            return i >= 0 && i < audioData.Count ? audioData.GetRight(i) : 0;
        }
    }
}