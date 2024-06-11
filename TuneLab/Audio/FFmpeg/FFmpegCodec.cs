using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using Buffer = System.Buffer;
using Marshal = System.Runtime.InteropServices.Marshal;

namespace TuneLab.Audio.FFmpeg;

internal class FFmpegCodec : IAudioCodec
{
    public IEnumerable<string> AllDecodableFormats { get; } = ["wav", "mp3", "aiff", "aac", "wma", "mp4"];

    public AudioInfo GetAudioInfo(string path)
    {
        return new AudioInfo();
    }

    public IAudioStream Decode(string path)
    {
        return new FileAudioStream(path);
    }

    public void EncodeToWav(string path, float[] buffer, int samplingRate, int bitPerSample, int channelCount)
    {
    }

    public IAudioStream Resample(IAudioProvider input, int outputSamplingRate)
    {
        return null;
    }

    private unsafe class FileAudioStream : IAudioStream
    {
        public int SamplingRate => _codecContext != null ? _codecContext->sample_rate : 0;
        public int ChannelCount => _codecContext != null ? _codecContext->ch_layout.nb_channels : 0;
        public int SamplesPerChannel => (int)_samples;

        public FileAudioStream(string fileName)
        {
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
            CloseFile();
        }

        public void Read(float[] buffer, int offset, int count)
        {
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

        // 内部缓冲区相关
        SimpleVector<byte> _cachedBuffer; // 缓冲区
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

            // 初始化缓冲区
            _cachedBuffer = new SimpleVector<byte>();
            _cachedBufferPos = 0;
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
                var cacheSize = Math.Min(_cachedBuffer.Size - _cachedBufferPos, requiredSize);
                if (cacheSize > 0)
                {
                    Buffer.BlockCopy(_cachedBuffer.Data, _cachedBufferPos, buf, 0, cacheSize);
                    _cachedBufferPos += cacheSize;
                    bytesWritten = cacheSize;
                }
            }

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
                    else if (ret < 0)
                    {
                        // 出错
                        ffmpeg.av_frame_unref(frame);

                        // 忽略
                        Console.WriteLine($"FFmpeg: Error decoding frame with code {-ret:x}, ignored.");
                        continue;
                    }

                    int size_need = requiredSize - bytesWritten;
                    int size_supply = ffmpeg.av_samples_get_buffer_size(null, frame->ch_layout.nb_channels,
                        frame->nb_samples, (AVSampleFormat)frame->format, 1);

                    var arr = frame->data[0];

                    var size_cached = size_supply - size_need;
                    if (size_cached > 0)
                    {
                        // 写到输出缓冲区
                        Marshal.Copy((IntPtr)arr, buf, bytesWritten, size_need);

                        // 剩下的存入 cache
                        _cachedBuffer.Resize(size_cached);
                        Marshal.Copy(IntPtr.Add((IntPtr)arr, size_need), _cachedBuffer.Data, 0, size_cached);
                        _cachedBufferPos = 0;

                        bytesWritten = requiredSize;
                    }
                    else
                    {
                        // 全部写到输出缓冲区
                        Marshal.Copy((IntPtr)arr, buf, bytesWritten, size_supply);
                        bytesWritten += size_supply;
                    }

                    ffmpeg.av_frame_unref(frame);
                }
            }

            return bytesWritten;
        }
    }

    protected unsafe class ResampledAudioStream : IAudioStream
    {
        public int SamplingRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }

        public ResampledAudioStream(IAudioStream input, int sampleRate)
        {
            SamplingRate = sampleRate;
            ChannelCount = input.ChannelCount;
            SamplesPerChannel = (int)((long)input.SamplesPerChannel * sampleRate / input.SamplingRate);

            _stream = input;
        }

        public void Read(float[] buffer, int offset, int count)
        {
        }

        public void Dispose()
        {
        }

        private IAudioStream _stream;
    }
}