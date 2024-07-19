using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public static unsafe void BytesToFloats<T>(float* dest, byte* src, int size, int channelCount)
    {
        int sizeOfT = Marshal.SizeOf<T>();
        int totalSamples = size / (sizeOfT * channelCount) * channelCount;

        if (typeof(T) == typeof(byte))
        {
            const float max = byte.MaxValue;
            for (var i = 0; i < totalSamples; i++)
            {
                var intPtr = src + i * sizeOfT;
                dest[i] = *intPtr / max;
            }

            return;
        }

        if (typeof(T) == typeof(int))
        {
            const float max = int.MaxValue;
            for (var i = 0; i < totalSamples; i++)
            {
                var intPtr = (int*)(src + i * sizeOfT);
                dest[i] = *intPtr / max;
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

    public static unsafe void FloatsToBytes<T>(byte* dest, float* src, int size, int channelCount)
    {
        int sizeOfT = Marshal.SizeOf<T>();
        int totalSamples = size / channelCount * channelCount;

        if (typeof(T) == typeof(byte))
        {
            const float max = byte.MaxValue;
            for (var i = 0; i < totalSamples; i++)
            {
                var byteValue = (byte)(src[i] * max);
                var bytePtr = dest + i * sizeOfT;
                *bytePtr = byteValue;
            }

            return;
        }

        if (typeof(T) == typeof(int))
        {
            const float max = int.MaxValue;
            for (var i = 0; i < totalSamples; i++)
            {
                var intValue = (int)(src[i] * max);
                var intPtr = (int*)(dest + i * sizeOfT);
                *intPtr = intValue;
            }

            return;
        }

        if (typeof(T) == typeof(short))
        {
            const float max = short.MaxValue;
            for (var i = 0; i < totalSamples; i++)
            {
                var shortValue = (short)(src[i] * max);
                var shortPtr = (short*)(dest + i * sizeOfT);
                *shortPtr = shortValue;
            }

            return;
        }

        if (typeof(T) == typeof(float))
        {
            for (var i = 0; i < totalSamples; i++)
            {
                var floatPtr = (float*)(dest + i * sizeOfT);
                *floatPtr = src[i];
            }

            return;
        }

        if (typeof(T) == typeof(double))
        {
            for (var i = 0; i < totalSamples; i++)
            {
                var doubleValue = (double)src[i];
                var doublePtr = (double*)(dest + i * sizeOfT);
                *doublePtr = doubleValue;
            }
        }
    }
}

internal class FFmpegCodec : IAudioCodec
{
    public IEnumerable<string> AllDecodableFormats { get; } =
        ["wav", "mp3", "aac", "aiff", "m4a", "flac", "wma", "ogg", "opus"];

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
        using (var stream = new FileDecoderStream(path))
        {
            return new AudioInfo()
            {
                duration = stream.Duration(),
            };
        }
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
        return new ResampledAudioStream(input, outputSamplingRate);
        // return new NAudioCodec.NAudioResamplerStream(input, outputSamplingRate);
    }

    private abstract class FIFOStream<T>
    {
        public FIFOStream()
        {
            _cachedBuffer = new List<T>();
            _cachedBufferPos = 0;
        }

        protected int Decode(T[] buf, int requiredSize)
        {
            // 采取边解码边写到输出缓冲区的方式。策略是先把 cache 全部写出，然后边解码边写，写到最后剩下的再存入 cache
            int bytesWritten = 0;
            if (!_cachedBuffer.IsEmpty())
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
                    _cachedBuffer.Clear();
                    _cachedBufferPos = 0;
                }
            }

            // 如果 cache 不够需要，那么继续读取
            while (bytesWritten < requiredSize)
            {
                var decodedBytes = DecodeOnce();
                if (decodedBytes == null)
                {
                    break;
                }

                // 本次没读到任何内容
                if (decodedBytes.IsEmpty())
                {
                    continue;
                }

                var bytesNeeded = requiredSize - bytesWritten;
                var bytesSupply = (int)decodedBytes.Length;
                var sizeToCache = bytesSupply - bytesNeeded;
                if (sizeToCache > 0)
                {
                    // 写到输出缓冲区
                    Buffer.BlockCopy(decodedBytes, 0, buf, bytesWritten, bytesNeeded);

                    // 剩下的存入 cache
                    _cachedBuffer.AddRange(Enumerable.Repeat<T>(default!, sizeToCache));
                    _cachedBufferPos = 0;
                    Buffer.BlockCopy(decodedBytes, bytesNeeded, _cachedBuffer.GetBuffer(), 0, sizeToCache);

                    bytesWritten = requiredSize;
                }
                else
                {
                    // 全部写到输出缓冲区
                    Buffer.BlockCopy(decodedBytes, 0, buf, bytesWritten, bytesSupply);
                    bytesWritten += bytesSupply;
                }
            }

            return bytesWritten;
        }

        protected abstract byte[]? DecodeOnce();

        // 内部缓冲区相关
        protected List<T> _cachedBuffer; // 缓冲区
        protected int _cachedBufferPos; // 缓冲区读取位置
    }

    private unsafe class FileDecoderStream : FIFOStream<byte>, IAudioStream
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
            fixed (float* dest = &buffer[offset])
            {
                fixed (byte* src = bytes)
                {
                    switch (_format)
                    {
                        case AVSampleFormat.AV_SAMPLE_FMT_U8:
                        case AVSampleFormat.AV_SAMPLE_FMT_U8P:
                        {
                            Utils.BytesToFloats<byte>(dest, src, bytesRead, channels);
                            break;
                        }
                        case AVSampleFormat.AV_SAMPLE_FMT_S16:
                        case AVSampleFormat.AV_SAMPLE_FMT_S16P:
                        {
                            Utils.BytesToFloats<short>(dest, src, bytesRead, channels);
                            break;
                        }
                        case AVSampleFormat.AV_SAMPLE_FMT_S32:
                        case AVSampleFormat.AV_SAMPLE_FMT_S32P:
                        {
                            Utils.BytesToFloats<int>(dest, src, bytesRead, channels);
                            break;
                        }
                        case AVSampleFormat.AV_SAMPLE_FMT_FLT:
                        case AVSampleFormat.AV_SAMPLE_FMT_FLTP:
                        {
                            Utils.BytesToFloats<float>(dest, src, bytesRead, channels);
                            break;
                        }
                        case AVSampleFormat.AV_SAMPLE_FMT_DBL:
                        case AVSampleFormat.AV_SAMPLE_FMT_DBLP:
                        {
                            Utils.BytesToFloats<double>(dest, src, bytesRead, channels);
                            break;
                        }
                        case AVSampleFormat.AV_SAMPLE_FMT_S64:
                        case AVSampleFormat.AV_SAMPLE_FMT_S64P:
                        {
                            Utils.BytesToFloats<long>(dest, src, bytesRead, channels);
                            break;
                        }
                    }
                }
            }
        }

        public double Duration()
        {
            var stream = _formatContext->streams[_audioIndex];
            return stream->duration * stream->time_base.num / (double)stream->time_base.den;
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

        // 打开音频
        private void OpenFile(string fileName)
        {
            _fileName = fileName;

            // 打开文件
            AVFormatContext* fmt_ctx = null;
            var ret = ffmpeg.avformat_open_input(&fmt_ctx, fileName, null, null);
            if (ret != 0)
            {
                throw new FileLoadException($"FFmpeg: Failed to load file {fileName}.", fileName);
            }

            _formatContext = fmt_ctx;

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

            _samples = (long)Math.Round((stream->duration * codec_ctx->sample_rate *
                    stream->time_base.num / (double)stream->time_base.den
                ));
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

        protected override byte[]? DecodeOnce()
        {
            var fmt_ctx = _formatContext;
            var codec_ctx = _codecContext;

            var pkt = _packet;
            var frame = _frame;

            var ret = ffmpeg.av_read_frame(fmt_ctx, pkt);

            // 判断是否结束
            if (ret == ffmpeg.AVERROR_EOF)
            {
                ffmpeg.av_packet_unref(pkt);
                return null;
            }

            // 忽略其他错误
            if (ret != 0)
            {
                Console.WriteLine($"FFmpeg: Error getting next frame with code {-ret:x}, ignored.");
                return Array.Empty<byte>();
            }

            // 跳过其他流
            if (pkt->stream_index != _audioIndex)
            {
                ffmpeg.av_packet_unref(pkt);
                return Array.Empty<byte>();
            }

            // 发送待解码包
            ret = ffmpeg.avcodec_send_packet(codec_ctx, pkt);
            ffmpeg.av_packet_unref(pkt);

            // 忽略错误
            if (ret < 0)
            {
                Console.WriteLine($"FFmpeg: Error submitting a packet for decoding with code {-ret:x}, ignored.");
                return Array.Empty<byte>();
            }

            // 读取当前包的所有内容
            var buffer = new List<byte>();
            while (ret >= 0)
            {
                // 接收解码数据
                ret = ffmpeg.avcodec_receive_frame(codec_ctx, frame);

                // 判断是否结束
                if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    break;
                }

                // 忽略当前错误
                if (ret < 0)
                {
                    ffmpeg.av_frame_unref(frame);
                    Console.WriteLine($"FFmpeg: Error decoding frame with code {-ret:x}, ignored.");
                    continue;
                }

                // 接收数据
                var sampleFormat = (AVSampleFormat)frame->format;
                var sampleCount = frame->nb_samples;
                var channelCount = frame->ch_layout.nb_channels;
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

                buffer.AddRange(nonPlainBuffer);

                ffmpeg.av_frame_unref(frame);
            }

            return buffer.ToArray();
        }
    }

    private unsafe class ResampledAudioStream : FIFOStream<byte>, IAudioStream
    {
        public int SamplingRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }

        public int InputSamplingRate { get; }

        public ResampledAudioStream(IAudioProvider input, int sampleRate)
        {
            SamplingRate = sampleRate;
            ChannelCount = input.ChannelCount;
            SamplesPerChannel = (int)((long)input.SamplesPerChannel * sampleRate / input.SamplingRate);
            InputSamplingRate = input.SamplingRate;

            _provider = input;
            _cachedBuffer = new List<byte>();
            _cachedBufferPos = 0;

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

            // 解码
            var bytesRequired = count * sizeof(float);
            var bytes = new byte[bytesRequired];
            var bytesRead = Decode(bytes, bytesRequired);
            if (bytesRead == 0)
            {
                return;
            }

            fixed (float* dest = &buffer[offset])
            {
                fixed (byte* src = bytes)
                {
                    Utils.BytesToFloats<float>(dest, src, bytesRead, channels);
                }
            }
        }

        public void Dispose()
        {
            if (_swr_ctx != null)
            {
                CloseResampler();
            }
        }

        private readonly IAudioProvider _provider;

        // FFmpeg 数据
        private SwrContext* _swr_ctx;
        private byte** _src_data;
        private byte** _dst_data;
        private int _src_linesize;
        private int _dst_linesize;
        private long _max_dst_nb_samples;
        private long _dst_nb_samples;

        private const int src_nb_samples = 1024;
        private const AVSampleFormat src_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        private const AVSampleFormat dst_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT;

        // https://github.com/FFmpeg/FFmpeg/blob/master/doc/examples/resample_audio.c
        private void OpenResampler()
        {
            // 初始化重采样器
            var swr = ffmpeg.swr_alloc();
            _swr_ctx = swr;

            // 初始化输入输出声道
            int ret;
            {
                AVChannelLayout chLayout;
                ffmpeg.av_channel_layout_default(&chLayout, ChannelCount);

                ret = ffmpeg.swr_alloc_set_opts2(&swr, &chLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT,
                    SamplingRate, &chLayout,
                    AVSampleFormat.AV_SAMPLE_FMT_FLT, InputSamplingRate, 0, null);

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

            var channels = ChannelCount;
            var src_data = _src_data;
            int src_linesize;

            /* allocate source and destination samples buffers */
            ret = ffmpeg.av_samples_alloc_array_and_samples(&src_data, &src_linesize, channels,
                src_nb_samples, src_sample_fmt, 0);
            if (ret < 0)
            {
                throw new DecoderFallbackException("FFmpeg: Could not allocate source samples.");
            }

            _src_data = src_data;
            _src_linesize = src_linesize;

            /* compute the number of converted samples: buffering is avoided
             * ensuring that the output buffer will contain at least all the
             * converted input samples */
            _max_dst_nb_samples = _dst_nb_samples =
                ffmpeg.av_rescale_rnd(src_nb_samples, SamplingRate, InputSamplingRate, AVRounding.AV_ROUND_UP);

            /* buffer is going to be directly written to a rawaudio file, no alignment */
            var dst_data = _dst_data;
            int dst_linesize;
            ret = ffmpeg.av_samples_alloc_array_and_samples(&dst_data, &dst_linesize, channels,
                (int)_dst_nb_samples, dst_sample_fmt, 0);
            if (ret < 0)
            {
                ffmpeg.av_freep(&src_data[0]);
                ffmpeg.av_freep(&src_data);
                throw new DecoderFallbackException("FFmpeg: Could not allocate destination samples.");
            }

            _dst_data = dst_data;
            _dst_linesize = dst_linesize;
        }

        private void CloseResampler()
        {
            var src_data = _src_data;
            if (src_data != null)
                ffmpeg.av_freep(&src_data[0]);
            ffmpeg.av_freep(&src_data);
            _src_data = null;

            var dst_data = _dst_data;
            if (dst_data != null)
                ffmpeg.av_freep(&dst_data[0]);
            ffmpeg.av_freep(&dst_data);
            _dst_data = null;

            var swr_ctx = _swr_ctx;
            if (swr_ctx != null)
            {
                ffmpeg.swr_close(swr_ctx);
                ffmpeg.swr_free(&swr_ctx);
            }

            _swr_ctx = null;
        }

        protected override byte[]? DecodeOnce()
        {
            var channels = ChannelCount;
            var swr_ctx = _swr_ctx;
            var dst_linesize = _dst_linesize;
            int ret;

            /* generate synthetic audio */
            var srcBuffer = new float[src_nb_samples * channels];
            _provider.Read(srcBuffer, 0, srcBuffer.Length);
            fixed (float* srcBufferPtr = srcBuffer)
            {
                var srcByteSize = srcBuffer.Length * sizeof(float);
                Buffer.MemoryCopy(srcBufferPtr, _src_data[0], srcByteSize, srcByteSize);
            }

            /* compute destination number of samples */
            _dst_nb_samples = ffmpeg.av_rescale_rnd(ffmpeg.swr_get_delay(swr_ctx, InputSamplingRate) +
                                                    src_nb_samples, SamplingRate, InputSamplingRate,
                AVRounding.AV_ROUND_UP);
            if (_dst_nb_samples > _max_dst_nb_samples)
            {
                ffmpeg.av_freep(&_dst_data[0]);
                ret = ffmpeg.av_samples_alloc(_dst_data, &dst_linesize, channels,
                    (int)_dst_nb_samples, dst_sample_fmt, 1);

                if (ret < 0)
                {
                    return null;
                }

                _max_dst_nb_samples = _dst_nb_samples;
            }

            /* convert to destination format */
            ret = ffmpeg.swr_convert(swr_ctx, _dst_data, (int)_dst_nb_samples, _src_data, src_nb_samples);
            if (ret < 0)
            {
                throw new DecoderFallbackException("FFmpeg: Error while converting.");
            }

            var dst_bufsize = ffmpeg.av_samples_get_buffer_size(&dst_linesize, channels,
                ret, dst_sample_fmt, 1);

            var buffer = new byte[dst_bufsize];
            fixed (byte* bufferPtr = buffer)
            {
                Buffer.MemoryCopy(_dst_data[0], bufferPtr, dst_bufsize, dst_bufsize);
            }

            _dst_linesize = dst_linesize;
            return buffer;
        }
    }
}