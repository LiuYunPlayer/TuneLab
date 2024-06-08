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
    public static float[][] Decode(this IAudioCodec codec, string path, ref int samplingRate)
    {
        using var stream = codec.Decode(path);
        if (samplingRate == 0)
        {
            samplingRate = stream.SamplingRate;
        }
        IAudioStream resampled = stream;
        if (stream.SamplingRate != samplingRate)
        {
            resampled = codec.Resample(stream, samplingRate);
        }

        var result = resampled.ToSamples();

        if (resampled != stream)
            resampled.Dispose();

        return result;
    }
}