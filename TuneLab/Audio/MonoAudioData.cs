using System.Collections.Generic;

namespace TuneLab.Audio;

internal class MonoAudioData : IAudioData
{
    public int Count => mData.Count;

    public MonoAudioData(IReadOnlyList<float> data)
    {
        mData = data;
    }

    public float GetLeft(int index)
    {
        return mData[index];
    }

    public float GetRight(int index)
    {
        return mData[index];
    }

    IReadOnlyList<float> mData;
}
