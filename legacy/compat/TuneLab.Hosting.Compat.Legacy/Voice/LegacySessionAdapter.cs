using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Hosting.Compat.Legacy.Conversion;
using TuneLab.Foundation;
using LProp = TuneLab.Base.Properties;
using LVoice = TuneLab.Extensions.Voices;
using VBase = TuneLab.SDK;
using VVoice = TuneLab.SDK;
using PStruct = TuneLab.Foundation;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 IVoiceSource + ISynthesisTask（task 模型）适配成 V1 IVoiceSession（会话模型）：
// compat 层内置一个"迷你宿主"——分片账本（老 Segment() 决定分片）、脏判定（懒策略：
// 订阅 context 全部变更通知按块标脏、BatchEnd 重分片并按 note 集等价保留未变块的缓存）、
// 产物聚合（多块音频拼成单一时间线、音素扁平化归出身、状态带按块平铺）。
//
// 线程纪律：除老 task 的回调（任意线程，一律 Post 回数据线程再落账）外，全部成员仅数据线程访问。
internal sealed class LegacySessionAdapter : VVoice.IVoiceSynthesisSession
{
    public LegacySessionAdapter(LVoice.IVoiceSource source, VVoice.IVoiceSynthesisContext context)
    {
        mSource = source;
        mContext = context;
        mSync = SynchronizationContext.Current ?? throw new InvalidOperationException("LegacySessionAdapter must be created on the data thread.");
        mNoteViewCache = new LiveNoteViewCache(ReadNoteProperties);

        // —— 懒脏策略（设计许可的最粗粒度实现的细化版）：任何 note 字段/增删 → 标脏所在块 +
        //    待重分片；曲线区间变更 → 标脏相交块；时基/part 属性 → 全部标脏重分片。 ——
        mNotesDirty = mContext.Notes.WhenAnyItem(
            n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Pitch.Modified,
            n => n.Lyric.Modified, n => n.Phonemes.Modified, n => n.Properties.Modified);
        mNotesDirty.Subscribe(OnNoteDirty);
        mContext.Notes.ItemAdded.Subscribe(OnNotesStructureChanged);
        mContext.Notes.ItemRemoved.Subscribe(OnNotesStructureChanged);
        mContext.PartProperties.Modified.Subscribe(OnPartPropertiesModified);
        mContext.Committed.Subscribe(OnCommitted);
        mContext.Pitch.RangeModified.Subscribe(OnRangeModified);
        mContext.PitchDeviation.RangeModified.Subscribe(OnRangeModified);   // 老 Pitch 含偏差，偏差变化同样标脏
        // 构造期即订阅本声源声明的各自动化轨：宿主已在建会话之前按引擎声明填好 AutomationConfigs，
        // 故 context.Automations 已含自己声明的轨。键集取自老声源声明。
        foreach (var key in mSource.AutomationConfigs.Keys)
        {
            if (mContext.Automations.TryGetValue(key, out var automation))
            {
                automation.RangeModified.Subscribe(OnRangeModified);
                mSubscribedAutomations.Add(automation);
            }
        }

        mNeedReSegment = true;
    }

    // 默认歌词：会话级运行时取值（声明类 config 已上移到 VoiceEngineAdapter）。
    public string DefaultLyric => mSource.DefaultLyric;

    // —— 调度 ——
    public VVoice.SynthesisRange? GetNextSegment(double startTime, double endTime)
    {
        return FindNextDirtyPiece(startTime, endTime) is { } piece
            ? new VVoice.SynthesisRange(piece.StartTime, piece.EndTime)
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

    public async Task SynthesizeNext(double startTime, double endTime,
        CancellationToken cancellation = default)
    {
        if (FindNextDirtyPiece(startTime, endTime) is not { } piece)
            return;

        // 同步前缀拉取快照：automation 开窗按 note 范围外扩固定秒余量（老引擎常对块边界外略作采样）。
        const double windowMarginSeconds = 0.5;
        var snapshot = mContext.GetSnapshot(
            piece.Notes,
            piece.Notes.First().StartTime.Value - windowMarginSeconds,
            piece.Notes.Last().EndTime.Value + windowMarginSeconds);
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

    // —— 音频产物（每块经 IAudioSegment 握柄交付；采样率随段在 CreateAudioSegment 传入；协议：全局 0 时刻 = 采样点 0）——

    // —— 曲线类产物 ——
    public VVoice.SynthesizedPitch SynthesizedPitch
    {
        get
        {
            var result = new List<IReadOnlyList<PStruct.Point>>();
            foreach (var piece in mPieces)
            {
                result.AddRange(piece.PitchLines);
            }
            return new VVoice.SynthesizedPitch { Segments = result };
        }
    }

    public PStruct.IReadOnlyMap<string, VVoice.SynthesizedParameter> SynthesizedParameters => mSynthesizedParameters;

    public PStruct.IReadOnlyMap<VVoice.IVoiceSynthesisNote, IReadOnlyList<VVoice.SynthesizedPhoneme>> SynthesizedPhonemes
    {
        get
        {
            var result = new PStruct.Map<VVoice.IVoiceSynthesisNote, IReadOnlyList<VVoice.SynthesizedPhoneme>>();
            foreach (var piece in mPieces)
            {
                foreach (var kvp in piece.Phonemes)   // 块间 note 不相交，直接并入
                    result.Add(kvp.Key, kvp.Value);
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
        if (mDisposed)
            return;
        mDisposed = true;

        mNotesDirty.Unsubscribe(OnNoteDirty);
        mContext.Notes.ItemAdded.Unsubscribe(OnNotesStructureChanged);
        mContext.Notes.ItemRemoved.Unsubscribe(OnNotesStructureChanged);
        mContext.PartProperties.Modified.Unsubscribe(OnPartPropertiesModified);
        mContext.Committed.Unsubscribe(OnCommitted);
        mContext.Pitch.RangeModified.Unsubscribe(OnRangeModified);
        mContext.PitchDeviation.RangeModified.Unsubscribe(OnRangeModified);
        foreach (var automation in mSubscribedAutomations)
        {
            automation.RangeModified.Unsubscribe(OnRangeModified);
        }
        mSubscribedAutomations.Clear();
        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
        mPieces.Clear();
    }

    // —— 变更接线（数据线程）——

    // 任一 note 的几何/发音/属性变更 → 标脏该 note 所在块 + 待重分片。WhenAnyItem 带成员标识，
    // 故能精确标脏变化的 note；成员订阅生命周期（增删接线/退订）由 WhenAnyItem 托管。
    void OnNoteDirty(VVoice.IVoiceSynthesisNote note)
    {
        MarkNoteDirty(note);
        mNeedReSegment = true;
    }

    void OnNotesStructureChanged(VVoice.IVoiceSynthesisNote note)
    {
        mNeedReSegment = true;
    }

    void OnPartPropertiesModified()
    {
        MarkAllDirty();
        mNeedReSegment = true;
    }

    void OnCommitted()
    {
        if (mNeedReSegment)
        {
            ReSegment();
            NotifyStatusChanged();
        }
    }

    void OnRangeModified(double startTime, double endTime)
    {
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

    void MarkNoteDirty(VVoice.IVoiceSynthesisNote note)
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

        var origins = new List<VVoice.IVoiceSynthesisNote>(mContext.Notes);
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

        // 未被复用的旧块（note 集已不存在）：释放段握柄，宿主丢对应 effect 缓存。
        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
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
                    other.Segment?.Dispose();
                    other.Segment = null;
                    MarkDirty(other);
                }
            }
        }
        mSessionRate = result.SamplingRate;

        piece.Result = result;
        // 段握柄：丢旧建新（一握柄 = 一次渲染）；写入整段后 Commit 把整段音频交宿主驱动 effect。
        piece.Segment?.Dispose();
        piece.Segment = mContext.CreateAudioSegment((long)(result.StartTime * result.SamplingRate), result.AudioData.Length, result.SamplingRate);
        piece.Segment.Write(0, result.AudioData);
        piece.Segment.Commit();
        piece.PitchLines = result.SynthesizedPitch
            .Select(line => (IReadOnlyList<PStruct.Point>)line.Select(p => p.ToV1()).ToList())
            .ToList();

        // 音素归组：老结果按 note 装字典（键 = 我们递入的快照包装）→ 经 Origin 归出身 live 代理（map 键）。
        var phonemes = new PStruct.Map<VVoice.IVoiceSynthesisNote, IReadOnlyList<VVoice.SynthesizedPhoneme>>();
        foreach (var kv in result.SynthesizedPhonemes)
        {
            if (kv.Key is not SnapshotNoteView view)
                continue;

            var list = new List<VVoice.SynthesizedPhoneme>();
            foreach (var phoneme in kv.Value)
            {
                list.Add(new VVoice.SynthesizedPhoneme
                {
                    Symbol = phoneme.Symbol,
                    Duration = phoneme.EndTime - phoneme.StartTime,   // 老引擎报绝对位置，转标称时长
                    // 老引擎无伸缩权重概念：w = 时长，退化为均匀缩放（与旧 preview 行为一致）。
                    StretchWeight = phoneme.EndTime - phoneme.StartTime,
                    // 老引擎无前置概念，与老工程导入同口径按时间判定：区间中点落在音符头之前 → 前置辅音。
                    // 老结果为绝对秒，分界线 = 本 note 起点（起点无单声部钳位，直接用快照值）。
                    IsLead = phoneme.StartTime + phoneme.EndTime < 2 * view.StartTime,
                });
            }
            if (list.Count > 0)
                phonemes.Add(view.Origin, list);
        }
        piece.Phonemes = phonemes;

        NotifyStatusChanged();
    }

    // 兼容 shim：老引擎一次性产出全部产物、无细粒度失效图，故统一一并通知（含状态）。
    void NotifyStatusChanged()
    {
        if (mDisposed)
            return;
        mSynthesizedPhonemesChanged.Invoke();
        mSynthesizedParametersChanged.Invoke();
        mSynthesizedPitchChanged.Invoke();
        mStatusChanged.Invoke();
    }

    // —— 老引擎的输入读取（V1 订阅树外观不可枚举：键集来自老声源的声明）——

    LProp.PropertyObject ReadPartProperties()
    {
        return ReadProperties(mSource.PartProperties.Keys, mContext.PartProperties);
    }

    LProp.PropertyObject ReadNoteProperties(VVoice.IVoiceSynthesisNote note)
    {
        return ReadProperties(mSource.NoteProperties.Keys, note.Properties);
    }

    static LProp.PropertyObject ReadProperties(IEnumerable<string> keys, TuneLab.Foundation.IReadOnlyNotifiablePropertyObject source)
    {
        var map = new TuneLab.Base.Structures.Map<string, LProp.PropertyValue>();
        foreach (var key in keys)
        {
            var value = source.GetValue(key, TuneLab.Foundation.PropertyValue.Null);
            if (!value.IsNull())
                map[key] = value.ToLegacy();
        }
        return new LProp.PropertyObject(map);
    }

    // —— 内部类型 ——

    sealed class Piece
    {
        public required IReadOnlyList<VVoice.IVoiceSynthesisNote> Notes;
        public double StartTime;
        public double EndTime;
        public bool Dirty;
        public bool Failed;
        public bool Synthesizing;
        public string? Error;
        public double Progress;
        public LVoice.SynthesisResult? Result;
        public VVoice.IAudioSegment? Segment;
        public IReadOnlyList<IReadOnlyList<PStruct.Point>> PitchLines = [];
        public PStruct.IReadOnlyMap<VVoice.IVoiceSynthesisNote, IReadOnlyList<VVoice.SynthesizedPhoneme>> Phonemes = new PStruct.Map<VVoice.IVoiceSynthesisNote, IReadOnlyList<VVoice.SynthesizedPhoneme>>();
    }

    // 老 ISynthesisData：全部读冻结快照（worker 线程安全）。
    // 老接口与 V1 求值器同为秒轴（V1 全秒轴改造后），直接转调、无需换算。
    // 老 Pitch 语义 = 最终音高（含 vibrato）：新双通道在此合成 Pitch + PitchDeviation（NaN=自由区保持 NaN，
    // 与老引擎"无绘制即自由"的预期一致；老模型本就收不到自由区的偏差，行为不回退）。
    sealed class SnapshotSynthesisData(VVoice.VoiceSynthesisSnapshot snapshot, IReadOnlyList<SnapshotNoteView> views) : LVoice.ISynthesisData
    {
        public IEnumerable<LVoice.ISynthesisNote> Notes => views;
        public LProp.PropertyObject PartProperties => mPartProperties ??= snapshot.PartProperties.ToLegacy();
        public LVoice.IAutomationValueGetter Pitch => mPitch ??= new ComposedFinalPitchGetter(snapshot);

        public bool GetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out LVoice.IAutomationValueGetter? automation)
        {
            if (snapshot.Automations.TryGetValue(automationID, out var automationSnapshot))
            {
                automation = new EvaluatorGetterAdapter(automationSnapshot.Evaluator);
                return true;
            }
            automation = null;
            return false;
        }

        LProp.PropertyObject? mPartProperties;
        LVoice.IAutomationValueGetter? mPitch;
    }

    // 老 IAutomationValueGetter 与 V1 IAutomationEvaluator 同为秒轴，仅类型不同，直接转调。
    sealed class EvaluatorGetterAdapter(VBase.IAutomationEvaluator evaluator) : LVoice.IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times) => evaluator.Evaluate(times);
    }

    sealed class ComposedFinalPitchGetter(VVoice.VoiceSynthesisSnapshot snapshot) : LVoice.IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            var values = snapshot.Pitch.Evaluator.Evaluate(times);
            var deviation = snapshot.PitchDeviation.Evaluator.Evaluate(times);
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.IsNaN(values[i]))
                    values[i] += deviation[i];
            }
            return values;
        }
    }

    readonly LVoice.IVoiceSource mSource;
    readonly VVoice.IVoiceSynthesisContext mContext;
    readonly SynchronizationContext mSync;
    readonly LiveNoteViewCache mNoteViewCache;

    static readonly PStruct.Map<string, VVoice.SynthesizedParameter> mSynthesizedParameters = new();

    readonly IActionEvent<VVoice.IVoiceSynthesisNote> mNotesDirty;
    readonly List<VVoice.ISynthesisAutomation> mSubscribedAutomations = new();

    readonly List<Piece> mPieces = new();
    bool mNeedReSegment;
    int? mSessionRate;
    bool mDisposed;
}
