using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioSource
{
    double StartTime { get; }
    int SampleRate { get; }
    int SampleCount { get; }
    IAudioData GetAudioData(int offset, int count);
    void OnSampleRateChanged();
}

internal static class IAudioSourceExtension
{
    public static double StartTime(this IAudioSource source)
    {
        return source.StartTime;
    }

    public static double Duration(this IAudioSource source)
    {
        return source.SampleCount == 0 ? 0 : (double)source.SampleCount / source.SampleRate;
    }

    public static double EndTime(this IAudioSource source)
    {
        return source.StartTime() + source.Duration();
    }
}
