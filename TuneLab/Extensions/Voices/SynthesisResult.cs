using System;
using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voices;

public class SynthesisResult(
    double startTime,
    int samplingRate,
    float[] audioData,
    IReadOnlyList<IReadOnlyList<Point>>? synthesizedPitch = null,
    IReadOnlyDictionary<ISynthesisNote, SynthesizedPhoneme[]>? synthesizedPhoneme = null)
{
    public readonly double StartTime = startTime;
    public readonly int SamplingRate = samplingRate;
    public readonly float[] AudioData = audioData;
    public readonly IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch = synthesizedPitch ?? EmptyPitch;
    public readonly IReadOnlyDictionary<ISynthesisNote, SynthesizedPhoneme[]> SynthesizedPhonemes = synthesizedPhoneme ?? EmptyPhonemes;

    static readonly IReadOnlyList<IReadOnlyList<Point>> EmptyPitch = [];
    static readonly IReadOnlyDictionary<ISynthesisNote, SynthesizedPhoneme[]> EmptyPhonemes = new Dictionary<ISynthesisNote, SynthesizedPhoneme[]>();
}

public static class SynthesisResultExtension
{
    public static double AudioStartTime(this SynthesisResult result)
    {
        return result.StartTime;
    }

    public static double AudioDurationTime(this SynthesisResult result)
    {
        return (double)result.AudioData.Length / result.SamplingRate;
    }

    public static double AudioEndTime(this SynthesisResult result)
    {
        return result.AudioStartTime() + result.AudioDurationTime();
    }

    public static float[] Read(this SynthesisResult result, int offset, int count)
    {
        float[] data = new float[count];
        int start = Math.Max(offset, 0);
        int end = Math.Min(offset + count, result.AudioData.Length);
        for (int i = start; i < end; i++)
        {
            data[i - offset] = result.AudioData[i];
        }
        return data;
    }
}