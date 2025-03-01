using System.Collections.Generic;

namespace TuneLab.Audio;

internal class AudioData(IReadOnlyList<float> data, int channelCount) : IAudioData
{
    public int Count => data.Count / channelCount;

    public float GetLeft(int index)
    {
        return data[index * channelCount];
    }

    public float GetRight(int index)
    {
        return data[index * channelCount + 1];
    }
}
