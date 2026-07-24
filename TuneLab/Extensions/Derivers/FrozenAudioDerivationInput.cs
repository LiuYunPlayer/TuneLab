using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Derivers;

// IAudioDerivationInput 的宿主实现：一份冻结的多声道音频快照（copy-out 的自有缓冲）+ 冻结参数。
// worker 只读它、永不回碰宿主活数据。物化在数据线程做（Create），此后与源解耦——源被移动/编辑/删除都不影响本快照。
internal sealed class FrozenAudioDerivationInput : IAudioDerivationInput
{
    public int SampleRate { get; }
    public int ChannelCount => mChannels.Length;
    public long SampleCount => mChannels.Length > 0 ? mChannels[0].Length : 0;
    public PropertyObject Properties { get; }

    public void Read(int channel, long offset, Span<float> destination)
    {
        if ((uint)channel >= (uint)mChannels.Length)
            throw new ArgumentOutOfRangeException(nameof(channel));
        var src = mChannels[channel];
        if (offset < 0 || offset + destination.Length > src.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Read range out of bounds.");
        src.AsSpan((int)offset, destination.Length).CopyTo(destination);
    }

    FrozenAudioDerivationInput(int sampleRate, float[][] channels, PropertyObject properties)
    {
        SampleRate = sampleRate;
        mChannels = channels;
        Properties = properties;
    }

    // 数据线程物化：拷出音频 part 的【源解码内容】（整段文件、位置无关）到自有缓冲，顺带算内容 hash。
    // 喂源音频而非可见窗：deriver 输入是「固定音频、无时间线」（§1），裁剪/落点是 apply-side 的事、不进内容快照。
    // contentHash 覆盖 SampleRate + 声道数 + 全部源 PCM 字节——位置/tempo/裁剪无关，故移动 part 必命中缓存（§4.4）。
    // 仅数据线程调用（读活音频数据）。
    public static FrozenAudioDerivationInput Create(IAudioPart part, PropertyObject properties, out string contentHash)
    {
        int sampleRate = part.SampleRate;
        int channelCount = Math.Max(1, part.ChannelCount);
        int sampleCount = Math.Max(0, part.SourceSampleCount);

        var channels = new float[channelCount][];
        for (int c = 0; c < channelCount; c++)
        {
            channels[c] = new float[sampleCount];
            part.ReadSource(c, 0, channels[c]);   // 按文件样本域读，位置无关
        }

        contentHash = ComputeContentHash(sampleRate, channels);
        return new FrozenAudioDerivationInput(sampleRate, channels, properties);
    }

    static string ComputeContentHash(int sampleRate, float[][] channels)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[8];
        BitConverter.TryWriteBytes(header[..4], sampleRate);
        BitConverter.TryWriteBytes(header[4..], channels.Length);
        hash.AppendData(header);
        foreach (var channel in channels)
            hash.AppendData(MemoryMarshal.AsBytes(channel.AsSpan()));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    readonly float[][] mChannels;
}
