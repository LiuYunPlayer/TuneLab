using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using NAudio.Wave;
using NAudio.Flac;
using NAudio.Lame;
using NAudio.Vorbis;
using NLayer.NAudioSupport;
using TuneLab.Foundation;
using NAudio.Dsp;
using OggVorbisEncoder;
using CUETools.Codecs;
using CUETools.Codecs.FLAKE;

namespace TuneLab.Audio.NAudio;

internal class NAudioCodec : IAudioCodec
{
    public IEnumerable<string> AllDecodableFormats { get; } = ["wav", "mp3", "flac", "ogg", "aiff", "aif", "aifc"];

    public void Encode(string path, float[] buffer, int sampleRate, int channelCount, AudioEncodeSettings settings)
    {
        switch (settings.Format)
        {
            case AudioExportFormat.Mp3:
                EncodeToMp3(path, buffer, sampleRate, channelCount, settings.Bitrate);
                break;
            case AudioExportFormat.Flac:
                EncodeToFlac(path, buffer, sampleRate, channelCount, settings.BitDepth);
                break;
            case AudioExportFormat.Ogg:
                EncodeToOgg(path, buffer, sampleRate, channelCount, OggQuality(settings.Bitrate));
                break;
            default:
                EncodeToWav(path, buffer, sampleRate, settings.BitDepth, channelCount);
                break;
        }
    }

    static void EncodeToWav(string path, float[] buffer, int sampleRate, int bitPerSample, int channelCount)
    {
        switch (bitPerSample)
        {
            case 32:
            {
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
                using var writer = new WaveFileWriter(path, waveFormat);
                var bytes = To32BitsBytes(buffer);
                writer.Write(bytes, 0, bytes.Length);
                break;
            }
            case 24:
            {
                var waveFormat = new WaveFormat(sampleRate, 24, channelCount);
                using var writer = new WaveFileWriter(path, waveFormat);
                var bytes = To24BitsBytes(buffer);
                writer.Write(bytes, 0, bytes.Length);
                break;
            }
            default: // 16-bit
            {
                var waveFormat = new WaveFormat(sampleRate, 16, channelCount);
                using var writer = new WaveFileWriter(path, waveFormat);
                var bytes = To16BitsBytes(buffer);
                writer.Write(bytes, 0, bytes.Length);
                break;
            }
        }
    }

    static void EncodeToMp3(string path, float[] buffer, int sampleRate, int channelCount, int bitrate)
    {
        var waveFormat = new WaveFormat(sampleRate, 16, channelCount);
        var bytes = To16BitsBytes(buffer);
        using var writer = new LameMP3FileWriter(path, waveFormat, bitrate);
        writer.Write(bytes, 0, bytes.Length);
    }

    // FLAC 存整数 PCM，仅 16/24 位。用纯托管 FLAKE 编码器（无原生依赖）。
    static void EncodeToFlac(string path, float[] buffer, int sampleRate, int channelCount, int bitDepth)
    {
        int bits = bitDepth == 24 ? 24 : 16;
        int samplesPerChannel = buffer.Length / channelCount;
        int max = (1 << (bits - 1)) - 1;
        int min = -(1 << (bits - 1));
        float scale = 1 << (bits - 1);

        var samples = new int[samplesPerChannel, channelCount];
        for (int i = 0; i < samplesPerChannel; i++)
        {
            for (int c = 0; c < channelCount; c++)
            {
                int v = (int)(buffer[i * channelCount + c] * scale);
                samples[i, c] = Math.Clamp(v, min, max);
            }
        }

        var pcm = new AudioPCMConfig(bits, channelCount, sampleRate);
        var writer = new FlakeWriter(path, pcm) { CompressionLevel = 8 };
        try
        {
            writer.Write(new AudioBuffer(pcm, samples, samplesPerChannel));
        }
        finally
        {
            writer.Close();
        }
    }

    // OGG Vorbis，纯托管编码器；quality 为 Vorbis VBR 基准质量(0..1)。
    static void EncodeToOgg(string path, float[] buffer, int sampleRate, int channelCount, float quality)
    {
        int samplesPerChannel = buffer.Length / channelCount;
        var samples = new float[channelCount][];
        for (int c = 0; c < channelCount; c++)
            samples[c] = new float[samplesPerChannel];
        for (int i = 0; i < samplesPerChannel; i++)
        {
            for (int c = 0; c < channelCount; c++)
                samples[c][i] = buffer[i * channelCount + c];
        }

        var info = VorbisInfo.InitVariableBitRate(channelCount, sampleRate, quality);
        using var outStream = File.Create(path);
        var oggStream = new OggStream(1);

        oggStream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        oggStream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(new Comments()));
        oggStream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        while (oggStream.PageOut(out OggPage headerPage, true))
            WriteOggPage(outStream, headerPage);

        var state = ProcessingState.Create(info);
        const int block = 4096;
        for (int offset = 0; offset < samplesPerChannel; offset += block)
        {
            int len = Math.Min(block, samplesPerChannel - offset);
            state.WriteData(samples, len, offset);
            DrainOggPackets(state, oggStream, outStream, false);
        }
        state.WriteEndOfStream();
        DrainOggPackets(state, oggStream, outStream, true);
    }

    static void DrainOggPackets(ProcessingState state, OggStream oggStream, Stream outStream, bool force)
    {
        while (state.PacketOut(out OggPacket packet))
        {
            oggStream.PacketIn(packet);
            while (oggStream.PageOut(out OggPage page, force))
                WriteOggPage(outStream, page);
        }
    }

    static void WriteOggPage(Stream outStream, OggPage page)
    {
        outStream.Write(page.Header, 0, page.Header.Length);
        outStream.Write(page.Body, 0, page.Body.Length);
    }

    // 有损格式 UI 暴露 kbps 目标；映射到 Vorbis VBR 基准质量。
    static float OggQuality(int bitrate) => bitrate switch
    {
        <= 128 => 0.4f,
        <= 192 => 0.6f,
        <= 256 => 0.8f,
        _ => 1.0f,
    };

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

    static byte[] To24BitsBytes(float[] data)
    {
        byte[] results = new byte[data.Length * 3];
        for (int i = 0; i < data.Length; i++)
        {
            int intValue = (int)(data[i] * 8388608); // 2^23
            intValue = Math.Clamp(intValue, -8388608, 8388607);
            results[i * 3] = (byte)(intValue & 0xFF);
            results[i * 3 + 1] = (byte)((intValue >> 8) & 0xFF);
            results[i * 3 + 2] = (byte)((intValue >> 16) & 0xFF);
        }
        return results;
    }

    static byte[] To32BitsBytes(float[] data)
    {
        byte[] results = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, results, 0, results.Length);
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
                tag = System.Text.Encoding.UTF8.GetString(buffer.AsSpan(0, 4));
            }
            if (tag == "RIFF")
            {
                var reader = new WaveFileReader(path);
                // WAVE_FORMAT_EXTENSIBLE（0xFFFE，常见于 24-bit / 32-float 录音导出）：ToSampleProvider 不认此编码、
                // 会抛 "Unsupported source encoding"。其样本本就是标准 PCM / IEEE float，仅外层封装不同——规范化到底层
                // 子格式即可正常解。把数据拷进内存流、关掉文件 reader（避免句柄泄漏 / 文件占用）再以标准格式包装。
                var standard = ToStandardFormatIfExtensible(reader.WaveFormat);
                if (standard != null)
                {
                    // 按 blockAlign 对齐分块拷贝（WaveFileReader.Read 要求整块，默认 CopyTo 的缓冲非其倍数会抛
                    // "Must read complete blocks"）；拷进内存后关掉文件 reader（避免句柄泄漏），以标准格式包装。
                    int blockAlign = Math.Max(1, reader.WaveFormat.BlockAlign);
                    var ms = new MemoryStream();
                    var copyBuffer = new byte[blockAlign * 16384];
                    int read;
                    while ((read = reader.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                        ms.Write(copyBuffer, 0, read);
                    reader.Dispose();
                    ms.Position = 0;
                    return new RawSourceWaveStream(ms, standard);
                }
                return reader;
            }
            if (ext == ".mp3")
            {
                return new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf));
            }
            if (tag == "fLaC")
            {
                return new FlacReader(path);
            }
            if (tag == "OggS" || ext == ".ogg")
            {
                return new VorbisWaveReader(path);
            }
            if (ext == ".aiff" || ext == ".aif" || ext == ".aifc")
            {
                return new AiffFileReader(path);
            }

            return new AudioFileReader(path);
        }

        // 若 WaveFormat 是 WAVE_FORMAT_EXTENSIBLE，返回其规范化的标准格式（PCM / IEEE float），否则 null。
        // NAudio 视具体路径把 extensible 解析成 WaveFormatExtensible 或 WaveFormatExtraData 两种运行时类型，都要认。
        static WaveFormat? ToStandardFormatIfExtensible(WaveFormat waveFormat)
        {
            if (waveFormat.Encoding != WaveFormatEncoding.Extensible)
                return null;
            if (waveFormat is WaveFormatExtensible extensible)
                return extensible.ToStandardWaveFormat();
            // WaveFormatExtraData：extension 布局 = validBits(2) + channelMask(4) + SubFormat GUID(16)；
            // GUID 前 2 字节（偏移 6）= 格式码（1 = PCM，3 = IEEE float）。缺省按 PCM。
            int subFormatCode = 1;
            if (waveFormat is WaveFormatExtraData extra && extra.ExtraData.Length >= 8)
                subFormatCode = extra.ExtraData[6] | (extra.ExtraData[7] << 8);
            return subFormatCode == 3
                ? WaveFormat.CreateIeeeFloatWaveFormat(waveFormat.SampleRate, waveFormat.Channels)
                : new WaveFormat(waveFormat.SampleRate, waveFormat.BitsPerSample, waveFormat.Channels);
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
