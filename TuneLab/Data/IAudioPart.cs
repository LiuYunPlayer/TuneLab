using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Extensions.Derivers;
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

    // —— 派生记录账本（普通【非撤销】集合，键 = 内容寻址缓存 key）——
    // 宿主内部概念，仅 native 格式经 NativeAudioPartInfo 持久化；增删不进回退栈（见 undo 对称原则）。
    // 删记录只移除本引用（缓存内容寻址、跨 part/工程共享），绝不删缓存文件。
    IReadOnlyMap<string, DerivationRecordInfo> DerivationRecords { get; }
    IActionEvent DerivationRecordsChanged { get; }
    void AddDerivationRecord(string cacheKey, DerivationRecordInfo info);
    void RemoveDerivationRecord(string cacheKey);
}
