using System;
using System.Collections.Generic;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// 音频段登记表的宿主只读面：voice / instrument 的合成 context 都实现，供 EffectGraph 统一消费
//（effect 链对两类音源输出同样生效——段是领域中性的音频承载 + 失效单元）。
internal interface IAudioSegmentHost
{
    IReadOnlyList<AudioSegment> AudioSegments { get; }

    // 段集 / 段内容变化（Commit 或 Dispose）→ 通知效果图 reconcile（变了哪段据 AudioSegments + CommitVersion 算）。
    event Action? AudioSegmentsChanged;
}

// AudioSegment 的属主回调（创建它的 context 实现）：数据线程断言 + 从登记表摘除 + 变更通知。
internal interface IAudioSegmentOwner
{
    void AssertSegmentThread();
    void RemoveAudioSegment(AudioSegment segment);
    void NotifyAudioSegmentsChanged();
}

// 音频段握柄（宿主实现 SDK 的 IAudioSegment）：创建时一次性分配固定缓冲，插件就地写子区间、Commit() 标固定，
// 重分片 Dispose() 释放重建。从 voice 的 VoiceSynthesisContext 提为共享类，voice / instrument context 共用、
// EffectGraph 统一消费。时间对齐：全局 0 时刻 = 采样点 0；缓冲按插件 native 采样率从段起始铺。
// 线程：写 / 提交 / 释放全在数据线程（worker 渲染完，在 marshal 回数据线程的续延里写）。
internal sealed class AudioSegment : IAudioSegment
{
    public AudioSegment(IAudioSegmentOwner owner, long sampleOffset, int sampleCount, int sampleRate)
    {
        mOwner = owner;
        SampleOffset = sampleOffset;
        SampleRate = sampleRate;
        mSamples = new float[Math.Max(0, sampleCount)];
    }

    public long SampleOffset { get; }
    public int SampleRate { get; }   // 该段 native 采样率（创建时传入；宿主侧读取 / 重采样，不经 IAudioSegment 暴露给插件）
    public float[] Samples => mSamples;
    public bool IsCommitted { get; private set; }
    public int CommitVersion { get; private set; }   // 每次 Commit 自增：管线据此识别"同握柄重提交"重建该段链

    public void Write(int offset, ReadOnlySpan<float> samples)
    {
        mOwner.AssertSegmentThread();
        samples.CopyTo(mSamples.AsSpan(offset));   // 越界即抛（契约：超出 sampleCount 非法）
        IsCommitted = false;
    }

    public void Commit()
    {
        mOwner.AssertSegmentThread();
        IsCommitted = true;
        CommitVersion++;
        mOwner.NotifyAudioSegmentsChanged();
    }

    public void Dispose()
    {
        mOwner.AssertSegmentThread();
        mOwner.RemoveAudioSegment(this);
    }

    readonly IAudioSegmentOwner mOwner;
    readonly float[] mSamples;
}
