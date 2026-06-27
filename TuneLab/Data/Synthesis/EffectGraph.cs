using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// 「effect 实例 × 段」反应式处理器图：voice 交付的每个音频段（及链上每一级的输出段）都是图中一个输入；
// 每个「启用且引擎可用的 effect × 一个输入段」配一个厚 IEffectProcessor 节点（自管该段失效与重处理）。
//
// 模型（段间彼此无共享上下文，分别处理后由消费端按时间混音）：
//   · voice 段 Commit → 作为第一级各 processor 的 Input；某级 processor 的输出段集 → 下一级各建 processor 接为 Input。
//   · 失效自管：processor 订阅自己的 IEffectContext（Input.Committed / Properties.Modified / automation.RangeModified）
//     自算 dirty，于 context.Committed 触发 ProcessingRequested；宿主据此把该节点标 Pending 并调度 Process。
//   · 调度：跨段/跨 part 并行，受 EffectTaskGate（Settings.MaxParallelSynthesisTasks）全局封顶；
//     按播放线就近挑 Pending 节点。链尾各输出段汇为 SynthesizedSegments（消费端按绝对时间混音）。
//   · bypass / 引擎缺失的 effect 不建节点（该级整体 passthrough，输入直接喂下一级 / 链尾）。
//
// 线程纪律：除 processor.Process 内部 offload 的 worker 外，全部成员仅数据线程访问（活视图纪律）。
// processor 的 ProcessingRequested 恒在数据线程触发（见 SDK 约定）。
internal sealed class EffectGraph : IDisposable
{
    public EffectGraph(MidiPart part, IAudioSegmentHost segments, CancellationToken cancellation, Action onChanged, Action onSettled)
    {
        mPart = part;
        mSegments = segments;
        mCancellation = cancellation;
        mOnChanged = onChanged;
        mOnSettled = onSettled;
        mPumpCallback = RequestSchedule;
        mOnVoiceSegmentsChanged = RequestSchedule;
        mSegments.AudioSegmentsChanged += mOnVoiceSegmentsChanged;
        RebuildStructureSubscriptions();
        Schedule();
    }

    // 链尾各段（工程率 + 波形）；播放/波形按段消费、按绝对时间混音。原子换引用、跨线程读安全。
    public IReadOnlyList<SynthesizedSegment> SynthesizedSegments => mSynthesizedSegments;

    // 某个 effect 的回显曲线（聚合其所有「effect × 段」节点 processor 的 SynthesizedParameters、按 key 拼接 Segments）。
    // 无该 effect 或无回显时返回空 map。原子换引用、跨线程读安全。
    public IReadOnlyMap<string, SynthesizedParameter> GetSynthesizedParameters(IEffect effect)
        => mEffectReadbacks.TryGetValue(effect, out var map) ? map : EmptyReadback;

    // 仍有在飞 Process（用于管线延迟销毁：voice/effect 都归后才销毁会话与图）。
    public bool IsBusy => mRunningCount > 0;

    // 链结构变化（增删/重排/启用切换）：重建启用-effect 订阅 + 重排图（reconcile 自然弃旧建新）。
    public void OnStructureChanged()
    {
        if (mDisposed)
            return;
        RebuildStructureSubscriptions();
        Schedule();
    }

    // 工程采样率变了：各 voice 输入快照按新率重做（清缓存版本强制重采）+ 全图重处理。
    public void OnSampleRateChanged()
    {
        if (mDisposed)
            return;

        // 清各 voice 上游的"已观测源版本"，迫使 reconcile 按新工程率重采快照并标 Input 变。
        foreach (var upstream in mVoiceUpstreams.Values)
            upstream.InvalidateSnapshot();
        // 链上各级输出经其上游 Input 变级联重处理；此处只需驱动一次调度。
        Schedule();
    }

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        mSegments.AudioSegmentsChanged -= mOnVoiceSegmentsChanged;
        EffectTaskGate.Unregister(mPumpCallback);
        mStructureSubscriptions?.DisposeAll();

        // 在飞节点收尾时自行销毁（RunNode 见 mDisposed）；非在飞节点立即销毁。
        foreach (var node in new List<EffectNode>(mNodes.Values))
        {
            if (node.Running)
                node.RequestDisposeWhileRunning();
            else
                DisposeAndRemove(node);
        }
        mVoiceUpstreams.Clear();
    }

    // —— 调度（reconcile + pump，循环至稳定、防重入）——

    // 请求一趟调度：已在调度中（如同步完成的 Process 重入、空槽唤醒、reconcile 中补发的 ProcessingRequested）
    // 只置脏让当前循环再跑一遍 reconcile；否则起一趟新调度。
    void RequestSchedule()
    {
        if (mInSchedule)
        {
            mDirty = true;
            return;
        }
        Schedule();
    }

    void Schedule()
    {
        if (mDisposed || mInSchedule)
            return;

        mInSchedule = true;
        try
        {
            // 循环至稳定：同步引擎的 Process 在 Pump 内同步完成 → 置脏 → 本循环下一趟 reconcile 接其输出、
            // 级联拉起下游节点并 pump，整条链一趟内跑完（异步引擎完成时 mInSchedule 已释、自起新调度）。
            int guard = 0;
            do
            {
                mDirty = false;
                Reconcile();
                Pump();
            }
            while (mDirty && !mDisposed && ++guard < 10000);

            if (guard >= 10000)
                Log.Error("EffectGraph schedule did not converge (possible runaway ProcessingRequested).");
        }
        finally
        {
            mInSchedule = false;
        }
        // 链尾产物可能已变（reconcile 重算/节点收尾），统一刷新 UI（重绘幂等，UI 自行节流）。
        mOnChanged();
    }

    // 由当前真相重算图：启用-effect 序列 → 逐级拉起/复用节点、收集各级输出接为下一级输入；
    // 弃不再被任何级需要的节点；最后算链尾段。
    void Reconcile()
    {
        var active = RecomputeActiveEffects();

        // 第一级输入 = 已提交的 voice 段（按工程率快照）。
        var stageInputs = CollectVoiceUpstreams();

        var desired = new HashSet<(IEffect, object)>();
        for (int k = 0; k < active.Count; k++)
        {
            var effect = active[k];
            var engine = EffectManager.GetInitedEngine(effect.Type);
            if (engine == null)
                continue;   // RecomputeActiveEffects 已过滤，理论不至；防御。

            var nextInputs = new List<UpstreamSegment>();
            foreach (var input in stageInputs)
            {
                var node = GetOrCreateNode(effect, engine, input);
                desired.Add((effect, input.SourceKey));
                if (node.Removed)
                    continue;
                nextInputs.AddRange(node.RefreshDownstreamInputs());
            }
            stageInputs = nextInputs;
        }

        // 弃不再被需要的节点（在飞者延迟到收尾销毁）。
        RemoveNodesNotIn(desired);

        // 输入重提交的存活节点：补发 context.Committed 触发其 ProcessingRequested（新建节点本就 Pending）。
        foreach (var node in mNodes.Values)
        {
            if (node.Removed)
                continue;
            int version = node.Input.CommitVersion;
            if (version != node.LastInputVersion)
            {
                node.LastInputVersion = version;
                node.Context.RaiseCommitted();
            }
        }

        BuildSynthesizedSegments(stageInputs);
        BuildEffectReadbacks();
    }

    // 在并发上限内派发 Pending 就绪节点（按播放线就近）；满则登记待空槽重 pump。
    void Pump()
    {
        if (mDisposed)
            return;

        var ready = new List<EffectNode>();
        foreach (var node in mNodes.Values)
        {
            if (!node.Removed && node.Pending && !node.Running && node.Input.CommitVersion > 0 && node.Input.SampleCount > 0)
                ready.Add(node);
        }

        if (ready.Count > 1)
            SortByPlaybackProximity(ready);

        foreach (var node in ready)
        {
            if (!EffectTaskGate.TryAcquire())
            {
                EffectTaskGate.WaitForSlot(mPumpCallback);
                break;
            }
            RunNode(node);
        }
    }

    // 跑一个节点的 Process（让出点）：取消/失败都正常收尾——失败该段 passthrough（输出 = 输入）。
    async void RunNode(EffectNode node)
    {
        node.Pending = false;
        node.Running = true;
        mRunningCount++;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(mCancellation);
        node.Cancellation = linked;

        bool errored = false;
        try
        {
            await node.Processor.Process(linked.Token);
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Effect {0} process failed: {1}", node.Effect.Type, ex));
            errored = true;
        }
        finally
        {
            node.Errored = errored;     // 先于 Release：唤醒的等待者 reconcile 时即读到正确 passthrough 态
            node.Running = false;
            node.Cancellation = null;
            linked.Dispose();
            mRunningCount--;
            EffectTaskGate.Release();   // 唤醒等待者（各自重 pump）
        }

        // 图销毁中：销毁本节点；若是最后一个在飞任务则回调管线重检销毁（voice/effect 都归才销毁会话）。
        if (mDisposed)
        {
            DisposeAndRemove(node);
            mOnSettled();
            return;
        }

        // 已被弃（输入消失等）：销毁本节点、不据其输出级联。否则正常收尾据新输出段级联下游。
        if (node.Removed)
            DisposeAndRemove(node);

        // reconcile 据新输出段级联下游 + 刷新链尾 + pump 其余。Process 同步完成时本调用在 Pump 内重入，
        // RequestSchedule 只置脏由当前循环再跑一趟；异步完成时起新调度。
        RequestSchedule();
    }

    // —— 图维护 ——

    List<IEffect> RecomputeActiveEffects()
    {
        var active = new List<IEffect>();
        foreach (var effect in mPart.Effects)
        {
            if (!effect.IsEnabled.Value)
                continue;
            if (EffectManager.GetInitedEngine(effect.Type) == null)
                continue;   // 引擎缺失/Init 失败 = 该级 passthrough。
            active.Add(effect);
        }
        return active;
    }

    // 已提交的 voice 段 → 工程率上游快照（按握柄缓存、CommitVersion 变才重采）；消失的段弃其上游。
    List<UpstreamSegment> CollectVoiceUpstreams()
    {
        var present = new HashSet<AudioSegment>();
        var result = new List<UpstreamSegment>();
        foreach (var segment in mSegments.AudioSegments)
        {
            if (!segment.IsCommitted || segment.Samples.Length == 0)
                continue;

            present.Add(segment);
            if (!mVoiceUpstreams.TryGetValue(segment, out var upstream))
            {
                upstream = new UpstreamSegment(segment);
                mVoiceUpstreams.Add(segment, upstream);
            }
            // 工程率快照：源 CommitVersion 变（或采样率变后失效）才重采新缓冲。
            if (upstream.SourceVersion != segment.CommitVersion)
            {
                int engineRate = AudioEngine.SampleRate.Value;
                float[] resampled = segment.SampleRate == engineRate
                    ? (float[])segment.Samples.Clone()
                    : AudioUtils.Resample(segment.Samples, 1, segment.SampleRate, engineRate);
                long offset = (long)Math.Round((double)segment.SampleOffset / segment.SampleRate * engineRate);
                upstream.UpdateCommitted(offset, engineRate, resampled, segment.CommitVersion);
            }
            result.Add(upstream);
        }

        if (mVoiceUpstreams.Count > present.Count)
        {
            var stale = new List<AudioSegment>();
            foreach (var key in mVoiceUpstreams.Keys)
            {
                if (!present.Contains(key))
                    stale.Add(key);
            }
            foreach (var key in stale)
                mVoiceUpstreams.Remove(key);
        }
        return result;
    }

    EffectNode GetOrCreateNode(IEffect effect, IEffectEngine engine, UpstreamSegment input)
    {
        var key = (effect, input.SourceKey);
        if (mNodes.TryGetValue(key, out var node))
            return node;

        var context = new EffectContext(mPart, effect, input);
        node = new EffectNode(effect, context, input);
        try
        {
            node.Processor = engine.CreateProcessor(context);
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Effect {0} create processor failed: {1}", effect.Type, ex));
        }
        node.ProcessingRequestedHandler = () => OnProcessingRequested(node);
        if (node.Processor != null)
            node.Processor.ProcessingRequested.Subscribe(node.ProcessingRequestedHandler);
        node.Pending = true;
        node.LastInputVersion = input.CommitVersion;
        mNodes.Add(key, node);

        // 无 processor（创建失败）→ 该段 passthrough。
        if (node.Processor == null)
            node.Errored = true;
        return node;
    }

    void RemoveNodesNotIn(HashSet<(IEffect, object)> desired)
    {
        List<(IEffect, object)>? stale = null;
        foreach (var kv in mNodes)
        {
            if (desired.Contains(kv.Key) || kv.Value.Removed)
                continue;
            (stale ??= new()).Add(kv.Key);
        }
        if (stale == null)
            return;

        foreach (var key in stale)
        {
            var node = mNodes[key];
            if (node.Running)
                node.RequestDisposeWhileRunning();   // 收尾时由 RunNode 销毁
            else
                DisposeAndRemove(node);
        }
    }

    void OnProcessingRequested(EffectNode node)
    {
        if (mDisposed || node.Removed)
            return;
        node.Pending = true;
        RequestSchedule();
    }

    void DisposeAndRemove(EffectNode node)
    {
        mNodes.Remove((node.Effect, node.Input.SourceKey));
        node.Dispose();
    }

    // 链尾输入段集 → SynthesizedSegments（工程率 + 波形，按上游版本缓存，只重算变化段）。
    void BuildSynthesizedSegments(List<UpstreamSegment> tail)
    {
        var list = new List<SynthesizedSegment>(tail.Count);
        foreach (var upstream in tail)
        {
            var seg = upstream.GetSynthesizedSegment();
            if (seg.Audio.Samples is { Length: > 0 })
                list.Add(seg);
        }
        mSynthesizedSegments = list.ToArray();
    }

    // 各 effect 回显聚合：遍历有 processor 的存活节点，按其 Effect 归组、按 key 把各段 Segments 拼起来，
    // 段按起始秒升序（满足 SynthesizedParameter「按时间升序」契约——节点字典无序，拼接后须排）。
    void BuildEffectReadbacks()
    {
        Dictionary<IEffect, Dictionary<string, List<IReadOnlyList<Point>>>>? acc = null;
        foreach (var node in mNodes.Values)
        {
            if (node.Removed || node.Processor == null)
                continue;
            var readback = node.Processor.SynthesizedParameters;
            if (readback.Count == 0)
                continue;

            acc ??= new();
            if (!acc.TryGetValue(node.Effect, out var perEffect))
            {
                perEffect = new();
                acc.Add(node.Effect, perEffect);
            }
            foreach (var kv in readback)
            {
                if (kv.Value.Segments.Count == 0)
                    continue;
                if (!perEffect.TryGetValue(kv.Key, out var segments))
                {
                    segments = new();
                    perEffect.Add(kv.Key, segments);
                }
                segments.AddRange(kv.Value.Segments);
            }
        }

        if (acc == null)
        {
            mEffectReadbacks = EmptyEffectReadbacks;
            return;
        }

        var result = new Map<IEffect, IReadOnlyMap<string, SynthesizedParameter>>();
        foreach (var (effect, perEffect) in acc)
        {
            var map = new Map<string, SynthesizedParameter>();
            foreach (var (key, segments) in perEffect)
            {
                segments.Sort((a, b) => FirstX(a).CompareTo(FirstX(b)));
                map.Add(key, new SynthesizedParameter { Segments = segments });
            }
            result.Add(effect, map);
        }
        mEffectReadbacks = result;
    }

    static double FirstX(IReadOnlyList<Point> segment) => segment.Count > 0 ? segment[0].X : double.PositiveInfinity;

    void SortByPlaybackProximity(List<EffectNode> nodes)
    {
        double currentTime = AudioEngine.CurrentTime;
        // 播放线之后的段优先（升序最早开始）；其后是线前的段（降序最晚开始，离播放线最近）。
        nodes.Sort((a, b) =>
        {
            double sa = a.Input.StartTime, sb = b.Input.StartTime;
            bool aheadA = sa >= currentTime, aheadB = sb >= currentTime;
            if (aheadA != aheadB)
                return aheadA ? -1 : 1;
            return aheadA ? sa.CompareTo(sb) : sb.CompareTo(sa);
        });
    }

    // 启用-effect 订阅：启用切换 = 结构变（建/弃该级节点），与增删/重排同走重排。
    void RebuildStructureSubscriptions()
    {
        mStructureSubscriptions?.DisposeAll();
        mStructureSubscriptions = new DisposableManager();
        foreach (var effect in mPart.Effects)
            effect.IsEnabled.Modified.Subscribe(OnStructureChanged, mStructureSubscriptions);
    }

    readonly MidiPart mPart;
    readonly IAudioSegmentHost mSegments;
    readonly CancellationToken mCancellation;
    readonly Action mOnChanged;
    readonly Action mOnSettled;
    readonly Action mPumpCallback;
    readonly Action mOnVoiceSegmentsChanged;

    readonly Dictionary<(IEffect Effect, object InputKey), EffectNode> mNodes = new();
    readonly Dictionary<AudioSegment, UpstreamSegment> mVoiceUpstreams = new();
    DisposableManager? mStructureSubscriptions;

    SynthesizedSegment[] mSynthesizedSegments = [];
    IReadOnlyMap<IEffect, IReadOnlyMap<string, SynthesizedParameter>> mEffectReadbacks = EmptyEffectReadbacks;
    static readonly IReadOnlyMap<IEffect, IReadOnlyMap<string, SynthesizedParameter>> EmptyEffectReadbacks = new Map<IEffect, IReadOnlyMap<string, SynthesizedParameter>>();
    static readonly IReadOnlyMap<string, SynthesizedParameter> EmptyReadback = new Map<string, SynthesizedParameter>();
    int mRunningCount;
    bool mInSchedule;
    bool mDirty;
    bool mDisposed;

    // —— 单个「effect × 段」节点：处理器 + 上下文 + 调度状态 + 输出段→下游上游 的映射 ——
    sealed class EffectNode
    {
        public IEffect Effect { get; }
        public EffectContext Context { get; }
        public UpstreamSegment Input { get; }
        public IEffectProcessor? Processor;
        public Action? ProcessingRequestedHandler;

        public bool Pending;
        public bool Running;
        public bool Removed;
        public bool Errored;            // 处理失败/无处理器 → 本段 passthrough（输出 = 输入）
        public int LastInputVersion;
        public CancellationTokenSource? Cancellation;

        public EffectNode(IEffect effect, EffectContext context, UpstreamSegment input)
        {
            Effect = effect;
            Context = context;
            Input = input;
        }

        // 本节点供下游的输入段集：失败/无处理器 → 直传输入（passthrough）；否则 = 各已提交输出段（包成上游）。
        public IReadOnlyList<UpstreamSegment> RefreshDownstreamInputs()
        {
            if (Errored)
                return mPassthrough ??= new[] { Input };
            return Context.RefreshOutputs();
        }

        public void RequestDisposeWhileRunning()
        {
            Removed = true;
            Cancellation?.Cancel();
        }

        public void Dispose()
        {
            Removed = true;
            if (Processor != null)
            {
                if (ProcessingRequestedHandler != null)
                    Processor.ProcessingRequested.Unsubscribe(ProcessingRequestedHandler);
                try { Processor.Dispose(); }
                catch (Exception ex) { Log.Error("Effect processor dispose failed: " + ex); }
                Processor = null;
            }
            Context.Dispose();
        }

        UpstreamSegment[]? mPassthrough;
    }

    // —— 上游音频段（SDK 面）：voice 输出或上一级 effect 输出的只读不可变视图。
    //    已提交版本 PCM 为按源 CommitVersion 拷的快照（重 Commit 换新缓冲）；worker 同步前缀抓引用直读。——
    sealed class UpstreamSegment : IUpstreamAudioSegment
    {
        // 图键：底层源对象身份（voice 段握柄 / 上一级输出段）——节点按此稳定缓存。
        public object SourceKey { get; }

        public UpstreamSegment(object sourceKey)
        {
            SourceKey = sourceKey;
        }

        public long SampleOffset { get; private set; }
        public int SampleCount => mSamples.Length;
        public int SampleRate { get; private set; }
        public ReadOnlyMemory<float> Samples => mSamples;
        public int CommitVersion { get; private set; }
        public IActionEvent Committed => mCommitted;
        readonly ActionEvent mCommitted = new();

        public double StartTime => SampleRate > 0 ? (double)SampleOffset / SampleRate : 0;

        // 已观测的源 CommitVersion（CollectVoiceUpstreams / RefreshOutputs 据此判是否需重拷快照）。
        public int SourceVersion { get; private set; } = -1;

        public void UpdateCommitted(long sampleOffset, int sampleRate, float[] snapshot, int sourceVersion)
        {
            SampleOffset = sampleOffset;
            SampleRate = sampleRate;
            mSamples = snapshot;
            SourceVersion = sourceVersion;
            CommitVersion++;
            mCachedVersion = -1;   // 链尾段缓存失效
            mCommitted.Invoke();
        }

        public void InvalidateSnapshot() => SourceVersion = -1;

        // 链尾段产物（工程率 + 波形），按本上游 CommitVersion 缓存。
        public SynthesizedSegment GetSynthesizedSegment()
        {
            if (mCachedVersion == CommitVersion && mCached.Waveform != null)
                return mCached;

            int engineRate = AudioEngine.SampleRate.Value;
            float[] samples = mSamples;
            int rate = SampleRate;
            double startTime = StartTime;
            if (rate != engineRate && samples.Length > 0)
            {
                samples = AudioUtils.Resample(samples, 1, rate, engineRate);
                rate = engineRate;
            }
            var audio = new MonoAudio(startTime, rate, samples);
            mCached = new SynthesizedSegment(audio, new Waveform(samples));
            mCachedVersion = CommitVersion;
            return mCached;
        }

        float[] mSamples = [];
        int mCachedVersion = -1;
        SynthesizedSegment mCached;
    }

    // —— 输出段握柄（SDK 面 IAudioSegment）：processor 写入并 Commit 的产物；与 voice 段同语义。
    //    宿主据其 CommitVersion 把已提交内容拷成下游上游快照。重分片 Dispose 即从节点输出集摘除。——
    sealed class OutputSegment : IAudioSegment
    {
        public OutputSegment(EffectContext owner, long sampleOffset, int sampleCount, int sampleRate)
        {
            mOwner = owner;
            SampleOffset = sampleOffset;
            SampleRate = sampleRate;
            mSamples = new float[Math.Max(0, sampleCount)];
        }

        public long SampleOffset { get; }
        public int SampleRate { get; }
        public float[] Samples => mSamples;
        public bool IsCommitted { get; private set; }
        public int CommitVersion { get; private set; }

        public void Write(int offset, ReadOnlySpan<float> samples)
        {
            samples.CopyTo(mSamples.AsSpan(offset));
            IsCommitted = false;
        }

        public void Commit()
        {
            IsCommitted = true;
            CommitVersion++;
        }

        public void Dispose() => mOwner.RemoveOutput(this);

        readonly EffectContext mOwner;
        readonly float[] mSamples;
    }

    // —— IEffectContext 宿主实现：绑定「该 effect × 一个上游段」、随节点死。processor 订阅它自管失效。——
    sealed class EffectContext : IEffectContext, IDisposable
    {
        public EffectContext(MidiPart part, IEffect effect, UpstreamSegment input)
        {
            mPart = part;
            mEffect = effect;
            Input = input;
            mBatchSignal = part.SynthesisBatch;

            // 收口脉冲与颗粒脏同源、经 part 合成批量收口（与 voice 的 VoiceSynthesisContext 对称，避免用 effect.Modified
            // 聚合事件——其在滑条拖拽 merge 中乱发、又不在 merge 收口补发，导致触发与终值错位、滞后一拍）：
            //   · 参数：effect.Properties.Modified（settled，滑条拖拽经 DataObject merge 收到松手才发）——
            //     mProperties 在 re-raise Modified（processor 据此标参数脏）后回调 ForwardCommitted，顺序确定；
            //   · 自动化：各轨 RangeModified（每步同步发，但绘制操作处于合成批量内 → 收口到 BatchEnd 一次）。
            // 批量中只标 pending、BatchEnd 一次性收口；不在批量则即时收口。
            mProperties = new LivePropertyObject(effect.Properties, ForwardCommitted);
            mOnBatchEnd = OnBatchEnd;
            mBatchSignal.BatchEnd += mOnBatchEnd;
            WireAutomations();
        }

        public IUpstreamAudioSegment Input { get; }
        public IReadOnlyNotifiablePropertyObject Properties => mProperties;
        public IActionEvent Committed => mCommitted;
        readonly ActionEvent mCommitted = new();

        // 已声明连续 automation 轨只读 map：轨集 = effect 声明的 AutomationConfigs 中非分段者；
        // 代理按 key 缓存、缺则补建；每次读重建 map 以反映当前声明集。
        public IReadOnlyMap<string, ISynthesisAutomation> Automations
        {
            get
            {
                var map = new Map<string, ISynthesisAutomation>();
                foreach (var kvp in mEffect.AutomationConfigs)
                {
                    if (kvp.Value.IsPiecewise)
                        continue;
                    string key = kvp.Key.Id;
                    if (!mAutomationProxies.TryGetValue(key, out var proxy))
                    {
                        proxy = new AutomationProxy(mPart, mEffect, key);
                        mAutomationProxies.Add(key, proxy);
                    }
                    map.Add(key, proxy);
                }
                return map;
            }
        }

        public IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate)
        {
            var segment = new OutputSegment(this, sampleOffset, sampleCount, sampleRate);
            mOutputs.Add(segment);
            return segment;
        }

        // 收口：批量中（如 automation 绘制的 BeginMergeDirty 作用域）只标 pending，BatchEnd 一次性发；
        // 非批量（如滑条松手时的 Properties.Modified）即时发。
        void ForwardCommitted()
        {
            if (mBatchSignal.IsBatching)
            {
                mPendingCommitted = true;
                return;
            }
            RaiseCommitted();
        }

        void OnBatchEnd()
        {
            if (!mPendingCommitted)
                return;
            mPendingCommitted = false;
            RaiseCommitted();
        }

        internal void RaiseCommitted()
        {
            try { mCommitted.Invoke(); }
            catch (Exception ex) { Log.Error("Effect context committed handler threw: " + ex); }
        }

        // 把已提交输出段同步成下游上游集（按各输出段 CommitVersion 重拷快照）。未提交的段不进下游。
        internal IReadOnlyList<UpstreamSegment> RefreshOutputs()
        {
            if (mOutputs.Count == 0)
                return Array.Empty<UpstreamSegment>();

            var result = new List<UpstreamSegment>(mOutputs.Count);
            foreach (var output in mOutputs)
            {
                if (!output.IsCommitted || output.Samples.Length == 0)
                    continue;

                if (!mOutputUpstreams.TryGetValue(output, out var upstream))
                {
                    upstream = new UpstreamSegment(output);
                    mOutputUpstreams.Add(output, upstream);
                }
                if (upstream.SourceVersion != output.CommitVersion)
                    upstream.UpdateCommitted(output.SampleOffset, output.SampleRate, (float[])output.Samples.Clone(), output.CommitVersion);
                result.Add(upstream);
            }
            return result;
        }

        internal void RemoveOutput(OutputSegment output)
        {
            mOutputs.Remove(output);
            mOutputUpstreams.Remove(output);
        }

        void WireAutomations()
        {
            mAutomationSubscriptions?.DisposeAll();
            mAutomationSubscriptions = new DisposableManager();
            // 轨集合变（条件轨显隐）→ 重接 + 视为收口。
            mEffect.Automations.MapModified.Subscribe(OnAutomationMapModified, mAutomationSubscriptions);
            foreach (var kv in mEffect.Automations)
            {
                string id = kv.Key;
                kv.Value.RangeModified.Subscribe(
                    (relStart, relEnd) => NotifyAutomationRange(id, relStart, relEnd), mAutomationSubscriptions);
                // 默认值平移 = 整轨全区间失效（与 voice 对称）。
                kv.Value.DefaultValue.Modified.Subscribe(
                    () => NotifyAutomationRange(id, double.NegativeInfinity, double.PositiveInfinity), mAutomationSubscriptions);
            }
        }

        void OnAutomationMapModified()
        {
            WireAutomations();
            ForwardCommitted();
        }

        // part 相对 tick 区间 → 全局秒，注入对应轨代理的 RangeModified（颗粒脏：processor 据此标 env 脏），
        // 随后收口（绘制操作处于合成批量内 → BatchEnd 一次性触发处理）。
        void NotifyAutomationRange(string id, double relStartTick, double relEndTick)
        {
            if (mAutomationProxies.TryGetValue(id, out var proxy))
                proxy.NotifyRangeModified(RelTickToGlobalSecond(relStartTick), RelTickToGlobalSecond(relEndTick));
            ForwardCommitted();
        }

        double RelTickToGlobalSecond(double relTick)
        {
            if (double.IsInfinity(relTick))
                return relTick;
            return mPart.TempoManager.GetTime(relTick + mPart.Pos.Value);
        }

        public void Dispose()
        {
            mBatchSignal.BatchEnd -= mOnBatchEnd;
            mAutomationSubscriptions?.DisposeAll();
            mProperties.Dispose();
            mOutputs.Clear();
            mOutputUpstreams.Clear();
        }

        readonly MidiPart mPart;
        readonly IEffect mEffect;
        readonly BatchSignal mBatchSignal;
        readonly LivePropertyObject mProperties;
        readonly Action mOnBatchEnd;
        bool mPendingCommitted;
        readonly List<OutputSegment> mOutputs = new();
        readonly Dictionary<OutputSegment, UpstreamSegment> mOutputUpstreams = new();
        readonly Dictionary<string, AutomationProxy> mAutomationProxies = new();
        DisposableManager? mAutomationSubscriptions;
    }

    // —— 该 effect 某条自动化轨的活视图：求值（全局秒 → 全局 tick → part 相对 tick → effect 取值）+ 区间订阅。——
    sealed class AutomationProxy(MidiPart part, IEffect effect, string automationID) : ISynthesisAutomation
    {
        public IActionEvent<double, double> RangeModified => mRangeModified;
        readonly ActionEvent<double, double> mRangeModified = new();

        public double[] Evaluate(IReadOnlyList<double> times)
        {
            double pos = part.Pos.Value;
            var ticks = part.TempoManager.GetTicks(times);
            for (int i = 0; i < ticks.Length; i++)
                ticks[i] -= pos;
            return effect.GetAutomationValues(ticks, automationID);
        }

        internal void NotifyRangeModified(double startSecond, double endSecond)
        {
            try { mRangeModified.Invoke(startSecond, endSecond); }
            catch (Exception ex) { Log.Error("Effect automation range handler threw: " + ex); }
        }
    }

    // —— 只读属性活视图守卫：借壳 effect.Properties（短命随节点死、读时不跨线程），事件 re-raise 到自身。——
    sealed class LivePropertyObject : IReadOnlyNotifiablePropertyObject, IDisposable
    {
        public IActionEvent WillModify => mWillModify;
        public IActionEvent Modified => mModified;

        // onModified（可选）在 re-raise Modified 之后回调——供 owner 在颗粒脏（订阅 Modified 者）已置后再收口，
        // 顺序确定（不依赖跨接口的订阅注册次序）。仅根节点传入，子节点（Object(key)）不带。
        public LivePropertyObject(IReadOnlyNotifiablePropertyObject source, Action? onModified = null)
        {
            mSource = source;
            mOnWillModify = () => mWillModify.Invoke();
            mOnModified = () =>
            {
                mModified.Invoke();
                onModified?.Invoke();
            };
            mSource.WillModify.Subscribe(mOnWillModify);
            mSource.Modified.Subscribe(mOnModified);
        }

        public IReadOnlyNotifiablePropertyObject Object(string key)
        {
            if (!mChildren.TryGetValue(key, out var child))
            {
                child = new LivePropertyObject(mSource.Object(key));
                mChildren.Add(key, child);
            }
            return child;
        }

        public PropertyValue GetValue(string key, PropertyValue defaultValue) => mSource.GetValue(key, defaultValue);

        public void Dispose()
        {
            mSource.WillModify.Unsubscribe(mOnWillModify);
            mSource.Modified.Unsubscribe(mOnModified);
            foreach (var kv in mChildren)
                kv.Value.Dispose();
            mChildren.Clear();
        }

        readonly IReadOnlyNotifiablePropertyObject mSource;
        readonly Action mOnWillModify;
        readonly Action mOnModified;
        readonly ActionEvent mWillModify = new();
        readonly ActionEvent mModified = new();
        readonly Dictionary<string, LivePropertyObject> mChildren = new();
    }
}
