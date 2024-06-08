using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal static class AudioUtils
{
    public static IEnumerable<string> AllDecodableFormats => mAudioCodec!.AllDecodableFormats;

    public static void Init(IAudioCodec audioCodec)
    {
        mAudioCodec = audioCodec;
    }

    public static bool TryGetAudioInfo(string path, [NotNullWhen(true)] out AudioInfo audioInfo)
    {
        try
        {
            audioInfo = mAudioCodec!.GetAudioInfo(path);
            return true;
        }
        catch
        {
            audioInfo = new();
            return false; 
        }
    }

    public static float[][] Decode(string path, ref int samplingRate)
    {
        return mAudioCodec!.Decode(path, ref samplingRate);
    }

    public static void EncodeToWav(string path, float[] buffer, int samplingRate, int bitPerSample, int channelCount)
    {
        mAudioCodec!.EncodeToWav(path, buffer, samplingRate, bitPerSample, channelCount);
    }

    static IAudioCodec? mAudioCodec;
}
