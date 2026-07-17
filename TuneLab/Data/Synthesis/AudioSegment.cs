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

// 音频段握柄（宿主实现 SDK 的 IAudioSegment）：创建时分配缓冲，插件就地写子区间、Commit() 标固定，
// 语义整改换新 Dispose() 重建；内容延续的扩展/裁剪走 Resize()（身份保持——下游 effect 链节点与缓存存活）。
// 从 voice 的 VoiceSynthesisContext 提为共享类，voice / instrument context 共用、EffectGraph 统一消费。
// 时间对齐：全局 0 时刻 = 采样点 0；缓冲按插件 native 采样率从段起始铺。
// 写入区间账本：每次 Write 记账（绝对采样位置，native 率），Commit 后由 EffectGraph 快照刷新时取走
// （TakeChangedRanges）传导给下游——上游只写头部新增区，下游账本就只有头部，局部重合成据此收窄重算量。
// 线程：写 / 提交 / 改几何 / 释放全在数据线程（worker 渲染完，在 marshal 回数据线程的续延里写）。
internal sealed class AudioSegment : IAudioSegment
{
    public AudioSegment(IAudioSegmentOwner owner, long sampleOffset, int sampleCount, int sampleRate)
    {
        mOwner = owner;
        SampleOffset = sampleOffset;
        SampleRate = sampleRate;
        mSamples = new float[Math.Max(0, sampleCount)];
    }

    public long SampleOffset { get; private set; }
    public int SampleRate { get; }   // 该段 native 采样率（创建时传入；宿主侧读取 / 重采样，不经 IAudioSegment 暴露给插件）
    public float[] Samples => mSamples;
    public bool IsCommitted { get; private set; }
    public int CommitVersion { get; private set; }   // 每次 Commit 自增：管线据此识别"同握柄重提交"刷新该段快照

    public void Write(int offset, ReadOnlySpan<float> samples)
    {
        mOwner.AssertSegmentThread();
        samples.CopyTo(mSamples.AsSpan(offset));   // 越界即抛（契约：超出 sampleCount 非法）
        RecordRange(SampleOffset + offset, samples.Length);
        IsCommitted = false;
    }

    public void Commit()
    {
        mOwner.AssertSegmentThread();
        IsCommitted = true;
        CommitVersion++;
        mOwner.NotifyAudioSegmentsChanged();
    }

    // 就地改几何、身份不变（SDK 契约）：交集内容按全局采样位置对齐保留（内容钉在绝对轴，前/后向扩展对称），
    // 新增区域清零；回未提交态（写完新增区域重 Commit 收口，期间下游持续消费上一版已提交快照）。
    // 不通知（未提交瞬态无消费者）；**几何对称差入账**（旧∖新 = 内容从有到无的裁剪区、新∖旧 = 零内容出现区）——
    // 几何变更对下游同样是上下文变更（边界邻域的正确输出依赖被裁掉的内容），必须如实通知；
    // 重算范围由下游自决（点态引擎自决为零、上下文引擎外扩自己的窗求交）。
    public void Resize(long sampleOffset, int sampleCount)
    {
        mOwner.AssertSegmentThread();
        sampleCount = Math.Max(0, sampleCount);
        WriteRangeLedger.RecordSymmetricDifference(ref mChangedRanges, SampleOffset, SampleOffset + mSamples.Length, sampleOffset, sampleOffset + sampleCount);
        var resized = new float[sampleCount];
        long copyStart = Math.Max(SampleOffset, sampleOffset);
        long copyEnd = Math.Min(SampleOffset + mSamples.Length, sampleOffset + sampleCount);
        if (copyEnd > copyStart)
            Array.Copy(mSamples, (int)(copyStart - SampleOffset), resized, (int)(copyStart - sampleOffset), (int)(copyEnd - copyStart));
        mSamples = resized;
        SampleOffset = sampleOffset;
        IsCommitted = false;
    }

    public void Dispose()
    {
        mOwner.AssertSegmentThread();
        mOwner.RemoveAudioSegment(this);
    }

    // 取走累积的写入区间账本（绝对采样位置，native 率；EffectGraph 快照刷新时调用，取走即清）。
    // null = 本轮无记账（如 Commit 未伴随 Write——内容未变，下游可跳过）。
    public List<(long Start, int Count)>? TakeChangedRanges()
    {
        var ranges = mChangedRanges;
        mChangedRanges = null;
        return ranges;
    }

    void RecordRange(long start, int count)
        => WriteRangeLedger.Record(ref mChangedRanges, start, count);

    readonly IAudioSegmentOwner mOwner;
    float[] mSamples;
    List<(long Start, int Count)>? mChangedRanges;
}

// 写入区间账本的共享合并逻辑（voice/instrument 的 AudioSegment 与 effect 输出段共用）：
// 插入即合并重叠/相邻（账本条目数 = 本轮离散写入簇数，恒小）；区间为绝对采样位置（各自 native 率）。
internal static class WriteRangeLedger
{
    public static void Record(ref List<(long Start, int Count)>? ranges, long start, int count)
    {
        if (count <= 0)
            return;
        ranges ??= new List<(long Start, int Count)>();
        long end = start + count;
        for (int i = ranges.Count - 1; i >= 0; i--)
        {
            var (s, c) = ranges[i];
            long e = s + c;
            if (e < start || s > end)
                continue;
            start = Math.Min(start, s);
            end = Math.Max(end, e);
            ranges.RemoveAt(i);
        }
        ranges.Add((start, (int)Math.Min(int.MaxValue, end - start)));
    }

    // 几何对称差入账（Resize 用）：旧域∖新域（裁剪掉的内容）+ 新域∖旧域（新出现的零内容）逐段记账。
    public static void RecordSymmetricDifference(ref List<(long Start, int Count)>? ranges, long oldStart, long oldEnd, long newStart, long newEnd)
    {
        static void Add(ref List<(long Start, int Count)>? r, long s, long e)
        {
            if (e > s)
                Record(ref r, s, (int)Math.Min(int.MaxValue, e - s));
        }
        Add(ref ranges, oldStart, Math.Min(oldEnd, newStart));   // 旧域左侧越出新域
        Add(ref ranges, Math.Max(oldStart, newEnd), oldEnd);     // 旧域右侧越出新域
        Add(ref ranges, newStart, Math.Min(newEnd, oldStart));   // 新域左侧越出旧域
        Add(ref ranges, Math.Max(newStart, oldEnd), newEnd);     // 新域右侧越出旧域
    }
}
