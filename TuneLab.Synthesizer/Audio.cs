using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Synthesizer;

public struct Audio
{
    public double StartTime;
    public int SampleRate;
    public float[] Samples;
}
