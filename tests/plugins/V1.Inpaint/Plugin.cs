using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Inpaint;

// V1 inpainting 形态 voice 测试引擎：与 V1.Voice（按 note 间隙分块、每块一段、完成即丢旧建新段）相对——
// 本引擎维护**整 part 一个贯穿音频段**，从不按 note 分片：
//   · 段几何按 5 秒网格取整（含余量）：网格内编辑就地覆写（Write 子区间 + 重 Commit）；
//     **越网格走 Resize（前/后向对称）**——段身份跨几何变更存活，交集内容钉绝对轴保留、只补渲新增区；
//     下游 effect 链节点因段身份稳定而**从不重建**（processor 缓存存活，宿主 Debug 日志可观测建/毁）；
//   · 编辑只标脏受影响的时间区间，重合成只渲染脏区（±扩到相交 note 全域），非脏区音频原样保留；
//     宿主把写入区间账本传导下游（Input.RangeModified 绝对轴），局部重合成 effect 据此收窄重算量；
//   · 状态声称只罩脏区/在渲区（inpainting 粒度的状态带：编辑一个 note 只有那一小段变橙，
//     其余保持已合成），与块式引擎的整块变橙形成肉眼对照。
// 测试钩子：歌词 "fail" → 该次 inpaint 失败并带报错文案（声称 Failed 罩本次窗口）。
// 有意从简：无音素产出、无回显轨、无自动化声明、无 part/note 属性（聚焦段身份与局部重合成语义）。

public sealed class InpaintVoiceEngine : IVoiceSynthesisEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    public void Init()
    {
        mVoiceInfos.Add("v1-inpaint", new VoiceSourceInfo
        {
            Name = "Inpaint (V1 Test)",
            Description = "Part-spanning single segment; in-place partial re-synthesis",
        });
    }

    public void Destroy() { }

    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => new InpaintSession(context);

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => sEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => sEmptyConfig;
    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyAutomations = new();
    static readonly ObjectConfig sEmptyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
}

public sealed class InpaintSession : IVoiceSynthesisSession
{
    public InpaintSession(IVoiceSynthesisContext context)
    {
        mContext = context;

        // 变更接线（数据线程，handler 只做廉价的脏区间登记）：
        // note 几何/歌词/音高变 → 标脏该 note 的新旧范围；增删 → 同理；音高曲线区间变 → 标脏相交区间。
        context.Notes.WhenAnyItem(n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Lyric.Modified, n => n.Pitch.Modified)
            .Subscribe(DirtyNote, mSubscriptions);
        context.Notes.ItemAdded.Subscribe(DirtyNote, mSubscriptions);
        context.Notes.ItemRemoved.Subscribe(DirtyRemovedNote, mSubscriptions);
        context.PartProperties.Modified.Subscribe(DirtyAll, mSubscriptions);
        context.Pitch.RangeModified.Subscribe(AddDirty);
        context.PitchDeviation.RangeModified.Subscribe(AddDirty);
    }

    public string DefaultLyric => "la";

    // 本引擎无延音语义（音频不依赖音素/延音链）。
    public bool IsContinuation(IVoiceSynthesisNote note) => false;

    // —— 调度：窗内第一个脏区间（扩到相交 note 全域）的纯值边界；无内容可渲但几何漂移待收口时
    //    也返回非空驱动一次 dispatch（纯裁剪的空提交在 SynthesizeNext 里完成）——
    public SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime)
    {
        if (FindWindow(startTime, endTime) is { } window)
            return new SynthesisRange(window.Start, window.End);
        if (NeedsGeometryPass())
            return new SynthesisRange(startTime, endTime);
        return null;
    }

    // 几何漂移待收口（peek 纯值判断、无副作用）：Resize 后无内容可渲待空提交、网格与段几何不符、
    // 或内容清空待释放段。
    bool NeedsGeometryPass()
    {
        if (mSegment == null)
            return false;
        if (mPendingGeometryCommit)
            return true;
        if (!TryGetContentSpan(out double contentStart, out double contentEnd))
            return true;
        double gridStart = Math.Max(0, Math.Floor((contentStart - kGridMargin) / kGridSeconds) * kGridSeconds);
        double gridEnd = Math.Ceiling((contentEnd + kGridMargin) / kGridSeconds) * kGridSeconds;
        return gridStart != mSegmentStart || gridEnd != mSegmentEnd;
    }

    public async Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default)
    {
        // 同步前缀（数据线程）：先对齐段几何（首建 / 网格漂移 Resize），再重导出本次窗口。
        EnsureSegment();
        if (FindWindow(startTime, endTime) is not { } window)
        {
            // 几何收口：纯裁剪（Resize 后无内容可渲）→ 空提交发布几何——账本无内容条目、下游收到
            // 「几何变、内容静默」，各级 Resize 跟随 + 空提交，全链零重算级联。
            if (mPendingGeometryCommit && mSegment != null)
            {
                mSegment.Commit();
                mPendingGeometryCommit = false;
                mStatusChanged.Invoke();
            }
            return;
        }

        // 快照：窗内相交 note 全集（FindWindow 已扩到相交 note 全域，note 不会被窗口截半）；automation 全量冻结。
        var notes = mContext.Notes.Where(n => n.StartTime.Value < window.End && n.EndTime.Value > window.Start).ToList();
        var snapshot = mContext.GetSnapshot(notes);

        ConsumeDirty(window.Start, window.End);   // 渲染期间到达的新变更会重新登记、完成后自然重排
        ClearFailed(window.Start, window.End);
        mActiveStart = window.Start;
        mActiveEnd = window.End;
        mActiveProgress = 0;
        mStatusChanged.Invoke();

        var report = new Progress<double>(p =>
        {
            mActiveProgress = p;
            mStatusChanged.Invoke();
        });

        try
        {
            var rendered = await Task.Run(() => Render(snapshot, window.Start, window.End, report, cancellation), CancellationToken.None);
            if (rendered == null)
            {
                AddDirty(window.Start, window.End);   // 取消是正常调度结局：脏账退回，不丢更新
                return;
            }

            // inpainting 核心：就地覆写贯穿段的对应子区间 + 重 Commit——段身份不变，
            // 下游 effect 节点不重建（对照 V1.Voice 的"完成即丢旧建新段"）。
            // 写入按段界兜底截断（窗口已钳网格，此处只消化独立取整的 ±1 样本误差）。
            if (mSegment != null)
            {
                int offset = (int)Math.Clamp((long)Math.Round((window.Start - mSegmentStart) * kSampleRate), 0, mSegmentCount);
                int length = Math.Min(rendered.Length, mSegmentCount - offset);
                if (length > 0)
                {
                    mSegment.Write(offset, rendered.AsSpan(0, length));
                    mSegment.Commit();
                    mPendingGeometryCommit = false;   // 本次 Commit 已一并发布几何
                }
                foreach (var note in notes)
                    mRenderedRanges[note] = (note.StartTime.Value, note.EndTime.Value);
            }
        }
        catch (Exception ex)
        {
            mFailed.Add((window.Start, window.End, ex.Message));
        }
        finally
        {
            mActiveStart = double.NaN;
            mStatusChanged.Invoke();
        }
    }

    // —— 产物：本引擎聚焦音频与段身份，其余产物面恒空 ——
    public SynthesizedPitch SynthesizedPitch => new() { Segments = [] };
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => sEmptyParameters;
    public IReadOnlyMap<string, SynthesizedSyllable> SynthesizedPhonemes => sEmptyPhonemes;

    // 状态声称（inpainting 粒度）：整个内容范围声称 Synthesized 垫底，脏区/失败区/在渲区各自叠报——
    // 宿主按 z 序分层（声称完成 < Pending < 活动），重叠即正确呈现，无需自行做区间减法。
    public IReadOnlyList<SynthesisStatusSegment> Status => BuildStatus();

    IReadOnlyList<SynthesisStatusSegment> BuildStatus()
    {
        var result = new List<SynthesisStatusSegment>();
        if (!TryGetContentSpan(out double contentStart, out double contentEnd))
            return result;

        if (mSegment != null)
            result.Add(new SynthesisStatusSegment { StartTime = contentStart, EndTime = contentEnd, Status = SynthesisSegmentStatus.Synthesized });

        foreach (var (start, end) in mDirty)
        {
            result.Add(new SynthesisStatusSegment
            {
                StartTime = Math.Max(start, contentStart),
                EndTime = Math.Min(end, contentEnd),
                Status = SynthesisSegmentStatus.Pending,
            });
        }

        foreach (var (start, end, message) in mFailed)
            result.Add(new SynthesisStatusSegment { StartTime = start, EndTime = end, Status = SynthesisSegmentStatus.Failed, Message = message });

        if (!double.IsNaN(mActiveStart))
        {
            result.Add(new SynthesisStatusSegment
            {
                StartTime = mActiveStart,
                EndTime = mActiveEnd,
                Status = SynthesisSegmentStatus.Synthesizing,
                Message = "inpainting",
                Progress = mActiveProgress,
            });
        }
        return result;
    }

    public IActionEvent SynthesizedPhonemesChanged => mSynthesizedPhonemesChanged;
    public IActionEvent SynthesizedParametersChanged => mSynthesizedParametersChanged;
    public IActionEvent SynthesizedPitchChanged => mSynthesizedPitchChanged;
    public IActionEvent StatusChanged => mStatusChanged;
    readonly ActionEvent mSynthesizedPhonemesChanged = new();
    readonly ActionEvent mSynthesizedParametersChanged = new();
    readonly ActionEvent mSynthesizedPitchChanged = new();
    readonly ActionEvent mStatusChanged = new();

    public void Dispose()
    {
        mSubscriptions.DisposeAll();
        mContext.Pitch.RangeModified.Unsubscribe(AddDirty);
        mContext.PitchDeviation.RangeModified.Unsubscribe(AddDirty);
        mSegment?.Dispose();
        mSegment = null;
    }

    // —— 段几何（5 秒网格 + 余量）：note 编辑不越网格 → 段身份恒定；越了 → 删旧建新 + 全脏（增长即全重绘）——
    void EnsureSegment()
    {
        if (!TryGetContentSpan(out double contentStart, out double contentEnd))
        {
            mSegment?.Dispose();
            mSegment = null;
            mPendingGeometryCommit = false;
            mDirty.Clear();
            mFailed.Clear();
            mRenderedRanges.Clear();
            return;
        }

        double gridStart = Math.Max(0, Math.Floor((contentStart - kGridMargin) / kGridSeconds) * kGridSeconds);
        double gridEnd = Math.Ceiling((contentEnd + kGridMargin) / kGridSeconds) * kGridSeconds;
        if (mSegment != null && gridStart == mSegmentStart && gridEnd == mSegmentEnd)
            return;

        long sampleOffset = (long)Math.Round(gridStart * kSampleRate);
        int sampleCount = (int)Math.Round((gridEnd - gridStart) * kSampleRate);
        if (mSegment == null)
        {
            mSegment = mContext.CreateAudioSegment(sampleOffset, sampleCount, kSampleRate);
            // 首建内容未写：全部内容区标脏。
            mDirty.Clear();
            AddDirty(contentStart, contentEnd);
        }
        else
        {
            // 网格变更走 Resize（前/后向对称）：段身份不变——下游 effect 节点与缓存存活；
            // 交集内容钉在绝对轴原样保留，只有触发本次变更的 note 事件已标脏的区间需要补渲，
            // 不全脏。段回未提交态，补渲窗口 Commit（或无窗可渲时的几何空提交）后收口。
            mSegment.Resize(sampleOffset, sampleCount);
            mPendingGeometryCommit = true;
        }
        mSegmentStart = gridStart;
        mSegmentEnd = gridEnd;
        mSegmentCount = sampleCount;

        // 修剪脏账到网格：删除/移走 note 的旧位置可能落在收缩后的网格外，±∞ 的曲线信号也在此收敛。
        for (int i = mDirty.Count - 1; i >= 0; i--)
        {
            var (s, e) = mDirty[i];
            s = Math.Max(s, gridStart);
            e = Math.Min(e, gridEnd);
            if (e <= s)
                mDirty.RemoveAt(i);
            else
                mDirty[i] = (s, e);
        }
    }

    bool TryGetContentSpan(out double start, out double end)
    {
        start = double.MaxValue;
        end = double.MinValue;
        foreach (var note in mContext.Notes)
        {
            start = Math.Min(start, note.StartTime.Value);
            end = Math.Max(end, note.EndTime.Value);
        }
        return end > start;
    }

    // 窗口导出（peek 与 SynthesizeNext 共用，确定性）：第一个与查询窗相交的脏区间，扩到相交 note 全域（迭代至不动点，
    // 保证 note 不被渲染窗截半——正弦相位按 note 起点积分，截半会破相位连续）。
    // 窗口恒钳到当前内容的网格（纯函数于 note 集，peek/dispatch 一致）：脏账可能含已删除/移走 note 的
    // 旧位置（落在收缩后的网格外）或 ±∞ 的曲线信号，出界部分不可渲染、由 EnsureSegment 修剪。
    (double Start, double End)? FindWindow(double startTime, double endTime)
    {
        if (!TryGetContentSpan(out double contentStart, out double contentEnd))
            return null;
        double gridStart = Math.Max(0, Math.Floor((contentStart - kGridMargin) / kGridSeconds) * kGridSeconds);
        double gridEnd = Math.Ceiling((contentEnd + kGridMargin) / kGridSeconds) * kGridSeconds;

        if (mSegment == null && mDirty.Count == 0)
            AddDirty(contentStart, contentEnd);   // 首次调度（段未建、账本空）：全内容待渲

        foreach (var (dirtyStart, dirtyEnd) in mDirty)
        {
            if (dirtyEnd < startTime || dirtyStart > endTime)
                continue;

            double start = dirtyStart, end = dirtyEnd;
            bool grown = true;
            while (grown)
            {
                grown = false;
                foreach (var note in mContext.Notes)
                {
                    if (note.StartTime.Value >= end || note.EndTime.Value <= start)
                        continue;
                    if (note.StartTime.Value < start) { start = note.StartTime.Value; grown = true; }
                    if (note.EndTime.Value > end) { end = note.EndTime.Value; grown = true; }
                }
            }
            start = Math.Max(start, gridStart);
            end = Math.Min(end, gridEnd);
            if (end <= start)
                continue;   // 整条脏区在网格外（旧位置遗账）：不可渲染，跳过待 EnsureSegment 修剪
            return (start, end);
        }
        return null;
    }

    // —— 脏区间账本（数据线程；插入即合并重叠）——
    void AddDirty(double start, double end)
    {
        if (end <= start)
            return;
        for (int i = mDirty.Count - 1; i >= 0; i--)
        {
            var (s, e) = mDirty[i];
            if (e < start || s > end)
                continue;
            start = Math.Min(start, s);
            end = Math.Max(end, e);
            mDirty.RemoveAt(i);
        }
        mDirty.Add((start, end));
        mStatusChanged.Invoke();
    }

    void ConsumeDirty(double start, double end)
    {
        for (int i = mDirty.Count - 1; i >= 0; i--)
        {
            var (s, e) = mDirty[i];
            if (e <= start || s >= end)
                continue;
            mDirty.RemoveAt(i);
            // 部分相交：留下窗外残段（罕见——窗口已扩到 note 全域，残段只来自纯曲线脏区）。
            if (s < start)
                mDirty.Add((s, start));
            if (e > end)
                mDirty.Add((end, e));
        }
    }

    void ClearFailed(double start, double end)
        => mFailed.RemoveAll(f => f.End > start && f.Start < end);

    void DirtyNote(IVoiceSynthesisNote note)
    {
        // 新旧范围都要重绘：旧位置留下的陈旧音频与新位置一样是脏的。
        if (mRenderedRanges.TryGetValue(note, out var old))
            AddDirty(old.Start, old.End);
        AddDirty(note.StartTime.Value, note.EndTime.Value);
        ClearFailed(note.StartTime.Value, note.EndTime.Value);
    }

    void DirtyRemovedNote(IVoiceSynthesisNote note)
    {
        if (mRenderedRanges.TryGetValue(note, out var old))
        {
            AddDirty(old.Start, old.End);
            mRenderedRanges.Remove(note);
        }
    }

    void DirtyAll()
    {
        if (TryGetContentSpan(out double start, out double end))
            AddDirty(start, end);
        mFailed.Clear();
    }

    // —— 渲染（worker，只读冻结快照）：窗口内清零重绘相交 note 的正弦（双通道音高消费同 V1.Voice 参照实现）——
    static float[]? Render(VoiceSynthesisSnapshot snapshot, double windowStart, double windowEnd,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        // 模拟 inpainting 推理耗时（分步上报进度；取消中途退出、产物保持上一版）。
        const int steps = 10;
        for (int sIdx = 0; sIdx < steps; sIdx++)
        {
            if (cancellation.WaitHandle.WaitOne(kSimulatedRenderMs / steps))
                return null;
            progress?.Report((double)(sIdx + 1) / steps);
        }

        if (snapshot.Notes.Any(n => string.Equals(n.Lyric, "fail", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Inpaint failed: forced test failure triggered by lyric \"fail\".");

        int sampleCount = Math.Max(1, (int)Math.Round((windowEnd - windowStart) * kSampleRate));
        var audio = new float[sampleCount];   // 窗口整体清零重绘：脏区旧内容一并覆掉

        foreach (var note in snapshot.Notes)
        {
            if (cancellation.IsCancellationRequested)
                return null;

            int from = Math.Clamp((int)((note.StartTime - windowStart) * kSampleRate), 0, sampleCount);
            int to = Math.Clamp((int)((note.EndTime - windowStart) * kSampleRate), 0, sampleCount);

            // 双通道音高：finalPitch = resolve(Pitch, 回退 note 音高) + PitchDeviation；100Hz 控制率线性插值、相位积分。
            int controlCount = Math.Max(2, (int)((note.EndTime - note.StartTime) * kControlRate) + 1);
            var controlTimes = new double[controlCount];
            for (int c = 0; c < controlCount; c++)
                controlTimes[c] = note.StartTime + (note.EndTime - note.StartTime) * c / (controlCount - 1);
            var pitchValues = snapshot.Pitch.Evaluator.Evaluate(controlTimes);
            var deviation = snapshot.PitchDeviation.Evaluator.Evaluate(controlTimes);
            for (int c = 0; c < controlCount; c++)
                pitchValues[c] = (double.IsNaN(pitchValues[c]) ? note.Pitch : pitchValues[c]) + deviation[c];

            int length = to - from;
            int attack = Math.Min(kAttackSamples, length / 2);
            int release = Math.Min(kReleaseSamples, length / 2);
            double phase = 0;
            for (int i = from; i < to; i++)
            {
                int pos = i - from;
                double envelope = pos < attack ? (double)pos / attack
                    : pos >= length - release ? (double)(length - pos) / release
                    : 1.0;
                double t = (double)pos / Math.Max(1, length - 1) * (controlCount - 1);
                int c0 = Math.Min((int)t, controlCount - 2);
                double pitch = pitchValues[c0] + (pitchValues[c0 + 1] - pitchValues[c0]) * (t - c0);
                double freq = 440.0 * Math.Pow(2, (pitch - 69) / 12.0);
                phase += 2 * Math.PI * freq / kSampleRate;
                audio[i] += (float)(0.2 * envelope * Math.Sin(phase));
            }
        }
        return audio;
    }

    const int kSimulatedRenderMs = 800;
    const int kSampleRate = 44100;
    const int kControlRate = 100;
    const double kGridSeconds = 5;    // 段几何网格：编辑不越网格 → 段身份恒定（就地覆写）
    const double kGridMargin = 1;     // 网格余量：内容贴网格边时预留增长空间
    const int kAttackSamples = (int)(0.008 * kSampleRate);
    const int kReleaseSamples = (int)(0.012 * kSampleRate);

    static readonly IReadOnlyMap<string, SynthesizedParameter> sEmptyParameters = new Map<string, SynthesizedParameter>();
    static readonly IReadOnlyMap<string, SynthesizedSyllable> sEmptyPhonemes = new Map<string, SynthesizedSyllable>();

    readonly IVoiceSynthesisContext mContext;
    readonly DisposableManager mSubscriptions = new();
    readonly List<(double Start, double End)> mDirty = new();
    readonly List<(double Start, double End, string Message)> mFailed = new();
    readonly Dictionary<IVoiceSynthesisNote, (double Start, double End)> mRenderedRanges = new();
    IAudioSegment? mSegment;
    double mSegmentStart;
    double mSegmentEnd;
    int mSegmentCount;
    bool mPendingGeometryCommit;   // Resize 后待发布几何（渲染 Commit 顺带发布；无窗可渲则几何空提交）
    double mActiveStart = double.NaN;
    double mActiveEnd;
    double mActiveProgress;
}
