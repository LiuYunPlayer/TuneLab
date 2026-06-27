using System;
using System.Collections;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Data.Timing;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// IInstrumentSynthesisContext 的宿主实现：会话级中间层，每次 CreateSession 新建、随会话死。
// 与 voice 的 VoiceSynthesisContext 同构但精简——instrument 无 pitch/vibrato 双音高通道、note 取满末（不去重叠）、
// 无 Lyric/Phonemes。领域中性的代理（DerivedProperty/PropertyObjectGuard/AutomationProxy）经 ISynthesisForwarder 复用；
// 音频段机制（AudioSegment/IAudioSegmentHost/Owner）与 EffectGraph 与 voice 共用。
//
// 坐标系约定：tick/秒均为全局工程轴；宿主数据层的 part 相对量在本层完成偏移换算。
internal sealed class InstrumentSynthesisContext : IInstrumentSynthesisContext, ISynthesisForwarder, IAudioSegmentHost, IAudioSegmentOwner, IDisposable
{
    public MidiPart Part => mPart;

    public string InstrumentId => mInstrumentId;
    public IReadOnlyNotifiableLinkedList<IInstrumentSynthesisNote> Notes => mNotes;
    public IReadOnlyNotifiablePropertyObject PartProperties => mPartProperties;

    public event Action? Committed;

    public InstrumentSynthesisContext(MidiPart part, string instrumentId)
    {
        mPart = part;
        mInstrumentId = instrumentId;
        mDataThreadId = System.Environment.CurrentManagedThreadId;
        mTiming = new LiveTiming(this, part.TempoManager);
        mNotes = new NoteProxyList(this);
        mPartProperties = new PropertyObjectGuard(this, part.Properties);

        mBatchSignal = part.SynthesisBatch;
        mBatchSignal.BatchEnd += OnBatchEnd;

        // 时基变化（tempo 表 / part 平移）走 session 整体重建，不在此做增量通知（同 voice）。

        // automation 区间失效：已存在的轨逐条接线，此后增删动态接/拆线。instrument 无 pitch/vibrato 通道。
        foreach (var kvp in part.Automations)
        {
            WireAutomation(kvp.Key, kvp.Value);
        }
        var automationMap = (IReadOnlyDataMap<string, IAutomation>)part.Automations;
        automationMap.ItemAdded.Subscribe(WireAutomation, s);
        automationMap.ItemRemoved.Subscribe(UnwireAutomation, s);
    }

    // 快照物化（插件在 SynthesizeNext 同步前缀主动拉取）：满末口径、无双音高通道。
    public InstrumentSynthesisSnapshot GetSnapshot(IReadOnlyList<IInstrumentSynthesisNote> notes, double startTime, double endTime)
    {
        AssertDataThread();
        return InstrumentSynthesisSnapshotFactory.Capture(mPart, notes, startTime, endTime);
    }

    public IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate)
    {
        AssertDataThread();
        var segment = new AudioSegment(this, sampleOffset, sampleCount, sampleRate);
        if (!mDisposed)
            mAudioSegments.Add(segment);
        return segment;
    }

    // —— IAudioSegmentHost（供 EffectGraph 消费）——
    public IReadOnlyList<AudioSegment> AudioSegments => mAudioSegments;
    public event Action? AudioSegmentsChanged;

    // —— IAudioSegmentOwner（段握柄回调）——
    public void AssertSegmentThread() => AssertDataThread();

    public void RemoveAudioSegment(AudioSegment segment)
    {
        mAudioSegments.Remove(segment);
        NotifyAudioSegmentsChanged();
    }

    public void NotifyAudioSegmentsChanged()
    {
        if (!mDisposed)
            AudioSegmentsChanged?.Invoke();
    }

    // 已声明 automation 轨只读 map：轨集 = 当前音源引擎声明的 AutomationConfigs；代理按 key 缓存、缺则补建；
    // 每次读重建 map 以反映当前声明集。原始曲线（无 vibrato 偏移）：instrument 无颤音语义。
    public IReadOnlyMap<string, ISynthesisAutomation> Automations
    {
        get
        {
            AssertDataThread();
            var map = new Map<string, ISynthesisAutomation>();
            foreach (var kvp in mPart.SoundSource.AutomationConfigs)
            {
                string key = kvp.Key.Id;
                if (!mAutomationProxies.TryGetValue(key, out var proxy))
                {
                    proxy = new AutomationProxy(this, ticks => mPart.GetAutomationValues(ticks, key));
                    mAutomationProxies.Add(key, proxy);
                }
                map.Add(key, proxy);
            }
            return map;
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    internal void AssertDataThread()
    {
        if (System.Environment.CurrentManagedThreadId != mDataThreadId)
            throw new InvalidOperationException("Synthesis live view (IInstrumentSynthesisContext and its proxies) must only be accessed on the data thread; synthesize against the immutable InstrumentSynthesisSnapshot instead.");
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
        mNotes.Dispose();
        mPartProperties.Dispose();
        mAudioSegments.Clear();
    }

    // —— ISynthesisForwarder（线程/时机/故障隔离/批量）——
    public void AssertThread() => AssertDataThread();

    public double[] ToRelativeTicks(IReadOnlyList<double> times)
    {
        AssertDataThread();
        double pos = mPart.Pos.Value;
        double[] globalTicks = mTiming.ToTicks(times);
        double[] relTicks = new double[globalTicks.Length];
        for (int i = 0; i < globalTicks.Length; i++)
        {
            relTicks[i] = globalTicks[i] - pos;
        }
        return relTicks;
    }

    public void ForwardChange(Action raise)
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

    public void ForwardWill(Action raise)
    {
        if (mDisposed)
            return;

        Guarded(raise);
    }

    internal InstrumentNoteProxy? ProxyOf(INote? note) => note == null ? null : mNotes.ProxyOf(note);

    static void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Instrument synthesis context handler threw: " + ex);
        }
    }

    void OnBatchEnd() => Guarded(() => Committed?.Invoke());

    double ToGlobalSecond(double relTick)
    {
        if (double.IsInfinity(relTick))
            return relTick;
        return mTiming.ToSecond(mPart.Pos.Value + relTick);
    }

    void WireAutomation(string key, IAutomation automation)
    {
        if (mAutomationSubscriptions.ContainsKey(key))
            return;

        var subscriptions = new DisposableManager();
        if (key == ConstantDefine.VolumeID)
        {
            // 音量在宿主混音阶段应用、不进合成，变更不产生合成失效（与 voice 一致）。
            mAutomationSubscriptions.Add(key, subscriptions);
            return;
        }

        automation.RangeModified.Subscribe((start, end) =>
        {
            NotifyAutomationRangeModified(key, start, end);
        }, subscriptions);
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

    void NotifyAutomationRangeModified(string key, double relStart, double relEnd)
    {
        if (mAutomationProxies.TryGetValue(key, out var proxy))
            proxy.NotifyRangeModified(ToGlobalSecond(relStart), ToGlobalSecond(relEnd));
    }

    readonly MidiPart mPart;
    readonly string mInstrumentId;
    readonly int mDataThreadId;
    readonly BatchSignal mBatchSignal;
    readonly LiveTiming mTiming;
    readonly NoteProxyList mNotes;
    readonly PropertyObjectGuard mPartProperties;
    readonly Dictionary<string, AutomationProxy> mAutomationProxies = new();
    readonly Dictionary<string, DisposableManager> mAutomationSubscriptions = new();
    readonly DisposableManager s = new();
    readonly List<AudioSegment> mAudioSegments = new();
    bool mDisposed;

    // —— ITiming 活实现：直接转发 TempoManager（仅数据线程使用；快照侧用 TempoSnapshot）——
    sealed class LiveTiming(InstrumentSynthesisContext context, ITempoManager tempoManager) : ITiming
    {
        public double ToSecond(double tick) { context.AssertDataThread(); return tempoManager.GetTime(tick); }
        public double ToTick(double second) { context.AssertDataThread(); return tempoManager.GetTick(second); }
        public double[] ToSeconds(IReadOnlyList<double> ticks) { context.AssertDataThread(); return tempoManager.GetTimes(ticks); }
        public double[] ToTicks(IReadOnlyList<double> seconds) { context.AssertDataThread(); return tempoManager.GetTicks(seconds); }
    }

    // —— note 代理集合：镜像 part.Notes（链表序、无索引承诺），增删自动建/毁代理并转发结构事件。 ——
    sealed class NoteProxyList : IReadOnlyNotifiableLinkedList<IInstrumentSynthesisNote>, IDisposable
    {
        public IActionEvent<IInstrumentSynthesisNote> ItemAdded => mItemAdded;
        public IActionEvent<IInstrumentSynthesisNote> ItemRemoved => mItemRemoved;
        public IActionEvent StructureModified => mStructureModified;
        public IEnumerable<IInstrumentSynthesisNote> Items => this;

        readonly ActionEvent<IInstrumentSynthesisNote> mItemAdded = new();
        readonly ActionEvent<IInstrumentSynthesisNote> mItemRemoved = new();
        readonly ActionEvent mStructureModified = new();

        public IInstrumentSynthesisNote? First
        {
            get
            {
                mContext.AssertDataThread();
                var first = mNotes.First;
                return first == null ? null : ProxyOf(first);
            }
        }

        public IInstrumentSynthesisNote? Last
        {
            get
            {
                mContext.AssertDataThread();
                var last = mNotes.Last;
                return last == null ? null : ProxyOf(last);
            }
        }

        public NoteProxyList(InstrumentSynthesisContext context)
        {
            mContext = context;
            mNotes = context.mPart.Notes;
            foreach (var note in mNotes)
            {
                mProxies.Add(note, new InstrumentNoteProxy(context, note));
            }
            mNotes.ItemAdded.Subscribe(OnItemAdded, s);
            mNotes.ItemRemoved.Subscribe(OnItemRemoved, s);
            mNotes.StructureModified.Subscribe(OnStructureModified, s);
        }

        public int Count => mNotes.Count;

        public IEnumerator<IInstrumentSynthesisNote> GetEnumerator()
        {
            mContext.AssertDataThread();
            foreach (var note in mNotes)
            {
                yield return ProxyOf(note);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public InstrumentNoteProxy ProxyOf(INote note)
        {
            if (!mProxies.TryGetValue(note, out var proxy))
            {
                proxy = new InstrumentNoteProxy(mContext, note);
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
            var proxy = new InstrumentNoteProxy(mContext, note);
            mProxies[note] = proxy;
            mContext.ForwardChange(() => mItemAdded.Invoke(proxy));
        }

        void OnItemRemoved(INote note)
        {
            if (!mProxies.Remove(note, out var proxy))
                return;

            mContext.ForwardChange(() => mItemRemoved.Invoke(proxy));
            proxy.Dispose();
        }

        void OnStructureModified()
        {
            mContext.ForwardChange(() => mStructureModified.Invoke());
        }

        readonly InstrumentSynthesisContext mContext;
        readonly INoteList mNotes;
        readonly Dictionary<INote, InstrumentNoteProxy> mProxies = new();
        readonly DisposableManager s = new();
    }

    // —— note 代理：满末（Pos+Dur，不去重叠）、无 Lyric/Phonemes；边界为全局秒。 ——
    internal sealed class InstrumentNoteProxy : IInstrumentSynthesisNote, IDisposable
    {
        public INote Source => mNote;

        public IReadOnlyNotifiableProperty<double> StartTime { get; }
        public IReadOnlyNotifiableProperty<double> EndTime { get; }
        public IReadOnlyNotifiableProperty<int> Pitch { get; }
        public IReadOnlyNotifiablePropertyObject Properties => mProperties;

        public IInstrumentSynthesisNote? Next => mContext.ProxyOf(mNote.Next);
        public IInstrumentSynthesisNote? Last => mContext.ProxyOf(mNote.Last);

        public InstrumentNoteProxy(InstrumentSynthesisContext context, INote note)
        {
            mContext = context;
            mNote = note;
            var part = context.mPart;
            StartTime = Track(new DerivedProperty<double>(context, () =>
                context.mTiming.ToSecond(part.Pos.Value + note.Pos.Value),
                note.Pos));
            // 满末：Pos+Dur 换算秒，不钳到邻居起点——instrument 原味消费重叠几何（和弦 / 多声部）。
            EndTime = Track(new DerivedProperty<double>(context, () =>
                context.mTiming.ToSecond(part.Pos.Value + note.Pos.Value + note.Dur.Value),
                note.Pos, note.Dur));
            Pitch = Track(new DerivedProperty<int>(context, () => note.Pitch.Value, note.Pitch));
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

        readonly InstrumentSynthesisContext mContext;
        readonly INote mNote;
        readonly PropertyObjectGuard mProperties;
        readonly List<IDisposable> mDisposables = new();
    }
}
