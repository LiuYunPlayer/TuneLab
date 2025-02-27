using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Synthesizer;

public struct Audio_V1
{
    public double StartTime;
    public int SampleRate;
    public float[] Samples;

    public Audio_V1 Clone()
    {
        Audio_V1 clone = this;
        clone.Samples = (float[])Samples.Clone();
        return clone;
    }
}
