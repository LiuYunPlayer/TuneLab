using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

internal enum AudioPartStatus
{
    Linked,
    Loading,
    Unlinked,
}

internal interface IAudioPart : IPart, IDataObject<AudioPartInfo>
{
    INotifiableProperty<AudioPartStatus> Status { get; }
    IActionEvent AudioChanged { get; }
    INotifiableProperty<string> BaseDirectory { get; }
    IDataProperty<string> Path { get; }
    int ChannelCount { get; }
    Waveform GetWaveform(int channelIndex);
    void Reload();

    // —— 位置无关的解码内容读取（内容寻址派生 deriver 用）——
    // 与 IAudioSource.GetAudioData/SampleCount 不同：那两者按时间线几何（经 GetTime(绝对位置) 换算）给「可见窗」，
    // 带 Pos/tempo 依赖；这里按解码文件自身的样本域读，与 Pos、tempo、裁剪都无关——是音频 part 的稳定内容身份。
    // 裁剪/落点是 apply-side 的事，不进内容快照（见 docs/deriver-sdk-design.md §1、§4.4）。
    int SourceSampleCount { get; }
    // 从解码内容的 [offset, offset+destination.Length) 拷出某声道到调用方缓冲；越界补静音。offset 以文件样本 0 为原点。
    void ReadSource(int channel, int offset, Span<float> destination);
}
