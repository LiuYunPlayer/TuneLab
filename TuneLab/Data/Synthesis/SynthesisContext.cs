using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;
using TuneLab.Primitives.Event;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.Timing;
using TuneLab.SDK.Voice;
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

    public IReadOnlyNotifiableList<ISynthesisNote> Notes => mNotes;
    public IReadOnlyNotifiablePropertyObject PartProperties => mPartProperties;
    public ISynthesisAutomation Pitch => mPitch;
    public ISynthesisAutomation PitchDeviation => mPitchDeviation;
    public ITiming Timing => mTiming;

    public event Action? TimingModified;
    public event Action? BatchBegin;
    public event Action? BatchEnd;

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
        mBatchSignal.BatchBegin += OnBatchBegin;
        mBatchSignal.BatchEnd += OnBatchEnd;

        // 时基变了：tempo 表变更，以及 part 平移（全部 Position 派生随之失效），引擎通常全量重排。
        part.TempoManager.Modified.Subscribe(NotifyTimingModified, s);
        part.Pos.Modified.Subscribe(NotifyTimingModified, s);

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
    public ISynthesisSnapshot GetSnapshot(IReadOnlyList<ISynthesisNote> notes, double startTick, double endTick)
    {
        AssertDataThread();
        return SynthesisSnapshotFactory.Capture(mPart, notes, startTick, endTick);
    }

    public bool TryGetAutomation(string key, [MaybeNullWhen(false)] out ISynthesisAutomation automation)
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
            throw new InvalidOperationException("Synthesis live view (ISynthesisContext and its proxies) must only be accessed on the data thread; synthesize against the immutable ISynthesisSnapshot instead.");
    }

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        s.DisposeAll();
        mBatchSignal.BatchBegin -= OnBatchBegin;
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
    }

    // —— 转发旋钮（线程/时机/故障隔离/批量都收在这里）——

    // 改后类通知的统一转发口：不在显式批量作用域时给单条变更自动补一对退化括号
    // （契约"每个逻辑编辑都包在括号里"的单条情形），并 try-catch 隔离插件 handler 故障。
    internal void ForwardChange(Action raise)
    {
        if (mDisposed)
            return;

        if (mBatchSignal.IsBatching)
        {
            Guarded(raise);
            return;
        }

        Guarded(() => BatchBegin?.Invoke());
        try
        {
            Guarded(raise);
        }
        finally
        {
            Guarded(() => BatchEnd?.Invoke());
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

    void OnBatchBegin() => Guarded(() => BatchBegin?.Invoke());
    void OnBatchEnd() => Guarded(() => BatchEnd?.Invoke());

    void NotifyTimingModified()
    {
        ForwardChange(() => TimingModified?.Invoke());
    }

    void NotifyPitchRangeModified(double start, double end)
    {
        double pos = mPart.Pos.Value;
        mPitch.NotifyRangeModified(pos + start, pos + end);
    }

    void NotifyDeviationRangeModified(double start, double end)
    {
        double pos = mPart.Pos.Value;
        mPitchDeviation.NotifyRangeModified(pos + start, pos + end);
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
            double pos = mPart.Pos.Value;
            NotifyAutomationRangeModified(key, pos + start, pos + end);
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

    void NotifyAutomationRangeModified(string key, double startTick, double endTick)
    {
        // 包络轨影响 vibrato 偏移：偏差通道（及受 vibrato 影响的轨）随之失效。
        // v1 按主要影响面转发到 PitchDeviation；受影响 automation 轨的精确失效缓后。
        if (key == ConstantDefine.VibratoEnvelopeID)
            mPitchDeviation.NotifyRangeModified(startTick, endTick);

        if (mAutomationProxies.TryGetValue(key, out var proxy))
            proxy.NotifyRangeModified(startTick, endTick);
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
    bool mDisposed;

    // —— ITiming 活实现：直接转发 TempoManager（仅数据线程使用；快照侧用 TempoSnapshot）——
    sealed class LiveTiming(SynthesisContext context, ITempoManager tempoManager) : ITiming
    {
        public double ToSeconds(double tick) { context.AssertDataThread(); return tempoManager.GetTime(tick); }
        public double ToTick(double seconds) { context.AssertDataThread(); return tempoManager.GetTick(seconds); }
        public double[] ToSeconds(IReadOnlyList<double> ticks) { context.AssertDataThread(); return tempoManager.GetTimes(ticks); }
        public double[] ToTick(IReadOnlyList<double> seconds) { context.AssertDataThread(); return tempoManager.GetTicks(seconds); }
    }

    // —— 曲线类的会话级活视图：取值经 sampler 委托（part 相对 tick 轴）回宿主数据层，
    //    区间事件由 context 集中换算成全局 tick 后注入。 ——
    internal sealed class AutomationProxy(SynthesisContext context, Func<IReadOnlyList<double>, double[]> sampler) : ISynthesisAutomation
    {
        public event Action<double, double>? RangeModified;

        public double[] GetValue(IReadOnlyList<double> times)
        {
            context.AssertDataThread();
            double pos = context.mPart.Pos.Value;
            double[] ticks = new double[times.Count];
            for (int i = 0; i < times.Count; i++)
            {
                ticks[i] = times[i] - pos;
            }
            return sampler(ticks);
        }

        internal void NotifyRangeModified(double startTick, double endTick)
        {
            context.ForwardChange(() => RangeModified?.Invoke(startTick, endTick));
        }
    }

    // —— note 代理列表：镜像 part.Notes（顺序即链表序），增删自动建/毁代理并转发列表事件。 ——
    sealed class NoteProxyList : IReadOnlyNotifiableList<ISynthesisNote>, IDisposable
    {
        public event Action<ISynthesisNote>? ItemAdded;
        public event Action<ISynthesisNote>? ItemRemoved;
        public event Action? Modified;

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
        public ISynthesisNote this[int index]
        {
            get
            {
                mContext.AssertDataThread();
                // 链表无随机访问：按序步进（插件按索引随机访问的场景少，常规消费是枚举/订阅）。
                int i = 0;
                foreach (var note in mNotes)
                {
                    if (i++ == index)
                        return ProxyOf(note);
                }
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<ISynthesisNote> GetEnumerator()
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

    // —— note 代理：固定字段全部以派生属性借壳数据层；Position 双域即时解析；
    //    Lyric 取最终发音（与旧 SDK 面一致）；Phonemes 转为 pinned 约束形。 ——
    internal sealed class SynthesisNoteProxy : ISynthesisNote, IDisposable
    {
        public INote Source => mNote;

        public IReadOnlyNotifiableProperty<Position> StartPosition { get; }
        public IReadOnlyNotifiableProperty<Position> EndPosition { get; }
        public IReadOnlyNotifiableProperty<int> Pitch { get; }
        public IReadOnlyNotifiableProperty<string> Lyric { get; }
        public IReadOnlyNotifiableProperty<IReadOnlyList<SDK.Voice.PhonemeInfo>> Phonemes { get; }
        public IReadOnlyNotifiablePropertyObject Properties => mProperties;

        public ISynthesisNote? Next => mContext.ProxyOf(mNote.Next);
        public ISynthesisNote? Last => mContext.ProxyOf(mNote.Last);

        public SynthesisNoteProxy(SynthesisContext context, INote note)
        {
            mContext = context;
            mNote = note;
            var part = context.mPart;
            StartPosition = Track(new DerivedProperty<Position>(context, () =>
            {
                double tick = part.Pos.Value + note.Pos.Value;
                return new Position(tick, part.TempoManager.GetTime(tick));
            }, note.Pos));
            EndPosition = Track(new DerivedProperty<Position>(context, () =>
            {
                double tick = part.Pos.Value + note.Pos.Value + note.Dur.Value;
                return new Position(tick, part.TempoManager.GetTime(tick));
            }, note.Pos, note.Dur));
            Pitch = Track(new DerivedProperty<int>(context, () => note.Pitch.Value, note.Pitch));
            Lyric = Track(new DerivedProperty<string>(context, () => note.FinalPronunciation() ?? note.Lyric.Value, note.Lyric, note.Pronunciation));
            Phonemes = Track(new DerivedProperty<IReadOnlyList<SDK.Voice.PhonemeInfo>>(context, () =>
            {
                var phonemes = new List<SDK.Voice.PhonemeInfo>(note.Phonemes.Count);
                foreach (var phoneme in note.Phonemes)
                {
                    // 宿主数据层的音素时长均为用户钉死值（note 相对秒）；列表非空即整 note 钉死。
                    phonemes.Add(new SDK.Voice.PhonemeInfo
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
