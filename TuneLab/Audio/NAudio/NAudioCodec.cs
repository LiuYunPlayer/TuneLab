using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using NAudio.Wave;
using NAudio.Flac;
using NLayer.NAudioSupport;
using TuneLab.Base.Science;

namespace TuneLab.Audio.NAudio;

internal class NAudioCodec : IAudioCodec
{
    public IEnumerable<string> AllDecodableFormats { get; } = ["wav", "mp3", "flac", "aiff", "aif", "aifc"];

    public void EncodeToWav(string path, float[] buffer, int samplingRate, int bitPerSample, int channelCount)
    {
        WaveFormat waveFormat = new WaveFormat(samplingRate, 16, channelCount);
        using WaveFileWriter writer = new WaveFileWriter(path, waveFormat);
        var bytes = To16BitsBytes(buffer);
        writer.Write(bytes, 0, bytes.Length);
    }

    public AudioInfo GetAudioInfo(string path)
    {
        using var reader = new NAudioFileReader(path);
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

    public IAudioStream Decode(string path)
    {
        return new NAudioFileReader(path);
    }

    public IAudioStream Resample(IAudioProvider input, int outputSamplingRate)
    {
        return new NAudioResamplerStream(input, outputSamplingRate);
    }

    class NAudioFileReader : IAudioStream
    {
        public int SamplingRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }
        public TimeSpan TotalTime { get; }

        public NAudioFileReader(string path)
        {
            mWaveStream = Create(path);
            mSampleProvider = mWaveStream.ToSampleProvider();
            SamplingRate = mWaveStream.WaveFormat.SampleRate;
            ChannelCount = mWaveStream.WaveFormat.Channels;
            TotalTime = mWaveStream.TotalTime;
            var count = TotalTime.TotalSeconds * SamplingRate;
            SamplesPerChannel = count.Round();
        }

        public void Dispose()
        {
            mWaveStream.Dispose();
        }

        public void Read(float[] buffer, int offset, int count)
        {
            mSampleProvider.Read(buffer, offset, count);
        }

        WaveStream Create(string path)
        {
            var ext = Path.GetExtension(path);
            byte[] buffer = new byte[4];
            string tag = "";
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                stream.Read(buffer, 0, 4);
                tag = Encoding.UTF8.GetString(buffer.AsSpan(0, 4));
            }
            if (tag == "RIFF")
            {
                return new WaveFileReader(path);
            }
            if (ext == ".mp3")
            {
                return new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf));
            }
            if (tag == "fLaC")
            {
                return new FlacReader(path);
            }
            if (ext == ".aiff" || ext == ".aif" || ext == ".aifc")
            {
                return new AiffFileReader(path);
            }

            return new AudioFileReader(path);
        }

        readonly WaveStream mWaveStream;
        readonly ISampleProvider mSampleProvider;
    }

    class NAudioResamplerStream : IAudioStream
    {
        public int SamplingRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }

        public NAudioResamplerStream(IAudioProvider input, int outputSamplingRate)
        {
            mMediaFoundationResampler = new(new NAudioWaveProvider(input), WaveFormat.CreateIeeeFloatWaveFormat(outputSamplingRate, input.ChannelCount));
            mMediaFoundationResampler.ResamplerQuality = 60;
            mSampleProvider = mMediaFoundationResampler.ToSampleProvider();
            SamplingRate = outputSamplingRate;
            ChannelCount = input.ChannelCount;
            SamplesPerChannel = (int)((long)input.SamplesPerChannel * outputSamplingRate / input.SamplingRate);
        }

        public void Dispose()
        {
            mMediaFoundationResampler.Dispose();
        }

        public void Read(float[] buffer, int offset, int count)
        {
            mSampleProvider.Read(buffer, offset, count);
        }

        readonly MediaFoundationResampler mMediaFoundationResampler;
        readonly ISampleProvider mSampleProvider; 
    }

    class NAudioWaveProvider(IAudioProvider provider) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(provider.SamplingRate, provider.ChannelCount);

        public int Read(byte[] buffer, int offset, int count)
        {
            int length = count / sizeof(float);
            float[] samples = new float[length];
            provider.Read(samples, 0, length);
            for (int i = 0; i < length; i++)
            {
                var bytes = BitConverter.GetBytes(samples[i]);
                buffer[offset++] = bytes[0];
                buffer[offset++] = bytes[1];
                buffer[offset++] = bytes[2];
                buffer[offset++] = bytes[3];
            }
            return count;
        }
    }
}
