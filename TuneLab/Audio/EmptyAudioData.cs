using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal class EmptyAudioData : IAudioData
{
    public int Count => 0;

    public float GetLeft(int index)
    {
        return 0;
    }

    public float GetRight(int index)
    {
        return 0;
    }
}
