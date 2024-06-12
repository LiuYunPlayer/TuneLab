using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using NAudio.Wave;
using TuneLab.Audio.NAudio;
using TuneLab.Base.Utils;
using Buffer = System.Buffer;
using FFmpegNative = FFmpeg.AutoGen.Native;

namespace TuneLab.Audio.FFmpeg;

internal static class Utils
{
    public static T[] GetBuffer<T>(this List<T> list)
    {
        var fieldInfo = list.GetType().GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);
        return (T[])fieldInfo!.GetValue(list)!;
    }

    public static unsafe void BytesToFloats<T>(float[] dest, int destIndex, byte[] bytes, int bytesSize,
        int channelCount)
    {
        int sizeOfT = Marshal.SizeOf<T>();
        int totalSamples = bytesSize / (sizeOfT * channelCount) * channelCount;

        fixed (byte* src = bytes)
        {
            if (typeof(T) == typeof(byte))
            {
                const float max = byte.MaxValue;
                for (var i = 0; i < totalSamples; i++)
                {
                    var intPtr = src + i * sizeOfT;
                    dest[i + destIndex] = *intPtr / max;
                }

                return;
            }

            if (typeof(T) == typeof(int))
            {
                const float max = int.MaxValue;
                for (var i = 0; i < totalSamples; i++)
                {
                    var intPtr = (int*)(src + i * sizeOfT);
                    dest[i + destIndex] = *intPtr / max;
                }

                return;
            }

            if (typeof(T) == typeof(short))
            {
                const float max = short.MaxValue;
                for (var i = 0; i < totalSamples; i++)
                {
                    var shortPtr = (short*)(src + i * sizeOfT);
                    dest[i] = *shortPtr / max;
                }

                return;
            }

            if (typeof(T) == typeof(float))
            {
                for (var i = 0; i < totalSamples; i++)
                {
                    var floatPtr = (float*)(src + i * sizeOfT);
                    dest[i] = *floatPtr;
                }

                return;
            }

            if (typeof(T) == typeof(double))
            {
                for (var i = 0; i < totalSamples; i++)
                {
                    var doublePtr = (double*)(src + i * sizeOfT);
                    dest[i] = (float)*doublePtr;
                }
            }
        }
    }
}

internal class FFmpegCodec : IAudioCodec
{
    public IEnumerable<string> AllDecodableFormats { get; } = ["wav", "mp3", "aiff", "aac", "wma", "mp4"];

    public FFmpegCodec(string libraryDir)
    {
        var libs = new[] { "avcodec", "avutil", "avformat", "swresample" };
        ffmpeg.RootPath = libraryDir;
        foreach (var lib in libs)
        {
            var ver = ffmpeg.LibraryVersionMap[lib];
            var nativeLibraryName = FFmpegNative.LibraryLoader.GetNativeLibraryName(lib, ver);
            var fullName = Path.Combine(ffmpeg.RootPath, nativeLibraryName);
            if (!File.Exists(fullName))
            {
                throw new FileNotFoundException($"FFmpeg library {fullName} not found!");
            }
        }
    }

    public AudioInfo GetAudioInfo(string path)
    {
        return new AudioInfo();
    }

    public IAudioStream Decode(string path)
    {
        return new FileDecoderStream(path);
    }

    public void EncodeToWav(string path, float[] buffer, int samplingRate, int bitPerSample, int channelCount)
    {
        WaveFormat waveFormat = new WaveFormat(samplingRate, 16, channelCount);
        using WaveFileWriter writer = new WaveFileWriter(path, waveFormat);
        var bytes = NAudioCodec.To16BitsBytes(buffer);
        writer.Write(bytes, 0, bytes.Length);
    }

    public IAudioStream Resample(IAudioProvider input, int outputSamplingRate)
    {
        // return new ResampledAudioStream(input, outputSamplingRate);
        return new NAudioCodec.NAudioResamplerStream(input, outputSamplingRate);
    }

    private unsafe class FileDecoderStream : IAudioStream
    {
        public int SamplingRate => _codecContext != null ? _codecContext->sample_rate : 0;
        public int ChannelCount => _codecContext != null ? _codecContext->ch_layout.nb_channels : 0;
        public int SamplesPerChannel => (int)_samples;

        public FileDecoderStream(string fileName)
        {
            _cachedBuffer = new List<byte>();
            _cachedBufferPos = 0;
            try
            {
                OpenFile(fileName);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                CloseFile();
                throw;
            }
        }

        public void Dispose()
        {
            if (_fileName != string.Empty)
            {
                CloseFile();
            }
        }

        public void Read(float[] buffer, int offset, int count)
        {
            var channels = ChannelCount;
            var samplesPerChannel = count / channels;

            // 解码
            var bytesRequired = ffmpeg.av_samples_get_buffer_size(null, channels,
                samplesPerChannel, _format, 1);
            var bytes = new byte[bytesRequired];
            var bytesRead = Decode(bytes, bytesRequired);
            if (bytesRead == 0)
            {
                return;
            }

            // 将解码的字节流转为浮点
            switch (_format)
            {
                case AVSampleFormat.AV_SAMPLE_FMT_U8:
                case AVSampleFormat.AV_SAMPLE_FMT_U8P:
                {
                    Utils.BytesToFloats<byte>(buffer, offset, bytes, bytesRead, channels);
                    break;
                }
                case AVSampleFormat.AV_SAMPLE_FMT_S16:
                case AVSampleFormat.AV_SAMPLE_FMT_S16P:
                {
                    Utils.BytesToFloats<short>(buffer, offset, bytes, bytesRead, channels);
                    break;
                }
                case AVSampleFormat.AV_SAMPLE_FMT_S32:
                case AVSampleFormat.AV_SAMPLE_FMT_S32P:
                {
                    Utils.BytesToFloats<int>(buffer, offset, bytes, bytesRead, channels);
                    break;
                }
                case AVSampleFormat.AV_SAMPLE_FMT_FLT:
                case AVSampleFormat.AV_SAMPLE_FMT_FLTP:
                {
                    Utils.BytesToFloats<float>(buffer, offset, bytes, bytesRead, channels);
                    break;
                }
                case AVSampleFormat.AV_SAMPLE_FMT_DBL:
                case AVSampleFormat.AV_SAMPLE_FMT_DBLP:
                {
                    Utils.BytesToFloats<double>(buffer, offset, bytes, bytesRead, channels);
                    break;
                }
                case AVSampleFormat.AV_SAMPLE_FMT_S64:
                case AVSampleFormat.AV_SAMPLE_FMT_S64P:
                {
                    Utils.BytesToFloats<long>(buffer, offset, bytes, bytesRead, channels);
                    break;
                }
            }
        }

        // 文件信息
        private string _fileName;

        // FFmpeg 指针
        private AVFormatContext* _formatContext;
        private AVCodecContext* _codecContext;
        private AVPacket* _packet;
        private AVFrame* _frame;

        // 音频信息
        private int _audioIndex; // 音频流序号
        private long _samples; // 不包括声道
        private AVSampleFormat _format;

        // 内部缓冲区相关
        List<byte> _cachedBuffer; // 缓冲区
        int _cachedBufferPos; // 缓冲区读取位置

        // 打开音频
        private void OpenFile(string fileName)
        {
            _fileName = fileName;

            var fmt_ctx = ffmpeg.avformat_alloc_context();
            _formatContext = fmt_ctx;

            // 打开文件
            var ret = ffmpeg.avformat_open_input(&fmt_ctx, fileName, null, null);
            if (ret != 0)
            {
                throw new FileLoadException($"FFmpeg: Failed to load file {fileName}.", fileName);
            }

            // 查找流信息
            ret = ffmpeg.avformat_find_stream_info(fmt_ctx, null);
            if (ret < 0)
            {
                throw new DecoderFallbackException("FFmpeg: Failed to find streams.");
            }

            // 查找音频流
            var audio_idx = ffmpeg.av_find_best_stream(fmt_ctx, AVMediaType.AVMEDIA_TYPE_AUDIO,
                -1, -1, null, 0);
            if (audio_idx < 0)
            {
                throw new DecoderFallbackException("FFmpeg: Failed to find audio stream.");
            }

            _audioIndex = audio_idx;

            // 查找解码器
            var stream = fmt_ctx->streams[audio_idx];
            var codec_param = stream->codecpar;
            var codec = ffmpeg.avcodec_find_decoder(codec_param->codec_id);
            if (codec == null)
            {
                throw new DecoderFallbackException("FFmpeg: Failed to find decoder.");
            }

            // 分配解码器上下文
            var codec_ctx = ffmpeg.avcodec_alloc_context3(null);
            _codecContext = codec_ctx;

            // 传递解码器信息
            ret = ffmpeg.avcodec_parameters_to_context(codec_ctx, codec_param);
            if (ret < 0)
            {
                throw new DecoderFallbackException("FFmpeg: Failed to pass params to codec.");
            }

            // 打开解码器
            ret = ffmpeg.avcodec_open2(codec_ctx, codec, null);
            if (ret < 0)
            {
                throw new DecoderFallbackException("FFmpeg: Failed to open decoder.");
            }

            _samples = (long)(stream->duration * codec_ctx->sample_rate *
                    stream->time_base.num / (float)stream->time_base.den
                );
            _format = (AVSampleFormat)codec_param->format;
            
            // 初始化数据包和数据帧
            var pkt = ffmpeg.av_packet_alloc();
            var frame = ffmpeg.av_frame_alloc();

            // 等待进一步的解码
            _packet = pkt;
            _frame = frame;
        }

        private void CloseFile()
        {
            var fmt_ctx = _formatContext;
            var codec_ctx = _codecContext;

            var pkt = _packet;
            var frame = _frame;

            if (frame != null)
            {
                ffmpeg.av_frame_free(&frame);
            }

            if (pkt != null)
            {
                ffmpeg.av_packet_free(&pkt);
            }

            if (codec_ctx != null)
            {
                // ffmpeg.avcodec_close(codec_ctx);
                ffmpeg.avcodec_free_context(&codec_ctx);
            }

            if (fmt_ctx != null)
            {
                ffmpeg.avformat_close_input(&fmt_ctx);
            }

            _cachedBuffer.Clear();
            _cachedBufferPos = 0;

            _format = 0;
            _samples = 0;

            _audioIndex = 0;

            _frame = null;
            _packet = null;
            _codecContext = null;
            _formatContext = null;

            _fileName = string.Empty;
        }

        private int Decode(byte[] buf, int requiredSize)
        {
            var fmt_ctx = _formatContext;
            var codec_ctx = _codecContext;

            var pkt = _packet;
            var frame = _frame;

            // 采取边解码边写到输出缓冲区的方式。策略是先把 cache 全部写出，然后边解码边写，写到最后剩下的再存入 cache
            int bytesWritten = 0;
            {
                var cacheSize = Math.Min(_cachedBuffer.Count - _cachedBufferPos, requiredSize);
                if (cacheSize > 0)
                {
                    Buffer.BlockCopy(_cachedBuffer.GetBuffer(), _cachedBufferPos, buf, 0, cacheSize);
                    _cachedBufferPos += cacheSize;
                    bytesWritten = cacheSize;
                }

                // 如果 cache 用完了，那么清除 cache
                if (_cachedBufferPos == _cachedBuffer.Count)
                {
                    _cachedBuffer.Resize(0);
                }
            }

            // 如果 cache 不够需要，那么继续从音频中读取
            while (bytesWritten < requiredSize)
            {
                int ret = ffmpeg.av_read_frame(fmt_ctx, pkt);

                // 判断是否结束
                if (ret == ffmpeg.AVERROR_EOF)
                {
                    ffmpeg.av_packet_unref(pkt);
                    break;
                }

                if (ret != 0)
                {
                    // 忽略
                    Console.WriteLine($"FFmpeg: Error getting next frame with code {-ret:x}, ignored.");
                    continue;
                }

                // 跳过其他流
                if (pkt->stream_index != _audioIndex)
                {
                    ffmpeg.av_packet_unref(pkt);
                    continue;
                }

                // 发送待解码包
                ret = ffmpeg.avcodec_send_packet(codec_ctx, pkt);
                ffmpeg.av_packet_unref(pkt);
                if (ret < 0)
                {
                    // 忽略
                    Console.WriteLine($"FFmpeg: Error submitting a packet for decoding with code {-ret:x}, ignored.");
                    continue;
                }

                while (ret >= 0)
                {
                    // 接收解码数据
                    ret = ffmpeg.avcodec_receive_frame(codec_ctx, frame);
                    if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        // 结束
                        break;
                    }

                    if (ret < 0)
                    {
                        // 出错
                        ffmpeg.av_frame_unref(frame);

                        // 忽略
                        Console.WriteLine($"FFmpeg: Error decoding frame with code {-ret:x}, ignored.");
                        continue;
                    }

                    var sampleFormat = (AVSampleFormat)frame->format;
                    var sampleCount = frame->nb_samples;
                    var channelCount = frame->ch_layout.nb_channels;

                    var bytesNeeded = requiredSize - bytesWritten;
                    var bytesSupply = ffmpeg.av_samples_get_buffer_size(null, channelCount,
                        sampleCount, sampleFormat, 1);

                    var nonPlainBuffer = new byte[bytesSupply];
                    if (ffmpeg.av_sample_fmt_is_planar(sampleFormat) != 0)
                    {
                        // 平面格式
                        var bytesPerSample = ffmpeg.av_get_bytes_per_sample(sampleFormat);
                        for (var i = 0; i < sampleCount; ++i)
                        {
                            for (var j = 0; j < channelCount; ++j)
                            {
                                var src = frame->extended_data[j] + i * bytesPerSample;
                                var dstIdx = (i * channelCount + j) * bytesPerSample;
                                Marshal.Copy((IntPtr)src, nonPlainBuffer, dstIdx, bytesPerSample);
                            }
                        }
                    }
                    else
                    {
                        // 交织格式
                        Marshal.Copy((IntPtr)frame->data[0], nonPlainBuffer, 0, bytesSupply);
                    }

                    var sizeToCache = bytesSupply - bytesNeeded;
                    if (sizeToCache > 0)
                    {
                        // 写到输出缓冲区
                        Buffer.BlockCopy(nonPlainBuffer, 0, buf, bytesWritten, bytesNeeded);

                        // 剩下的存入 cache
                        _cachedBuffer.Resize(sizeToCache);
                        Buffer.BlockCopy(nonPlainBuffer, bytesNeeded, _cachedBuffer.GetBuffer(), 0, sizeToCache);
                        _cachedBufferPos = 0;

                        bytesWritten = requiredSize;
                    }
                    else
                    {
                        // 全部写到输出缓冲区
                        Buffer.BlockCopy(nonPlainBuffer, 0, buf, bytesWritten, bytesSupply);
                        bytesWritten += bytesSupply;
                    }

                    ffmpeg.av_frame_unref(frame);
                }
            }

            return bytesWritten;
        }
    }

    private unsafe class ResampledAudioStream : IAudioStream
    {
        public int SamplingRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }

        public ResampledAudioStream(IAudioProvider input, int sampleRate)
        {
            SamplingRate = sampleRate;
            ChannelCount = input.ChannelCount;
            SamplesPerChannel = (int)((long)input.SamplesPerChannel * sampleRate / input.SamplingRate);

            _provider = input;

            try
            {
                OpenResampler();
            }
            catch (Exception e)
            {
                CloseResampler();
                throw;
            }
        }

        public void Read(float[] buffer, int offset, int count)
        {
            var channels = ChannelCount;
            var dstSamplesPerChannel = count / channels;

            var srcSamplesPerChannel = ffmpeg.av_rescale_rnd(ffmpeg.swr_get_delay(_swrContext, _provider.SamplingRate) +
                                                             dstSamplesPerChannel, _provider.SamplingRate, SamplingRate,
                AVRounding.AV_ROUND_UP);
        }

        public void Dispose()
        {
            if (_swrContext != null)
            {
                CloseResampler();
            }
        }

        private readonly IAudioProvider _provider;

        // FFmpeg 数据
        private SwrContext* _swrContext;

        private void OpenResampler()
        {
            // 初始化重采样器
            var swr = ffmpeg.swr_alloc();
            _swrContext = swr;

            // 初始化输入输出声道
            int ret;
            {
                AVChannelLayout chLayout;
                ffmpeg.av_channel_layout_default(&chLayout, ChannelCount);

                ret = ffmpeg.swr_alloc_set_opts2(&swr, &chLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT,
                    SamplingRate, &chLayout,
                    AVSampleFormat.AV_SAMPLE_FMT_FLT, _provider.SamplingRate, 0, null);

                ffmpeg.av_channel_layout_uninit(&chLayout);
            }
            if (ret != 0)
            {
                throw new DecoderFallbackException("FFmpeg: Failed to create resampler.");
            }

            ret = ffmpeg.swr_init(swr);
            if (ret < 0)
            {
                throw new DecoderFallbackException("FFmpeg: Failed to init resampler.");
            }
        }

        private void CloseResampler()
        {
            var swr_ctx = _swrContext;
            if (swr_ctx != null)
            {
                ffmpeg.swr_close(swr_ctx);
                ffmpeg.swr_free(&swr_ctx);
            }
            _swrContext = null;
        }
    }
}