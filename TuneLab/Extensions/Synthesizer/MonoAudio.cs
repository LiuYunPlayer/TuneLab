using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.Extensions.Synthesizer;

internal struct MonoAudio
{
    public double StartTime;
    public int SampleRate;
    public float[] Samples;

    public readonly MonoAudio Clone()
    {
        MonoAudio clone = this;
        clone.Samples = (float[])Samples.Clone();
        return clone;
    }

    public static implicit operator MonoAudio_V1(MonoAudio v1)
    {
        return new MonoAudio_V1()
        { 
            StartTime = v1.StartTime,
            SampleRate = v1.SampleRate,
            Samples = v1.Samples,
        };
    }
}
