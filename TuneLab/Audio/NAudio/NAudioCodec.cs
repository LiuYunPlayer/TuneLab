using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio.NAudio;

internal class NAudioCodec : IAudioCodec
{
    public IEnumerable<string> AllDecodableFormats { get; } = ["wav", "mp3", "aiff", "aac", "wma", "mp4"];

    public float[][] Decode(string path, ref int samplingRate)
    {
        ISampleProvider sampleProvider;
        using var reader = new AudioFileReader(path);
        sampleProvider = reader;
        if (samplingRate == 0)
        {
            samplingRate = reader.WaveFormat.SampleRate;
        }
        if (reader.WaveFormat.SampleRate != samplingRate)
        {
            var resampler = new MediaFoundationResampler(reader, new WaveFormat(samplingRate, reader.WaveFormat.Channels));
            resampler.ResamplerQuality = 60;
            sampleProvider = resampler.ToSampleProvider();
        }

        float[] buffer = new float[reader.Length * samplingRate / reader.WaveFormat.SampleRate];
        var count = sampleProvider.Read(buffer, 0, buffer.Length);

        int channelCount = sampleProvider.WaveFormat.Channels;
        if (channelCount == 1)
        {
            return [buffer];
        }
        else if (channelCount == 2)
        {
            float[] left = new float[count];
            float[] right = new float[count];
            for (int i = 0; i < count; i++)
            {
                left[i] = buffer[i * 2];
                right[i] = buffer[i * 2 + 1];
            }
            return [left, right];
        }
        else
        {
            float[][] results = new float[channelCount][];
            for (int i = 0; i < channelCount; i++)
            {
                results[i] = new float[count];
            }
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                var data = results[channelIndex];
                for (int i = 0; i < count; i++)
                {
                    data[i] = buffer[i * channelCount + channelIndex];
                }
            }
            return results;
        }
    }

    public void EncodeToWav(string path, float[] buffer, int samplingRate, int bitPerSample, int channelCount)
    {
        WaveFormat waveFormat = new WaveFormat(samplingRate, 16, channelCount);
        using WaveFileWriter writer = new WaveFileWriter(path, waveFormat);
        var bytes = To16BitsBytes(buffer);
        writer.Write(bytes, 0, bytes.Length);
    }

    public AudioInfo GetAudioInfo(string path)
    {
        using var reader = new AudioFileReader(path);
        return new AudioInfo() { duration = reader.TotalTime.TotalSeconds };
    }

    static byte[] To16BitsBytes(float[] data)
    {
        byte[] results = new byte[data.Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            short shortValue = (short)(data[i] * 32768);
            byte[] shortBytes = BitConverter.GetBytes(shortValue);
            results[i * 2] = shortBytes[0];
            results[i * 2 + 1] = shortBytes[1];
        }
        return results;
    }
}
