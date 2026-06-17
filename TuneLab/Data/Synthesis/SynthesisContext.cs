using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Data.Timing;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// ISynthesisContext 的宿主实现：会话级中间层，每次 CreateSession 新建、随会话死。
// 插件订阅的事件字段全在本对象/其代理对象上（短命，随会话一起回收 → 泄漏结构性不可能）；
// 本对象内部订阅长寿数据层（part/note/automation/tempo），由宿主在数据线程转发——
// 借壳数据层最小订阅面（IReadOnlyNotifiable），天然只转发已提交的真实变更（merge 中间态不外漏）。
//
// 坐标系约定（SDK 面）：tick/秒均为全局工程轴（与音频产物、状态段同一时间系）；
// 宿主数据层的 part 相对量在本层完成偏移换算。
internal sealed class SynthesisContext : ISynthesisContext, IDisposable
{
    public MidiPart Part => mPart;

    public IReadOnlyNotifiableLinkedList<ILiveNote> Notes => mNotes;
    public IReadOnlyNotifiablePropertyObject PartProperties => mPartProperties;
    public ILiveAutomation Pitch => mPitch;
    public ILiveAutomation PitchDeviation => mPitchDeviation;

    public event Action? Committed;

    public SynthesisContext(MidiPart part)
    {
        mPart = part;
        mDataThreadId = System.Environment.CurrentManagedThreadId;
        mTiming = new LiveTiming(this, part.TempoManager);
        // 音高双通道：Pitch = 纯用户绘制曲线（NaN=自由）；PitchDeviation = 宿主侧偏差源汇总
        // （当前即 vibrato 偏移，处处有值默认 0）。插件 finalPitch = resolve(Pitch) + PitchDeviation。
        mPitch = new AutomationProxy(this, ticks => part.Pitch.GetValues(ticks));
        mPitchDeviation = new AutomationProxy(this, ticks => part.GetVibratoDeviation(ticks));
        mNotes = new NoteProxyList(this);
        mPartProperties = new PropertyObjectGuard(this, part.Properties);

        // 批量括号：显式作用域来自 part 的 BatchSignal（含 undo/redo 重放）。
        mBatchSignal = part.SynthesisBatch;
        mBatchSignal.BatchEnd += OnBatchEnd;

        // 时基变了（tempo 表变更 / part 平移）：宿主整体重建 session（含 context），不在此做
        // 增量通知——平移跨 bpm 段会改各 note 秒时长，非纯偏移，第一版无脑重建最简单正确。
        // 故 note 边界 DerivedProperty 不订阅 part.Pos / TempoManager（其变化由 session 重建覆盖）。

        // 区间失效分通道：pitch 曲线 → Pitch；vibrato 几何 → PitchDeviation。
        part.Pitch.RangeModified.Subscribe(NotifyPitchRangeModified, s);
        foreach (var vibrato in part.Vibratos)
        {
            WireVibrato(vibrato);
        }
        part.Vibratos.ItemAdded.Subscribe(WireVibrato, s);
        part.Vibratos.ItemRemoved.Subscribe(UnwireVibrato, s);

        // automation 区间失效：已存在的轨逐条接线，此后增删动态接/拆线。
        foreach (var kvp in part.Automations)
        {
            WireAutomation(kvp.Key, kvp.Value);
        }
        var automationMap = (IReadOnlyDataMap<string, IAutomation>)part.Automations;
        automationMap.ItemAdded.Subscribe(WireAutomation, s);
        automationMap.ItemRemoved.Subscribe(UnwireAutomation, s);
    }

    // 快照物化（插件在 SynthesizeNext 同步前缀主动拉取）：物化/版本缓存/记账收在宿主一处。
    // [startTime, endTime] 为全局秒开窗区间，物化器内部经 tempo 快照换算到 tick 找锚点。
    public SynthesisSnapshot GetSnapshot(IReadOnlyList<ILiveNote> notes, double startTime, double endTime)
    {
        AssertDataThread();
        return SynthesisSnapshotFactory.Capture(mPart, notes, startTime, endTime);
    }

    // 音频段工厂（插件调入）：一次性分配固定长度缓冲、登记握柄。插件就地写子区间、Commit 标完成，
    // 重分片时 Dispose 释放（从登记表摘除）。管线（VoiceSynthesisPipeline）经 AudioSegments 读取拼装。
    public IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate)
    {
        AssertDataThread();
        var segment = new AudioSegment(this, sampleOffset, sampleCount, sampleRate);
        if (!mDisposed)
            mAudioSegments.Add(segment);
        return segment;
    }

    // 宿主侧读取面（同 TuneLab 程序集内的管线消费）：已交付音频的段供 effect 链按段过。
    internal IReadOnlyList<AudioSegment> AudioSegments => mAudioSegments;

    // 段集 / 段内容变化（Commit 或 Dispose）→ 通知管线 reconcile（变了哪段据 AudioSegments + CommitVersion 算）。
    internal event Action? AudioSegmentsChanged;

    void RemoveAudioSegment(AudioSegment segment)
    {
        mAudioSegments.Remove(segment);
        NotifyAudioSegmentsChanged();
    }

    void NotifyAudioSegmentsChanged()
    {
        if (!mDisposed)
            AudioSegmentsChanged?.Invoke();
    }

    public bool TryGetAutomation(string key, [MaybeNullWhen(false)] out ILiveAutomation automation)
    {
        if (!mPart.IsEffectiveAutomation(key))
        {
            automation = null;
            return false;
        }

        if (!mAutomationProxies.TryGetValue(key, out var proxy))
        {
            proxy = new AutomationProxy(this, ticks => mPart.GetFinalAutomationValues(ticks, key));
            mAutomationProxies.Add(key, proxy);
        }
        automation = proxy;
        return true;
    }

    // 数据线程纪律断言（DEBUG）：活视图仅数据线程可用是纪律性约束，类型上无法强制——
    // 这里让违例插件在开发期第一次跨线程访问就炸响，而非静默数据竞争。v2 进程隔离后物理强制。
    [System.Diagnostics.Conditional("DEBUG")]
    internal void AssertDataThread()
    {
        if (System.Environment.CurrentManagedThreadId != mDataThreadId)
            throw new InvalidOperationException("Synthesis live view (ISynthesisContext and its proxies) must only be accessed on the data thread; synthesize against the immutable SynthesisSnapshot instead.");
    }

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        s.DisposeAll();
        mBatchSignal.BatchEnd -= OnBatchEnd;
        foreach (var kvp in mAutomationSubscriptions)
        {
            kvp.Value.DisposeAll();
        }
        mAutomationSubscriptions.Clear();
        foreach (var kvp in mVibratoSubscriptions)
        {
            kvp.Value.DisposeAll();
        }
        mVibratoSubscriptions.Clear();
        mNotes.Dispose();
        mPartProperties.Dispose();
        mAudioSegments.Clear();
    }

    // —— 转发旋钮（线程/时机/故障隔离/批量都收在这里）——

    // 改后类通知的统一转发口：变更通知发完后补一个 Committed 收口（不在显式批量中时给单条编辑也补，
    // 契约"每个逻辑编辑都收口一次"的单条情形；批量中则由 OnBatchEnd 在批量结束统一收口）。try-catch 隔离插件故障。
    internal void ForwardChange(Action raise)
    {
        if (mDisposed)
            return;

        if (mBatchSignal.IsBatching)
        {
            Guarded(raise);
            return;
        }

        try
        {
            Guarded(raise);
        }
        finally
        {
            Guarded(() => Committed?.Invoke());
        }
    }

    // 改前事件直转（不入括号）：插件在 handler 内读旧值做廉价记录，重活留到 BatchEnd。
    internal void ForwardWill(Action raise)
    {
        if (mDisposed)
            return;

        Guarded(raise);
    }

    internal SynthesisNoteProxy? ProxyOf(INote? note) => note == null ? null : mNotes.ProxyOf(note);

    static void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Synthesis context handler threw: " + ex);
        }
    }

    void OnBatchEnd() => Guarded(() => Committed?.Invoke());

    // part 相对 tick 区间 → 全局秒区间（±∞ 直通，表整轨失效）。
    double ToGlobalSecond(double relTick)
    {
        if (double.IsInfinity(relTick))
            return relTick;
        return mTiming.ToSecond(mPart.Pos.Value + relTick);
    }

    void NotifyPitchRangeModified(double start, double end)
    {
        mPitch.NotifyRangeModified(ToGlobalSecond(start), ToGlobalSecond(end));
    }

    void NotifyDeviationRangeModified(double start, double end)
    {
        mPitchDeviation.NotifyRangeModified(ToGlobalSecond(start), ToGlobalSecond(end));
    }

    void WireVibrato(Vibrato vibrato)
    {
        if (mVibratoSubscriptions.ContainsKey(vibrato))
            return;

        var subscriptions = new DisposableManager();
        vibrato.RangeModified.Subscribe(NotifyDeviationRangeModified, subscriptions);
        mVibratoSubscriptions.Add(vibrato, subscriptions);
    }

    void UnwireVibrato(Vibrato vibrato)
    {
        if (mVibratoSubscriptions.Remove(vibrato, out var subscriptions))
            subscriptions.DisposeAll();
    }

    void WireAutomation(string key, IAutomation automation)
    {
        if (mAutomationSubscriptions.ContainsKey(key))
            return;

        var subscriptions = new DisposableManager();
        if (key == ConstantDefine.VolumeID)
        {
            // 音量在宿主混音阶段应用、不进合成，变更不产生合成失效（与旧管线一致）。
            mAutomationSubscriptions.Add(key, subscriptions);
            return;
        }

        automation.RangeModified.Subscribe((start, end) =>
        {
            NotifyAutomationRangeModified(key, start, end);   // part 相对 tick，转发处换算全局秒
        }, subscriptions);
        // 默认值变更 = 整轨取值平移，全范围失效。
        automation.DefaultValue.Modified.Subscribe(() =>
        {
            NotifyAutomationRangeModified(key, double.NegativeInfinity, double.PositiveInfinity);
        }, subscriptions);
        mAutomationSubscriptions.Add(key, subscriptions);
    }

    void UnwireAutomation(string key, IAutomation automation)
    {
        if (mAutomationSubscriptions.Remove(key, out var subscriptions))
            subscriptions.DisposeAll();
    }

    readonly Dictionary<Vibrato, DisposableManager> mVibratoSubscriptions = new();

    void NotifyAutomationRangeModified(string key, double relStart, double relEnd)
    {
        double startSecond = ToGlobalSecond(relStart);
        double endSecond = ToGlobalSecond(relEnd);

        // 包络轨影响 vibrato 偏移：偏差通道（及受 vibrato 影响的轨）随之失效。
        // v1 按主要影响面转发到 PitchDeviation；受影响 automation 轨的精确失效缓后。
        if (key == ConstantDefine.VibratoEnvelopeID)
            mPitchDeviation.NotifyRangeModified(startSecond, endSecond);

        if (mAutomationProxies.TryGetValue(key, out var proxy))
            proxy.NotifyRangeModified(startSecond, endSecond);
    }

    readonly MidiPart mPart;
    readonly int mDataThreadId;
    readonly BatchSignal mBatchSignal;
    readonly LiveTiming mTiming;
    readonly AutomationProxy mPitch;
    readonly AutomationProxy mPitchDeviation;
    readonly NoteProxyList mNotes;
    readonly PropertyObjectGuard mPartProperties;
    readonly Dictionary<string, AutomationProxy> mAutomationProxies = new();
    readonly Dictionary<string, DisposableManager> mAutomationSubscriptions = new();
    readonly DisposableManager s = new();
    readonly List<AudioSegment> mAudioSegments = new();
    bool mDisposed;

    // —— 音频段握柄的宿主实现：创建时一次性分配固定缓冲（采样域，全局采样位置 SampleOffset）；
    //    插件就地写子区间、Commit 标固定；管线读 SampleOffset/Samples 拼装、驱动 effect。
    //    Dispose = 从登记表摘除（重分片 / 改长度位置时重建）。 ——
    internal sealed class AudioSegment : IAudioSegment
    {
        public AudioSegment(SynthesisContext owner, long sampleOffset, int sampleCount, int sampleRate)
        {
            mOwner = owner;
            SampleOffset = sampleOffset;
            SampleRate = sampleRate;
            mSamples = new float[Math.Max(0, sampleCount)];
        }

        public long SampleOffset { get; }
        public int SampleRate { get; }   // 该段 native 采样率（插件创建时传入；宿主侧读取，不经 IAudioSegment 暴露给插件）
        public float[] Samples => mSamples;
        public bool IsCommitted { get; private set; }
        public int CommitVersion { get; private set; }   // 每次 Commit 自增：管线据此识别"同握柄重提交"重建该段链

        public void Write(int offset, ReadOnlySpan<float> samples)
        {
            mOwner.AssertDataThread();
            samples.CopyTo(mSamples.AsSpan(offset));   // 越界即抛（契约：超出 sampleCount 非法）
            IsCommitted = false;
        }

        public void Commit()
        {
            mOwner.AssertDataThread();
            IsCommitted = true;
            CommitVersion++;
            mOwner.NotifyAudioSegmentsChanged();
        }

        public void Dispose()
        {
            mOwner.AssertDataThread();
            mOwner.RemoveAudioSegment(this);
        }

        readonly SynthesisContext mOwner;
        readonly float[] mSamples;
    }

    // —— ITiming 活实现：直接转发 TempoManager（仅数据线程使用；快照侧用 TempoSnapshot）——
    sealed class LiveTiming(SynthesisContext context, ITempoManager tempoManager) : ITiming
    {
        public double ToSecond(double tick) { context.AssertDataThread(); return tempoManager.GetTime(tick); }
        public double ToTick(double second) { context.AssertDataThread(); return tempoManager.GetTick(second); }
        public double[] ToSeconds(IReadOnlyList<double> ticks) { context.AssertDataThread(); return tempoManager.GetTimes(ticks); }
        public double[] ToTicks(IReadOnlyList<double> seconds) { context.AssertDataThread(); return tempoManager.GetTicks(seconds); }
    }

    // —— 曲线类的会话级活视图：取值经 sampler 委托（part 相对 tick 轴）回宿主数据层，
    //    秒↔tick 换算与 part 偏移由本代理在边界完成；区间事件由 context 换算成全局秒后注入。 ——
    internal sealed class AutomationProxy(SynthesisContext context, Func<IReadOnlyList<double>, double[]> sampler) : ILiveAutomation
    {
        public event Action<double, double>? RangeModified;

        // 查询轴 = 全局秒：全局秒 → 全局 tick → part 相对 tick → 喂数据层 sampler。
        public double[] Evaluate(IReadOnlyList<double> times)
        {
            context.AssertDataThread();
            double pos = context.mPart.Pos.Value;
            double[] globalTicks = context.mTiming.ToTicks(times);
            double[] relTicks = new double[globalTicks.Length];
            for (int i = 0; i < globalTicks.Length; i++)
            {
                relTicks[i] = globalTicks[i] - pos;
            }
            return sampler(relTicks);
        }

        internal void NotifyRangeModified(double startSecond, double endSecond)
        {
            context.ForwardChange(() => RangeModified?.Invoke(startSecond, endSecond));
        }
    }

    // —— note 代理集合：镜像 part.Notes（顺序即链表序，无索引承诺——SDK 面即链表形态），
    //    增删自动建/毁代理并转发结构事件。 ——
    sealed class NoteProxyList : IReadOnlyNotifiableLinkedList<ILiveNote>, IDisposable
    {
        public event Action<ILiveNote>? ItemAdded;
        public event Action<ILiveNote>? ItemRemoved;
        public event Action? Modified;

        public ILiveNote? First
        {
            get
            {
                mContext.AssertDataThread();
                var begin = mNotes.Begin;
                return begin == null ? null : ProxyOf(begin);
            }
        }

        public ILiveNote? Last
        {
            get
            {
                mContext.AssertDataThread();
                var end = mNotes.End;
                return end == null ? null : ProxyOf(end);
            }
        }

        public NoteProxyList(SynthesisContext context)
        {
            mContext = context;
            mNotes = context.mPart.Notes;
            foreach (var note in mNotes)
            {
                mProxies.Add(note, new SynthesisNoteProxy(context, note));
            }
            mNotes.ItemAdded.Subscribe(OnItemAdded, s);
            mNotes.ItemRemoved.Subscribe(OnItemRemoved, s);
            mNotes.ListModified.Subscribe(OnListModified, s);
        }

        public int Count => mNotes.Count;

        public IEnumerator<ILiveNote> GetEnumerator()
        {
            mContext.AssertDataThread();
            foreach (var note in mNotes)
            {
                yield return ProxyOf(note);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public SynthesisNoteProxy ProxyOf(INote note)
        {
            if (!mProxies.TryGetValue(note, out var proxy))
            {
                // 防御：理论上增删事件已维护全集，此处兜底补建。
                proxy = new SynthesisNoteProxy(mContext, note);
                mProxies.Add(note, proxy);
            }
            return proxy;
        }

        public void Dispose()
        {
            s.DisposeAll();
            foreach (var kvp in mProxies)
            {
                kvp.Value.Dispose();
            }
            mProxies.Clear();
        }

        void OnItemAdded(INote note)
        {
            var proxy = new SynthesisNoteProxy(mContext, note);
            mProxies[note] = proxy;
            mContext.ForwardChange(() => ItemAdded?.Invoke(proxy));
        }

        void OnItemRemoved(INote note)
        {
            if (!mProxies.Remove(note, out var proxy))
                return;

            mContext.ForwardChange(() => ItemRemoved?.Invoke(proxy));
            proxy.Dispose();
        }

        void OnListModified()
        {
            mContext.ForwardChange(() => Modified?.Invoke());
        }

        readonly SynthesisContext mContext;
        readonly INoteList mNotes;
        readonly Dictionary<INote, SynthesisNoteProxy> mProxies = new();
        readonly DisposableManager s = new();
    }

    // —— 派生只读属性：借壳一个或多个数据层源（最小订阅面）的改前/改后事件，
    //    值即时从 getter 计算；事件字段在本对象上（短命），源订阅随 Dispose 拆除。 ——
    sealed class DerivedProperty<T> : IReadOnlyNotifiableProperty<T>, IDisposable
    {
        public event Action? WillModify;
        public event Action? Modified;
        public T Value
        {
            get
            {
                mContext.AssertDataThread();
                return mGetter();
            }
        }

        public DerivedProperty(SynthesisContext context, Func<T> getter, params IReadOnlyNotifiable[] sources)
        {
            mContext = context;
            mGetter = getter;
            mSources = sources;
            mOnWillModify = () => mContext.ForwardWill(() => WillModify?.Invoke());
            mOnModified = () => mContext.ForwardChange(() => Modified?.Invoke());
            foreach (var source in mSources)
            {
                source.WillModify += mOnWillModify;
                source.Modified += mOnModified;
            }
        }

        public void Dispose()
        {
            foreach (var source in mSources)
            {
                source.WillModify -= mOnWillModify;
                source.Modified -= mOnModified;
            }
        }

        readonly SynthesisContext mContext;
        readonly Func<T> mGetter;
        readonly IReadOnlyNotifiable[] mSources;
        readonly Action mOnWillModify;
        readonly Action mOnModified;
    }

    // —— 属性树守卫：把宿主长寿 property object 包成会话级只读外观。
    //    导航逐层包裹（per key 缓存）、读值直透、事件 re-raise 到自身字段。 ——
    sealed class PropertyObjectGuard : IReadOnlyNotifiablePropertyObject, IDisposable
    {
        public event Action? WillModify;
        public event Action? Modified;

        public PropertyObjectGuard(SynthesisContext context, IReadOnlyNotifiablePropertyObject source)
        {
            mContext = context;
            mSource = source;
            mOnWillModify = () => mContext.ForwardWill(() => WillModify?.Invoke());
            mOnModified = () => mContext.ForwardChange(() => Modified?.Invoke());
            mSource.WillModify += mOnWillModify;
            mSource.Modified += mOnModified;
        }

        public IReadOnlyNotifiablePropertyObject Object(string key)
        {
            if (!mChildren.TryGetValue(key, out var child))
            {
                child = new PropertyObjectGuard(mContext, mSource.Object(key));
                mChildren.Add(key, child);
            }
            return child;
        }

        public PropertyValue GetValue(string key, PropertyValue defaultValue)
        {
            mContext.AssertDataThread();
            return mSource.GetValue(key, defaultValue);
        }

        public void Dispose()
        {
            mSource.WillModify -= mOnWillModify;
            mSource.Modified -= mOnModified;
            foreach (var kvp in mChildren)
            {
                kvp.Value.Dispose();
            }
            mChildren.Clear();
        }

        readonly SynthesisContext mContext;
        readonly IReadOnlyNotifiablePropertyObject mSource;
        readonly Action mOnWillModify;
        readonly Action mOnModified;
        readonly Dictionary<string, PropertyObjectGuard> mChildren = new();
    }

    // —— note 代理：固定字段全部以派生属性借壳数据层；边界为全局秒（ToSecond(partPos+notePos)，
    //    DerivedProperty source 含 part.Pos / TempoManager，故 tempo 变 / part 平移自动触发边界 Modified）；
    //    Lyric 取最终发音（与旧 SDK 面一致）；Phonemes 转为 pinned 约束形。 ——
    internal sealed class SynthesisNoteProxy : ILiveNote, IDisposable
    {
        public INote Source => mNote;

        public IReadOnlyNotifiableProperty<double> StartTime { get; }
        public IReadOnlyNotifiableProperty<double> EndTime { get; }
        public IReadOnlyNotifiableProperty<int> Pitch { get; }
        public IReadOnlyNotifiableProperty<string> Lyric { get; }
        public IReadOnlyNotifiableProperty<IReadOnlyList<SDK.PinnedPhoneme>> Phonemes { get; }
        public IReadOnlyNotifiablePropertyObject Properties => mProperties;

        public ILiveNote? Next => mContext.ProxyOf(mNote.Next);
        public ILiveNote? Last => mContext.ProxyOf(mNote.Last);

        public SynthesisNoteProxy(SynthesisContext context, INote note)
        {
            mContext = context;
            mNote = note;
            var part = context.mPart;
            // source 只含 note 自身字段（note 在 part 内编辑）；part.Pos/tempo 变化走 session 重建，
            // getter 读 part.Pos.Value 当前值即可（session 生命周期内稳定）。
            StartTime = Track(new DerivedProperty<double>(context, () =>
                context.mTiming.ToSecond(part.Pos.Value + note.Pos.Value),
                note.Pos));
            EndTime = Track(new DerivedProperty<double>(context, () =>
                context.mTiming.ToSecond(part.Pos.Value + note.Pos.Value + note.Dur.Value),
                note.Pos, note.Dur));
            Pitch = Track(new DerivedProperty<int>(context, () => note.Pitch.Value, note.Pitch));
            Lyric = Track(new DerivedProperty<string>(context, () => note.FinalPronunciation() ?? note.Lyric.Value, note.Lyric, note.Pronunciation));
            Phonemes = Track(new DerivedProperty<IReadOnlyList<SDK.PinnedPhoneme>>(context, () =>
            {
                var phonemes = new List<SDK.PinnedPhoneme>(note.Phonemes.Count);
                foreach (var phoneme in note.Phonemes)
                {
                    // 宿主数据层的音素时长均为用户钉死值（note 相对秒）；列表非空即整 note 钉死。
                    phonemes.Add(new SDK.PinnedPhoneme
                    {
                        Symbol = phoneme.Symbol.Value,
                        StartTime = phoneme.StartTime.Value,
                        EndTime = phoneme.EndTime.Value,
                    });
                }
                return phonemes;
            }, note.Phonemes));
            mProperties = new PropertyObjectGuard(context, note.Properties);
        }

        public void Dispose()
        {
            foreach (var disposable in mDisposables)
            {
                disposable.Dispose();
            }
            mDisposables.Clear();
            mProperties.Dispose();
        }

        DerivedProperty<T> Track<T>(DerivedProperty<T> property)
        {
            mDisposables.Add(property);
            return property;
        }

        readonly SynthesisContext mContext;
        readonly INote mNote;
        readonly PropertyObjectGuard mProperties;
        readonly List<IDisposable> mDisposables = new();
    }
}
