using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Hosting.Compat.Legacy.Conversion;
using TuneLab.Primitives.DataStructures;
using TuneLab.Primitives.Event;
using LProp = TuneLab.Base.Properties;
using LVoice = TuneLab.Extensions.Voices;
using VBase = TuneLab.SDK.Base;
using VConfig = TuneLab.SDK.Base.ControllerConfigs;
using VVoice = TuneLab.SDK.Voice;
using PStruct = TuneLab.Primitives.DataStructures;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 IVoiceSource + ISynthesisTask（task 模型）适配成 V1 ISynthesisSession（会话模型）：
// compat 层内置一个"迷你宿主"——分片账本（老 Segment() 决定分片）、脏判定（懒策略：
// 订阅 context 全部变更通知按块标脏、BatchEnd 重分片并按 note 集等价保留未变块的缓存）、
// 产物聚合（多块音频拼成单一时间线、音素扁平化归出身、状态带按块平铺）。
//
// 线程纪律：除老 task 的回调（任意线程，一律 Post 回数据线程再落账）外，全部成员仅数据线程访问。
internal sealed class LegacySessionAdapter : VVoice.ISynthesisSession
{
    public LegacySessionAdapter(LVoice.IVoiceSource source, VVoice.ISynthesisContext context)
    {
        mSource = source;
        mContext = context;
        mSync = SynchronizationContext.Current ?? throw new InvalidOperationException("LegacySessionAdapter must be created on the data thread.");
        mNoteViewCache = new LiveNoteViewCache(ReadNoteProperties);

        mAutomationConfigs = source.AutomationConfigs.ToV1AutomationMap();
        mPartProperties = source.PartProperties.ToV1ConfigMap();
        mNoteProperties = source.NoteProperties.ToV1ConfigMap();

        // —— 懒脏策略（设计许可的最粗粒度实现的细化版）：任何 note 字段/增删 → 标脏所在块 +
        //    待重分片；曲线区间变更 → 标脏相交块；时基/part 属性 → 全部标脏重分片。 ——
        mNotesSubscription = mContext.Notes.WhenAny(SubscribeNote, UnsubscribeNote);
        mContext.Notes.ItemAdded += OnNotesStructureChanged;
        mContext.Notes.ItemRemoved += OnNotesStructureChanged;
        mContext.PartProperties.Modified += OnPartPropertiesModified;
        mContext.TimingModified += OnTimingModified;
        mContext.BatchEnd += OnBatchEnd;
        mContext.Pitch.RangeModified += OnRangeModified;
        mContext.PitchDeviation.RangeModified += OnRangeModified;   // 老 Pitch 含偏差，偏差变化同样标脏
        foreach (var key in mAutomationConfigs.Keys)
        {
            if (mContext.TryGetAutomation(key, out var automation))
            {
                automation.RangeModified += OnRangeModified;
                mSubscribedAutomations.Add(automation);
            }
        }

        mNeedReSegment = true;
    }

    // —— 声明（老声源声明是静态的：缓存转换、函数式返回）——
    public string DefaultLyric => mSource.DefaultLyric;
    public PStruct.IReadOnlyOrderedMap<string, VConfig.AutomationConfig> GetAutomationConfigs() => mAutomationConfigs;
    public PStruct.IReadOnlyOrderedMap<string, VConfig.PiecewiseAutomationConfig> GetPiecewiseAutomationConfigs() => mPiecewiseAutomationConfigs;
    public VConfig.ObjectConfig GetPartConfig(VVoice.IPropertyContext context) => new() { Properties = mPartProperties };
    public VConfig.ObjectConfig GetNoteConfig(VVoice.IPropertyContext context) => new() { Properties = mNoteProperties };

    // —— 调度 ——
    public VVoice.SynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        return FindNextDirtyPiece(startTime, endTime) is { } piece
            ? new VVoice.SynthesisSegment(piece.StartTime, piece.EndTime)
            : null;
    }

    // peek 与 commit 共用同一查找（确定性 + 同调度 tick 无编辑 ⇒ commit 重算得到 peek 报出的同一块）。
    Piece? FindNextDirtyPiece(double startTime, double endTime)
    {
        EnsureSegmented();
        foreach (var piece in mPieces)
        {
            if (!piece.Dirty || piece.Failed || piece.Synthesizing)
                continue;

            if (piece.EndTime < startTime || piece.StartTime > endTime)
                continue;

            return piece;
        }
        return null;
    }

    public async Task SynthesizeNext(VVoice.SynthesisSegment segment,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (FindNextDirtyPiece(segment.StartTime, segment.EndTime) is not { } piece)
            return;

        // 同步前缀拉取快照：automation 开窗按 note 范围外扩 4 拍余量（老引擎常对块边界外略作采样）。
        const double windowMarginTicks = 4 * 480;
        var snapshot = mContext.GetSnapshot(
            piece.Notes,
            piece.Notes.First().StartPosition.Value.Tick - windowMarginTicks,
            piece.Notes.Last().EndPosition.Value.Tick + windowMarginTicks);
        var views = SnapshotNoteView.CreateChain(snapshot.Notes, piece.Notes);
        var data = new SnapshotSynthesisData(snapshot, views);

        LVoice.ISynthesisTask task;
        try
        {
            task = mSource.CreateSynthesisTask(data);
        }
        catch (Exception ex)
        {
            piece.Failed = true;
            piece.Error = ex.Message;
            piece.Dirty = false;
            NotifyStatusChanged();
            return;
        }

        // 开始即清脏：合成期间到达的新变更会重新标脏，完成后自然重排（替换，而非同步）。
        piece.Dirty = false;
        piece.Failed = false;
        piece.Error = null;
        piece.Synthesizing = true;
        piece.Progress = 0;
        NotifyStatusChanged();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        task.Complete += result => mSync.Post(_ =>
        {
            OnPieceComplete(piece, result, views);
            tcs.TrySetResult();
        }, null);
        task.Error += message => mSync.Post(_ =>
        {
            piece.Failed = true;
            piece.Error = message;
            NotifyStatusChanged();
            tcs.TrySetResult();
        }, null);
        task.Progress += p => mSync.Post(_ =>
        {
            piece.Progress = p;
            progress?.Report(p);
            NotifyStatusChanged();
        }, null);

        // 取消是尽力请求且正常返回：Stop 老任务后直接视为本块结束（缓存未更新则保持待合成）。
        using var registration = cancellation.Register(() =>
        {
            try { task.Stop(); } catch { /* 插件侧异常不外溢 */ }
            mSync.Post(_ => { piece.Dirty = true; tcs.TrySetResult(); }, null);
        });

        try
        {
            task.Start();
        }
        catch (Exception ex)
        {
            piece.Failed = true;
            piece.Error = ex.Message;
        }

        if (!piece.Failed)
            await tcs.Task;

        piece.Synthesizing = false;
        NotifyStatusChanged();
    }

    // —— 音频产物（多块按全局时间轴对齐相加；协议：全局 0 时刻 = 采样点 0）——
    public int SampleRate => mSessionRate ?? 44100;

    public void ReadAudio(long offset, int count, float[] dst)
    {
        if (mSessionRate is not { } rate)
            return;

        foreach (var piece in mPieces)
        {
            if (piece.Result is not { } result)
                continue;

            long resultOffset = (long)(result.StartTime * rate);
            var samples = result.AudioData;
            long from = Math.Max(offset, resultOffset);
            long to = Math.Min(offset + count, resultOffset + samples.Length);
            for (long i = from; i < to; i++)
            {
                dst[i - offset] += samples[i - resultOffset];
            }
        }
    }

    // —— 曲线类产物 ——
    public IReadOnlyList<IReadOnlyList<PStruct.Point>> SynthesizedPitch
    {
        get
        {
            var result = new List<IReadOnlyList<PStruct.Point>>();
            foreach (var piece in mPieces)
            {
                result.AddRange(piece.PitchLines);
            }
            return result;
        }
    }

    public PStruct.IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<PStruct.Point>>> SynthesizedParameters => mSynthesizedParameters;

    public IReadOnlyList<VVoice.SynthesizedPhoneme> Phonemes
    {
        get
        {
            var result = new List<VVoice.SynthesizedPhoneme>();
            foreach (var piece in mPieces)
            {
                result.AddRange(piece.Phonemes);
            }
            return result;
        }
    }

    // —— 状态：每块一段，统一平铺 ——
    public IReadOnlyList<VVoice.SynthesisStatusSegment> GetStatus()
    {
        var result = new List<VVoice.SynthesisStatusSegment>(mPieces.Count);
        foreach (var piece in mPieces)
        {
            var status = piece.Failed ? VVoice.SynthesisSegmentStatus.Failed
                : piece.Synthesizing ? VVoice.SynthesisSegmentStatus.Synthesizing
                : piece.Dirty || piece.Result == null ? VVoice.SynthesisSegmentStatus.Pending
                : VVoice.SynthesisSegmentStatus.Synthesized;
            result.Add(new VVoice.SynthesisStatusSegment
            {
                StartTime = piece.StartTime,
                EndTime = piece.EndTime,
                Status = status,
                Message = piece.Failed ? piece.Error : null,
                Progress = piece.Synthesizing ? piece.Progress : 0,
            });
        }
        return result;
    }

    public event Action? StatusChanged;

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        mNotesSubscription.Dispose();
        mContext.Notes.ItemAdded -= OnNotesStructureChanged;
        mContext.Notes.ItemRemoved -= OnNotesStructureChanged;
        mContext.PartProperties.Modified -= OnPartPropertiesModified;
        mContext.TimingModified -= OnTimingModified;
        mContext.BatchEnd -= OnBatchEnd;
        mContext.Pitch.RangeModified -= OnRangeModified;
        mContext.PitchDeviation.RangeModified -= OnRangeModified;
        foreach (var automation in mSubscribedAutomations)
        {
            automation.RangeModified -= OnRangeModified;
        }
        mSubscribedAutomations.Clear();
        foreach (var kv in mNoteHandlers)
        {
            DetachNoteHandler(kv.Key, kv.Value);
        }
        mNoteHandlers.Clear();
        mPieces.Clear();
    }

    // —— 变更接线（数据线程）——

    void SubscribeNote(VVoice.ISynthesisNote note)
    {
        void handler()
        {
            MarkNoteDirty(note);
            mNeedReSegment = true;
        }
        mNoteHandlers[note] = handler;
        note.StartPosition.Modified += handler;
        note.EndPosition.Modified += handler;
        note.Pitch.Modified += handler;
        note.Lyric.Modified += handler;
        note.Phonemes.Modified += handler;
        note.Properties.Modified += handler;
    }

    void UnsubscribeNote(VVoice.ISynthesisNote note)
    {
        if (mNoteHandlers.Remove(note, out var handler))
            DetachNoteHandler(note, handler);
    }

    void DetachNoteHandler(VVoice.ISynthesisNote note, Action handler)
    {
        note.StartPosition.Modified -= handler;
        note.EndPosition.Modified -= handler;
        note.Pitch.Modified -= handler;
        note.Lyric.Modified -= handler;
        note.Phonemes.Modified -= handler;
        note.Properties.Modified -= handler;
    }

    void OnNotesStructureChanged(VVoice.ISynthesisNote note)
    {
        mNeedReSegment = true;
    }

    void OnPartPropertiesModified()
    {
        MarkAllDirty();
        mNeedReSegment = true;
    }

    void OnTimingModified()
    {
        MarkAllDirty();
        mNeedReSegment = true;
    }

    void OnBatchEnd()
    {
        if (mNeedReSegment)
        {
            ReSegment();
            NotifyStatusChanged();
        }
    }

    void OnRangeModified(double startTick, double endTick)
    {
        double startTime = mContext.Timing.ToSeconds(startTick);
        double endTime = mContext.Timing.ToSeconds(endTick);
        bool any = false;
        foreach (var piece in mPieces)
        {
            if (piece.EndTime < startTime || piece.StartTime > endTime)
                continue;

            MarkDirty(piece);
            any = true;
        }
        if (any)
            NotifyStatusChanged();
    }

    void MarkNoteDirty(VVoice.ISynthesisNote note)
    {
        foreach (var piece in mPieces)
        {
            if (piece.Notes.Contains(note))
                MarkDirty(piece);
        }
    }

    void MarkAllDirty()
    {
        foreach (var piece in mPieces)
        {
            MarkDirty(piece);
        }
    }

    static void MarkDirty(Piece piece)
    {
        piece.Dirty = true;
        piece.Failed = false;
        piece.Error = null;
    }

    // —— 分片（数据线程；老 Segment() 决定边界，按 note 集等价保留未变块的缓存与状态）——

    void EnsureSegmented()
    {
        if (mNeedReSegment)
        {
            ReSegment();
        }
    }

    void ReSegment()
    {
        mNeedReSegment = false;

        var origins = new List<VVoice.ISynthesisNote>(mContext.Notes);
        var liveViews = new List<LiveNoteView>(origins.Count);
        foreach (var origin in origins)
        {
            liveViews.Add(mNoteViewCache.Wrap(origin));
        }
        mNoteViewCache.Prune(origins);

        var segments = mSource.Segment(new LVoice.SynthesisSegment<LiveNoteView>
        {
            PartProperties = ReadPartProperties(),
            Notes = liveViews,
        });

        var newPieces = new List<Piece>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.Notes.Count == 0)
                continue;

            var notes = segment.Notes.Select(view => view.Origin).ToList();
            var existing = mPieces.FirstOrDefault(piece => piece.Notes.SequenceEqual(notes));
            if (existing != null)
            {
                mPieces.Remove(existing);
                // 边界随当前数据刷新（note 集没变时通常不动；tempo/平移场景已全脏重排）。
                existing.StartTime = segment.Notes.First().StartTime;
                existing.EndTime = segment.Notes.Last().EndTime;
                newPieces.Add(existing);
            }
            else
            {
                newPieces.Add(new Piece
                {
                    Notes = notes,
                    StartTime = segment.Notes.First().StartTime,
                    EndTime = segment.Notes.Last().EndTime,
                    Dirty = true,
                });
            }
        }

        mPieces.Clear();
        mPieces.AddRange(newPieces);
    }

    // —— 产物落账（数据线程，经 Post 进入）——

    void OnPieceComplete(Piece piece, LVoice.SynthesisResult result, IReadOnlyList<SnapshotNoteView> views)
    {
        if (mDisposed || !mPieces.Contains(piece))
            return; // 已被重分片取代，过期结果丢弃

        // 采样率统一：会话报告单一输出率。老引擎输出率几乎恒定；万一变率，旧缓存全部
        // 标脏按新率重合成（罕见路径，正确性优先）。
        if (mSessionRate is { } rate && rate != result.SamplingRate)
        {
            foreach (var other in mPieces)
            {
                if (other != piece && other.Result != null)
                {
                    other.Result = null;
                    MarkDirty(other);
                }
            }
        }
        mSessionRate = result.SamplingRate;

        piece.Result = result;
        piece.PitchLines = result.SynthesizedPitch
            .Select(line => (IReadOnlyList<PStruct.Point>)line.Select(p => p.ToV1()).ToList())
            .ToList();

        // 音素扁平化：老结果按 note 装字典（键 = 我们递入的快照包装）→ 经 Origin 归出身 live 代理。
        var phonemes = new List<VVoice.SynthesizedPhoneme>();
        foreach (var kv in result.SynthesizedPhonemes)
        {
            if (kv.Key is not SnapshotNoteView view)
                continue;

            foreach (var phoneme in kv.Value)
            {
                phonemes.Add(new VVoice.SynthesizedPhoneme
                {
                    Symbol = phoneme.Symbol,
                    StartTime = phoneme.StartTime,
                    EndTime = phoneme.EndTime,
                    Note = view.Origin,
                    // 老引擎无伸缩权重概念：w = 时长，退化为均匀缩放（与旧 preview 行为一致）。
                    StretchWeight = phoneme.EndTime - phoneme.StartTime,
                });
            }
        }
        piece.Phonemes = phonemes;

        NotifyStatusChanged();
    }

    void NotifyStatusChanged()
    {
        if (!mDisposed)
            StatusChanged?.Invoke();
    }

    // —— 老引擎的输入读取（V1 订阅树外观不可枚举：键集来自老声源的声明）——

    LProp.PropertyObject ReadPartProperties()
    {
        return ReadProperties(mSource.PartProperties.Keys, mContext.PartProperties);
    }

    LProp.PropertyObject ReadNoteProperties(VVoice.ISynthesisNote note)
    {
        return ReadProperties(mSource.NoteProperties.Keys, note.Properties);
    }

    static LProp.PropertyObject ReadProperties(IEnumerable<string> keys, TuneLab.Primitives.Property.IReadOnlyNotifiablePropertyObject source)
    {
        var map = new TuneLab.Base.Structures.Map<string, LProp.PropertyValue>();
        foreach (var key in keys)
        {
            var value = source.GetValue(key, TuneLab.Primitives.Property.PropertyValue.Null);
            if (!value.IsNull())
                map[key] = value.ToLegacy();
        }
        return new LProp.PropertyObject(map);
    }

    // —— 内部类型 ——

    sealed class Piece
    {
        public required IReadOnlyList<VVoice.ISynthesisNote> Notes;
        public double StartTime;
        public double EndTime;
        public bool Dirty;
        public bool Failed;
        public bool Synthesizing;
        public string? Error;
        public double Progress;
        public LVoice.SynthesisResult? Result;
        public IReadOnlyList<IReadOnlyList<PStruct.Point>> PitchLines = [];
        public IReadOnlyList<VVoice.SynthesizedPhoneme> Phonemes = [];
    }

    // 老 ISynthesisData：全部读冻结快照（worker 线程安全）。
    // 老接口的取值轴是秒：经快照 Timing 换算到全局 tick 再查冻结 getter。
    // 老 Pitch 语义 = 最终音高（含 vibrato）：新双通道在此合成 Pitch + PitchDeviation（NaN=自由区保持 NaN，
    // 与老引擎"无绘制即自由"的预期一致；老模型本就收不到自由区的偏差，行为不回退）。
    sealed class SnapshotSynthesisData(VVoice.SynthesisSnapshot snapshot, IReadOnlyList<SnapshotNoteView> views) : LVoice.ISynthesisData
    {
        public IEnumerable<LVoice.ISynthesisNote> Notes => views;
        public LProp.PropertyObject PartProperties => mPartProperties ??= snapshot.PartProperties.ToLegacy();
        public LVoice.IAutomationValueGetter Pitch => mPitch ??= new ComposedFinalPitchGetter(snapshot);

        public bool GetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out LVoice.IAutomationValueGetter? automation)
        {
            if (snapshot.Automations.TryGetValue(automationID, out var getter))
            {
                automation = new SecondsGetterAdapter(getter, snapshot.Timing);
                return true;
            }
            automation = null;
            return false;
        }

        LProp.PropertyObject? mPartProperties;
        LVoice.IAutomationValueGetter? mPitch;
    }

    sealed class SecondsGetterAdapter(VBase.IAutomationValueGetter getter, TuneLab.SDK.Base.Timing.ITiming timing) : LVoice.IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            return getter.GetValue(timing.ToTick(times));
        }
    }

    sealed class ComposedFinalPitchGetter(VVoice.SynthesisSnapshot snapshot) : LVoice.IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            var ticks = snapshot.Timing.ToTick(times);
            var values = snapshot.Pitch.GetValue(ticks);
            var deviation = snapshot.PitchDeviation.GetValue(ticks);
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.IsNaN(values[i]))
                    values[i] += deviation[i];
            }
            return values;
        }
    }

    readonly LVoice.IVoiceSource mSource;
    readonly VVoice.ISynthesisContext mContext;
    readonly SynchronizationContext mSync;
    readonly LiveNoteViewCache mNoteViewCache;

    readonly PStruct.IReadOnlyOrderedMap<string, VConfig.AutomationConfig> mAutomationConfigs;
    readonly PStruct.IReadOnlyOrderedMap<string, VConfig.IControllerConfig> mPartProperties;
    readonly PStruct.IReadOnlyOrderedMap<string, VConfig.IControllerConfig> mNoteProperties;
    static readonly PStruct.OrderedMap<string, VConfig.PiecewiseAutomationConfig> mPiecewiseAutomationConfigs = new();
    static readonly PStruct.Map<string, IReadOnlyList<IReadOnlyList<PStruct.Point>>> mSynthesizedParameters = new();

    readonly IDisposable mNotesSubscription;
    readonly Dictionary<VVoice.ISynthesisNote, Action> mNoteHandlers = new(ReferenceEqualityComparer.Instance);
    readonly List<VVoice.ISynthesisAutomation> mSubscribedAutomations = new();

    readonly List<Piece> mPieces = new();
    bool mNeedReSegment;
    int? mSessionRate;
    bool mDisposed;
}
