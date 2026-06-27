using System;
using System.Collections;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Data.Timing;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// IVoiceSynthesisContext 的宿主实现：会话级中间层，每次 CreateSession 新建、随会话死。
// 插件订阅的事件字段全在本对象/其代理对象上（短命，随会话一起回收 → 泄漏结构性不可能）；
// 本对象内部订阅长寿数据层（part/note/automation/tempo），由宿主在数据线程转发——
// 借壳数据层最小订阅面（IReadOnlyNotifiable），天然只转发已提交的真实变更（merge 中间态不外漏）。
//
// 坐标系约定（SDK 面）：tick/秒均为全局工程轴（与音频产物、状态段同一时间系）；
// 宿主数据层的 part 相对量在本层完成偏移换算。
internal sealed class VoiceSynthesisContext : IVoiceSynthesisContext, ISynthesisForwarder, IAudioSegmentHost, IAudioSegmentOwner, IDisposable
{
    public MidiPart Part => mPart;

    public string VoiceId => mVoiceId;
    public IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> Notes => mNotes;
    public IReadOnlyNotifiablePropertyObject PartProperties => mPartProperties;
    public ISynthesisAutomation Pitch => mPitch;
    public ISynthesisAutomation PitchDeviation => mPitchDeviation;

    public event Action? Committed;

    public VoiceSynthesisContext(MidiPart part, string voiceId)
    {
        mPart = part;
        mVoiceId = voiceId;
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
    public VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes, double startTime, double endTime)
    {
        AssertDataThread();
        return VoiceSynthesisSnapshotFactory.Capture(mPart, notes, startTime, endTime);
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

    // 宿主侧读取面（IAudioSegmentHost，供 EffectGraph 消费）：已交付音频的段供 effect 链按段过。
    public IReadOnlyList<AudioSegment> AudioSegments => mAudioSegments;

    // 段集 / 段内容变化（Commit 或 Dispose）→ 通知效果图 reconcile（变了哪段据 AudioSegments + CommitVersion 算）。
    public event Action? AudioSegmentsChanged;

    // IAudioSegmentOwner：段握柄回调（线程断言转发给 DEBUG 期的 AssertDataThread，Release 下空转）。
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

    // 已声明 automation 轨只读 map：轨集 = 当前音源引擎声明的 AutomationConfigs（孤儿 / 他引擎数据不在内）；
    // 代理按 key 缓存（保订阅稳定）、缺则补建；每次读重建 map 以反映当前声明集。
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
                    proxy = new AutomationProxy(this, ticks => mPart.GetFinalAutomationValues(ticks, key));
                    mAutomationProxies.Add(key, proxy);
                }
                map.Add(key, proxy);
            }
            return map;
        }
    }

    // 数据线程纪律断言（DEBUG）：活视图仅数据线程可用是纪律性约束，类型上无法强制——
    // 这里让违例插件在开发期第一次跨线程访问就炸响，而非静默数据竞争。v2 进程隔离后物理强制。
    [System.Diagnostics.Conditional("DEBUG")]
    internal void AssertDataThread()
    {
        if (System.Environment.CurrentManagedThreadId != mDataThreadId)
            throw new InvalidOperationException("Synthesis live view (IVoiceSynthesisContext and its proxies) must only be accessed on the data thread; synthesize against the immutable VoiceSynthesisSnapshot instead.");
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

    // —— 转发旋钮（ISynthesisForwarder；线程/时机/故障隔离/批量都收在这里）——

    // 数据线程断言转发：AssertDataThread 是 [Conditional("DEBUG")]，故 Release 下本方法体内的调用被消除、空转。
    public void AssertThread() => AssertDataThread();

    // 全局秒 → part 相对 tick（AutomationProxy 求值用）：全局秒 → 全局 tick → 减 part 偏移。
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

    // 改后类通知的统一转发口：变更通知发完后补一个 Committed 收口（不在显式批量中时给单条编辑也补，
    // 契约"每个逻辑编辑都收口一次"的单条情形；批量中则由 OnBatchEnd 在批量结束统一收口）。try-catch 隔离插件故障。
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

    // 改前事件直转（不入括号）：插件在 handler 内读旧值做廉价记录，重活留到 BatchEnd。
    public void ForwardWill(Action raise)
    {
        if (mDisposed)
            return;

        Guarded(raise);
    }

    internal VoiceNoteProxy? ProxyOf(INote? note) => note == null ? null : mNotes.ProxyOf(note);

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
        // 颤音存在性变化本身即偏移变化：新增后该覆盖区间多了一份偏移源，须主动标脏。
        // 颤音的 RangeModified 只在其属性 merge 提交后发，纯增删不会自发 → 这里补发一次，
        // 否则只增不改的颤音不会重合成（删除路径对称处理见 UnwireVibrato）。
        NotifyDeviationRangeModified(vibrato.StartPos(), vibrato.EndPos());
    }

    void UnwireVibrato(Vibrato vibrato)
    {
        if (mVibratoSubscriptions.Remove(vibrato, out var subscriptions))
            subscriptions.DisposeAll();
        // 被删颤音原覆盖区间的偏移随之消失，须标脏让该段重算（此时 Detach 仅断链表指针，
        // Pos/Dur 值仍可读）。链表 Remove 不发区间事件、其 RangeModified 订阅亦已拆，故须在此补发。
        NotifyDeviationRangeModified(vibrato.StartPos(), vibrato.EndPos());
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
    readonly string mVoiceId;
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

    // 音频段握柄已提为共享类（见 AudioSegment.cs / IAudioSegmentOwner）：voice / instrument context 共用、
    // EffectGraph 统一消费。本类作为 IAudioSegmentOwner 提供其线程断言 / 摘除 / 通知回调。

    // —— ITiming 活实现：直接转发 TempoManager（仅数据线程使用；快照侧用 TempoSnapshot）——
    sealed class LiveTiming(VoiceSynthesisContext context, ITempoManager tempoManager) : ITiming
    {
        public double ToSecond(double tick) { context.AssertDataThread(); return tempoManager.GetTime(tick); }
        public double ToTick(double second) { context.AssertDataThread(); return tempoManager.GetTick(second); }
        public double[] ToSeconds(IReadOnlyList<double> ticks) { context.AssertDataThread(); return tempoManager.GetTimes(ticks); }
        public double[] ToTicks(IReadOnlyList<double> seconds) { context.AssertDataThread(); return tempoManager.GetTicks(seconds); }
    }

    // AutomationProxy / DerivedProperty / PropertyObjectGuard 已提为共享代理（见 SynthesisProxies.cs），
    // 经 ISynthesisForwarder 复用于 voice / instrument 两 context。

    // —— note 代理集合：镜像 part.Notes（顺序即链表序，无索引承诺——SDK 面即链表形态），
    //    增删自动建/毁代理并转发结构事件。 ——
    sealed class NoteProxyList : IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote>, IDisposable
    {
        public event Action<IVoiceSynthesisNote>? ItemAdded;
        public event Action<IVoiceSynthesisNote>? ItemRemoved;
        public event Action? Modified;

        public IVoiceSynthesisNote? First
        {
            get
            {
                mContext.AssertDataThread();
                var begin = mNotes.Begin;
                return begin == null ? null : ProxyOf(begin);
            }
        }

        public IVoiceSynthesisNote? Last
        {
            get
            {
                mContext.AssertDataThread();
                var end = mNotes.End;
                return end == null ? null : ProxyOf(end);
            }
        }

        public NoteProxyList(VoiceSynthesisContext context)
        {
            mContext = context;
            mNotes = context.mPart.Notes;
            foreach (var note in mNotes)
            {
                mProxies.Add(note, new VoiceNoteProxy(context, note));
            }
            mNotes.ItemAdded.Subscribe(OnItemAdded, s);
            mNotes.ItemRemoved.Subscribe(OnItemRemoved, s);
            mNotes.ListModified.Subscribe(OnListModified, s);
        }

        public int Count => mNotes.Count;

        public IEnumerator<IVoiceSynthesisNote> GetEnumerator()
        {
            mContext.AssertDataThread();
            foreach (var note in mNotes)
            {
                yield return ProxyOf(note);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public VoiceNoteProxy ProxyOf(INote note)
        {
            if (!mProxies.TryGetValue(note, out var proxy))
            {
                // 防御：理论上增删事件已维护全集，此处兜底补建。
                proxy = new VoiceNoteProxy(mContext, note);
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
            var proxy = new VoiceNoteProxy(mContext, note);
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

        readonly VoiceSynthesisContext mContext;
        readonly INoteList mNotes;
        readonly Dictionary<INote, VoiceNoteProxy> mProxies = new();
        readonly DisposableManager s = new();
    }

    // —— note 代理：固定字段全部以派生属性借壳数据层；边界为全局秒（ToSecond(partPos+notePos)，
    //    DerivedProperty source 含 part.Pos / TempoManager，故 tempo 变 / part 平移自动触发边界 Modified）；
    //    Lyric 取最终发音（与旧 SDK 面一致）；Phonemes 转为 pinned 约束形。 ——
    internal sealed class VoiceNoteProxy : IVoiceSynthesisNote, IDisposable
    {
        public INote Source => mNote;

        public IReadOnlyNotifiableProperty<double> StartTime { get; }
        public IReadOnlyNotifiableProperty<double> EndTime { get; }
        public IReadOnlyNotifiableProperty<int> Pitch { get; }
        public IReadOnlyNotifiableProperty<string> Lyric { get; }
        public IReadOnlyNotifiableProperty<IReadOnlyList<SDK.SynthesizedPhoneme>> Phonemes { get; }
        // 生效延续：普通只读、读时按相接链求值（数据线程，O(链长)）。无独立通知——其输入 Lyric/位置本就可订阅。
        public bool IsContinuation => mNote.IsEffectiveContinuation();
        public IReadOnlyNotifiablePropertyObject Properties => mProperties;

        public IVoiceSynthesisNote? Next => mContext.ProxyOf(mNote.Next);
        public IVoiceSynthesisNote? Last => mContext.ProxyOf(mNote.Last);

        public VoiceNoteProxy(VoiceSynthesisContext context, INote note)
        {
            mContext = context;
            mNote = note;
            var part = context.mPart;
            // source 只含 note 自身字段（note 在 part 内编辑）；part.Pos/tempo 变化走 session 重建，
            // getter 读 part.Pos.Value 当前值即可（session 生命周期内稳定）。
            StartTime = Track(new DerivedProperty<double>(context, () =>
                context.mTiming.ToSecond(part.Pos.Value + note.Pos.Value),
                note.Pos));
            // 有效结束（去重叠后盖前，非破坏）：voice 单声部约束下钳到下一 note 起点（见 INote.EffectiveEndPos）。
            // 这是喂插件的「单声部音频」口径——去重叠责任在宿主，插件直接拿不重叠的 note。
            // 音素布局（定位 / 跨 note 压缩 / melisma）也由宿主独占，插件只见有效末，不再暴露 note 满末。
            // 依赖含下一 note 的 Pos，但源只挂 note 自身 Pos/Dur：重叠的相邻 note 必同处一个分块，
            // 邻居移动经其自身变更已标脏该块并重分块，故有效结束的变化由块级重渲覆盖，无需在此追踪邻居（且 Next 身份会变）。
            EndTime = Track(new DerivedProperty<double>(context, () =>
                context.mTiming.ToSecond(part.Pos.Value + note.EffectiveEndPos()),
                note.Pos, note.Dur));
            Pitch = Track(new DerivedProperty<int>(context, () => note.Pitch.Value, note.Pitch));
            Lyric = Track(new DerivedProperty<string>(context, () => note.FinalPronunciation() ?? note.Lyric.Value, note.Lyric, note.Pronunciation));
            // 钉死音素的「时长 + 权重」（与工程存储同形）：位置 / 去重叠 / 跨 note 压缩由插件按时长 + note 几何自行派生
            // （布局算法不在 SDK，插件按需照抄参考实现）。时长不随 note 伸缩改变，
            // 故只依赖 note.Phonemes；note resize 经 StartTime/EndTime 另行触发重合成。
            Phonemes = Track(new DerivedProperty<IReadOnlyList<SDK.SynthesizedPhoneme>>(context, () =>
            {
                var phonemes = new List<SDK.SynthesizedPhoneme>(note.Phonemes.Count);
                foreach (var p in note.Phonemes)
                {
                    phonemes.Add(new SDK.SynthesizedPhoneme
                    {
                        Symbol = p.Symbol.Value,
                        Duration = p.Duration.Value,
                        StretchWeight = p.StretchWeight.Value,
                        IsLead = p.IsLead.Value,
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

        readonly VoiceSynthesisContext mContext;
        readonly INote mNote;
        readonly PropertyObjectGuard mProperties;
        readonly List<IDisposable> mDisposables = new();
    }
}
