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
using NAudio.Dsp;

namespace TuneLab.Audio.NAudio;

internal class NAudioCodec : IAudioCodec
{
    public IEnumerable<string> AllDecodableFormats { get; } = ["wav", "mp3", "flac", "aiff", "aif", "aifc"];

    public void EncodeToWav(string path, float[] buffer, int sampleRate, int bitPerSample, int channelCount)
    {
        WaveFormat waveFormat = new WaveFormat(sampleRate, 16, channelCount);
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

    public IAudioStream Resample(IAudioProvider input, int outputSampleRate)
    {
        return new WdlResamplerStream(input, outputSampleRate);
    }

    class NAudioFileReader : IAudioStream
    {
        public int SampleRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }
        public TimeSpan TotalTime { get; }

        public NAudioFileReader(string path)
        {
            mWaveStream = Create(path);
            mSampleProvider = mWaveStream.ToSampleProvider();
            SampleRate = mWaveStream.WaveFormat.SampleRate;
            ChannelCount = mWaveStream.WaveFormat.Channels;
            TotalTime = mWaveStream.TotalTime;
            var count = TotalTime.TotalSeconds * SampleRate;
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

        static WaveStream Create(string path)
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
        public int SampleRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }

        public NAudioResamplerStream(IAudioProvider input, int outputSampleRate)
        {
            mMediaFoundationResampler = new(new NAudioWaveProvider(input), WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, input.ChannelCount));
            mMediaFoundationResampler.ResamplerQuality = 60;
            mSampleProvider = mMediaFoundationResampler.ToSampleProvider();
            SampleRate = outputSampleRate;
            ChannelCount = input.ChannelCount;
            SamplesPerChannel = (int)((long)input.SamplesPerChannel * outputSampleRate / input.SampleRate);
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

    class WdlResamplerStream : IAudioStream
    {
        public int SampleRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }

        public WdlResamplerStream(IAudioProvider input, int outputSampleRate)
        {
            mInput = input;

            SampleRate = outputSampleRate;
            ChannelCount = input.ChannelCount;
            SamplesPerChannel = ((double)input.SamplesPerChannel * outputSampleRate / input.SampleRate).Ceil();

            mWdlResampler = new WdlResampler();
            mWdlResampler.SetRates(input.SampleRate, outputSampleRate);
            prepareCount = mWdlResampler.ResamplePrepare(SamplesPerChannel, ChannelCount, out inBuffer, out inBufferOffset);
        }

        public void Read(float[] buffer, int offset, int count)
        {
            if (outBuffer == null)
            {
                mInput.Read(inBuffer, inBufferOffset, ChannelCount * Math.Min(prepareCount, mInput.SamplesPerChannel));
                outBuffer = new float[SamplesPerChannel * ChannelCount];
                mWdlResampler.ResampleOut(outBuffer, 0, prepareCount, SamplesPerChannel, ChannelCount);
            }

            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = outBuffer[alreadyReadCount + i];
            }

            alreadyReadCount += count;
        }

        public void Dispose()
        {
            
        }

        float[]? outBuffer = null;
        int alreadyReadCount = 0;

        readonly float[] inBuffer;
        readonly int inBufferOffset;
        readonly int prepareCount;
        readonly WdlResampler mWdlResampler;

        readonly IAudioProvider mInput;
    }

    class NAudioWaveProvider(IAudioProvider provider) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(provider.SampleRate, provider.ChannelCount);

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
