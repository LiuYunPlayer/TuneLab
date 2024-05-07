using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
