using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal class StereoAudioData : IAudioData
{
    public int Count => mLeft.Count;

    public StereoAudioData(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        mLeft = left;
        mRight = right;
    }

    public float GetLeft(int index)
    {
        return mLeft[index];
    }

    public float GetRight(int index)
    {
        return mRight[index];
    }

    public float GetData(int index)
    {
        return (mLeft[index] + mRight[index]) / 2;
    }

    IReadOnlyList<float> mLeft;
    IReadOnlyList<float> mRight;
}
