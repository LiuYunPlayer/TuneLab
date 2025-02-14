using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioCodec : IAudioEncoder, IAudioDecoder, IAudioResampler
{

}

internal static class IAudioCodecExtension
{
    // 传入的采样率若为0，则返回时采样率值为音频本身的采样率；
    // 传入的采样率若为非0值，则返回时采样率为传入的采样率
    public static float[][] Decode(this IAudioCodec codec, string path, ref int sampleRate)
    {
        using var stream = codec.Decode(path);
        if (sampleRate == 0)
        {
            sampleRate = stream.SampleRate;
        }
        IAudioStream resampled = stream;
        if (stream.SampleRate != sampleRate)
        {
            resampled = codec.Resample(stream, sampleRate);
        }

        var result = resampled.ToChannelSamples();

        if (resampled != stream)
            resampled.Dispose();

        return result;
    }
}