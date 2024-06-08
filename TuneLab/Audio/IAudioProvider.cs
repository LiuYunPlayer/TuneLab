using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioProvider
{
    int SamplingRate { get; }
    int ChannelCount { get; }
    int SamplesPerChannel { get; }

    void Read(float[] buffer, int offset, int count);
}

internal static class IAudioProviderExtension
{
    public static float[][] ToSamples(this IAudioProvider provider)
    {
        int channelCount = provider.ChannelCount;
        int samplesPerChannel = provider.SamplesPerChannel;

        float[][] result = new float[channelCount][];
        for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
        {
            result[channelIndex] = new float[samplesPerChannel];
        }

        var buffer = channelCount == 1 ? result[0] : new float[samplesPerChannel * channelCount];
        provider.Read(buffer, 0, buffer.Length);
        if (channelCount > 1)
        {
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                var data = result[channelIndex];
                for (int i = 0; i < samplesPerChannel; i++)
                {
                    data[i] = buffer[i * channelCount + channelIndex];
                }
            }
        }
        return result;
    }
}
