using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public struct MonoAudio_V1
{
    public double StartTime;
    public int SampleRate;
    public float[] Samples;
}
