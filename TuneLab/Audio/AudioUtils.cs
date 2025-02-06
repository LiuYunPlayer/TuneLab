using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal static class AudioUtils
{
    public static IEnumerable<string> AllDecodableFormats => mAudioCodec!.AllDecodableFormats;

    public static void Init(IAudioCodec audioCodec)
    {
        mAudioCodec = audioCodec;
    }

    public static bool TryGetAudioInfo(string path, [NotNullWhen(true)] out AudioInfo audioInfo)
    {
        try
        {
            audioInfo = mAudioCodec!.GetAudioInfo(path);
            return true;
        }
        catch
        {
            audioInfo = new();
            return false; 
        }
    }

    public static float[][] Decode(string path, ref int sampleRate)
    {
        return mAudioCodec!.Decode(path, ref sampleRate);
    }

    public static void EncodeToWav(string path, float[] buffer, int sampleRate, int bitPerSample, int channelCount)
    {
        mAudioCodec!.EncodeToWav(path, buffer, sampleRate, bitPerSample, channelCount);
    }

    public static float[] Resample(float[] buffer, int channelCount, int inputSampleRate, int outputSampleRate)
    {
        return mAudioCodec!.Resample(new AudioProvider(buffer, inputSampleRate, channelCount), outputSampleRate).ToSamples();
    }

    class AudioProvider(float[] data, int sampleRate, int channelCount) : IAudioProvider
    {
        public int SampleRate => sampleRate;
        public int ChannelCount => channelCount;
        public int SamplesPerChannel => data.Length / channelCount;

        public void Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[i + offset] = data[i + alreadyReadCount];
            }

            alreadyReadCount += count;
        }

        int alreadyReadCount = 0;
    }

    static IAudioCodec? mAudioCodec;
}
