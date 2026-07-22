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

        // —— 懒脏策略（设计许可的最粗粒度实现的细化版）：note 字段变更分两桶——
        //    **基线脏**（改音素预测输入：几何/音高/歌词/属性）→ 作废基线回显缓存 + 标脏 + 待重分片；
        //    **渲染脏**（只改声学，不改音素预测：钉死音素双列表/结合线）→ 只标脏（保基线缓存、仅重渲）；
        //    曲线区间变更（automation）走 OnRangeModified，属渲染脏，**绝不**作废基线缓存；
        //    时基/part 属性 → 全部标脏（part 属性可能改预测，作废全部基线）+ 重分片。 ——
        mNotesBaselineDirty = mContext.Notes.WhenAnyItem(
            n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Pitch.Modified,
            n => n.Lyric.Modified, n => n.Properties.Modified);
        mNotesBaselineDirty.Subscribe(OnNoteBaselineDirty);
        mNotesRenderDirty = mContext.Notes.WhenAnyItem(
            n => n.LeadingPhonemes.Modified, n => n.BodyPhonemes.Modified, n => n.BodyOffset.Modified);
        mNotesRenderDirty.Subscribe(OnNoteRenderDirty);
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

    // 延音判定（覆盖接口默认体）：恒 false——老模型**完全没有乘客机制**，"-" note 在老世界就是一个
    // 普通 note（引擎自行决定延续元音或回传占位），显示上也从无"前 note 元音铺过 melisma"的形态。
    // 忠实降级 = 不给老引擎伪造新模型的铺末观感：留空型引擎的 "-" note 显示空白、占位型引擎的占位
    // 回显走普通内容显示——均与老版本 TuneLab 观感一致；钉死也天然界内（P1 场景无从发生）。
    // 恒 false 同时意味着判定零产物依赖、恒稳定：新契约的骨架恒等保证对 legacy 平凡成立。
    public bool IsContinuation(VVoice.IVoiceSynthesisNote note) => false;

    // —— 调度 ——
    public VVoice.SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime)
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

        // 同步前缀拉取快照：automation 开窗按 note 范围外扩固定秒余量（老引擎常对块边界外略作采样）。两 pass 共用同一份。
        const double windowMarginSeconds = 0.5;
        var snapshot = mContext.GetSnapshot(
            piece.Notes,
            piece.Notes.First().StartTime.Value - windowMarginSeconds,
            piece.Notes.Last().EndTime.Value + windowMarginSeconds);

        // 开始即清脏：合成期间到达的新变更会重新标脏，完成后自然重排（替换，而非同步）。
        piece.Dirty = false;
        piece.Failed = false;
        piece.Error = null;
        piece.Synthesizing = true;
        piece.Progress = 0;
        NotifyStatusChanged();

        try
        {
            // —— 基线 pass：缓存缺失 / 内在脏时先跑一遍 no-pin 自然预测（全喂空），取音素落缓存、丢音频 ——
            if (piece.BaselineDirty || piece.BaselineEcho == null)
            {
                var baselineViews = SnapshotNoteView.CreateChain(snapshot.Notes, piece.Notes, baselineEcho: null);
                var baseline = await RunPass(new SnapshotSynthesisData(snapshot, baselineViews), progressPiece: null, cancellation);
                if (baseline.Cancelled)
                {
                    piece.Dirty = true;
                    return;
                }
                if (baseline.Result == null)
                {
                    piece.Failed = true;
                    piece.Error = baseline.Error;
                    return;
                }
                piece.BaselineEcho = BuildEcho(baseline.Result, baselineViews);
                piece.BaselineDirty = false;
            }

            // —— 真实 pass：钉死 note 用用户值、未钉死 note 用基线缓存 → 全显式时间线（无引擎当场自预测、无接缝 Sil、音频==显示）——
            var echo = new VVoice.SynthesizedSyllable?[piece.Notes.Count];
            for (int i = 0; i < piece.Notes.Count; i++)
                echo[i] = piece.BaselineEcho!.TryGetValue(piece.Notes[i].Id, out var syllable) ? syllable : null;
            var views = SnapshotNoteView.CreateChain(snapshot.Notes, piece.Notes, echo);
            var real = await RunPass(new SnapshotSynthesisData(snapshot, views), progressPiece: piece, cancellation);
            if (real.Cancelled)
            {
                piece.Dirty = true;
                return;
            }
            if (real.Result == null)
            {
                piece.Failed = true;
                piece.Error = real.Error;
                return;
            }
            OnPieceComplete(piece, real.Result);
        }
        finally
        {
            piece.Synthesizing = false;
            NotifyStatusChanged();
        }
    }

    // 跑老引擎一遍：建任务、接线回调、Start、await 到完成 / 失败 / 取消（都正常返回，不抛）。
    // progressPiece 非空才把进度落账（基线 pass 不报进度）。回调经 mSync.Post 落回数据线程，
    // await 续体因数据线程有捕获的 SynchronizationContext 而恢复在数据线程 → 调用方后续（BuildEcho/OnPieceComplete）皆数据线程。
    async Task<(LVoice.SynthesisResult? Result, string? Error, bool Cancelled)> RunPass(
        SnapshotSynthesisData data, Piece? progressPiece, CancellationToken cancellation)
    {
        LVoice.ISynthesisTask task;
        try
        {
            task = mSource.CreateSynthesisTask(data);
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false);
        }

        var tcs = new TaskCompletionSource<(LVoice.SynthesisResult?, string?, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        task.Complete += result => mSync.Post(_ => tcs.TrySetResult((result, null, false)), null);
        task.Error += message => mSync.Post(_ => tcs.TrySetResult((null, message, false)), null);
        if (progressPiece != null)
        {
            task.Progress += p => mSync.Post(_ =>
            {
                progressPiece.Progress = p;
                NotifyStatusChanged();
            }, null);
        }

        // 取消是尽力请求且正常返回：Stop 老任务后视为本 pass 取消（调用方保持待合成）。
        using var registration = cancellation.Register(() =>
        {
            try { task.Stop(); } catch { /* 插件侧异常不外溢 */ }
            mSync.Post(_ => tcs.TrySetResult((null, null, true)), null);
        });

        try
        {
            task.Start();
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false);
        }

        return await tcs.Task;
    }

    // 老结果的音素 → 每 note 的 SynthesizedSyllable（自然预测缓存）：分类进引导 / 主体双列表（老模型无 lead 字段，
    // 按位置降级——音素中点落 note 头之前的连续前缀 = 引导、其余 = 主体；不吸头，保留引擎略跨头分界，根治强吸头引入
    // sub-frame 错位被帧量化成整帧 Sil）。两条修正（老引擎无权重概念，宿主补足使布局像真人嗓）：
    //   ① **至少一个拍后音素**：若按中点判完全归引导（无主体），把**最后一个引导**挪进主体，保证有核可填满音符。
    //   ② **首个拍后音素 w=1**（弹性核、填满音符到满末）；其余音素 w=0（刚性、固定长）。
    static PStruct.Map<string, VVoice.SynthesizedSyllable> BuildEcho(
        LVoice.SynthesisResult result, IReadOnlyList<SnapshotNoteView> views)
    {
        var echo = new PStruct.Map<string, VVoice.SynthesizedSyllable>();
        foreach (var kv in result.SynthesizedPhonemes)
        {
            if (kv.Key is not SnapshotNoteView view)
                continue;

            var items = new List<VVoice.SynthesizedPhoneme>();
            var starts = new List<double>();
            foreach (var phoneme in kv.Value)
            {
                items.Add(new VVoice.SynthesizedPhoneme { Symbol = phoneme.Symbol, Duration = phoneme.EndTime - phoneme.StartTime });
                starts.Add(phoneme.StartTime);
            }
            int count = items.Count;
            if (count == 0)
                continue;

            int leadCount = 0;                                    // 中点 < note 头的连续前缀 = 引导
            for (int i = 0; i < count; i++)
            {
                if ((starts[i] + starts[i] + items[i].Duration) < 2 * view.StartTime) leadCount = i + 1; else break;
            }
            if (leadCount >= count) leadCount = count - 1;        // ① 保证 ≥1 拍后：全归引导时把末个引导挪进主体

            var leading = new List<VVoice.SynthesizedPhoneme>();
            var body = new List<VVoice.SynthesizedPhoneme>();
            for (int i = 0; i < count; i++)
            {
                var d = items[i] with { StretchWeight = i == leadCount ? 1 : 0 };   // ② 首个拍后音素 = 弹性核 w=1，其余 w=0
                if (i < leadCount) leading.Add(d); else body.Add(d);
            }
            // junction（结合线）= 主体首起点（其原始绝对起点）。BodyOffset = junction − note 头（有符号、不吸头）。
            double bodyOffset = starts[leadCount] - view.StartTime;
            echo.Add(view.Origin.Id, new VVoice.SynthesizedSyllable(leading, body, bodyOffset));
        }
        return echo;
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

    public PStruct.IReadOnlyMap<string, VVoice.SynthesizedSyllable> SynthesizedPhonemes
    {
        get
        {
            var result = new PStruct.Map<string, VVoice.SynthesizedSyllable>();
            foreach (var piece in mPieces)
            {
                foreach (var kvp in piece.Phonemes)   // 块间 note 不相交，直接并入
                    result.Add(kvp.Key, kvp.Value);
            }
            return result;
        }
    }

    // —— 状态：每块一段，统一平铺 ——
    public IReadOnlyList<VVoice.SynthesisStatusSegment> Status => BuildStatus();

    IReadOnlyList<VVoice.SynthesisStatusSegment> BuildStatus()
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

        mNotesBaselineDirty.Unsubscribe(OnNoteBaselineDirty);
        mNotesRenderDirty.Unsubscribe(OnNoteRenderDirty);
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

    // 基线脏（几何/音高/歌词/属性 → 改音素预测）：作废该 note 所在块的基线回显缓存 + 标脏 + 待重分片。
    void OnNoteBaselineDirty(VVoice.IVoiceSynthesisNote note)
    {
        MarkNoteDirty(note, baselineDirty: true);
        mNeedReSegment = true;
    }

    // 渲染脏（钉死音素双列表/结合线 → 只改声学、不改音素预测）：仅标脏所在块，**保留基线回显缓存**。
    // 仍待重分片（分片按 note 时间间隙、不依赖音素内容，故通常为 note 集等价的 no-op 重分片、基线缓存随块复用保留）。
    void OnNoteRenderDirty(VVoice.IVoiceSynthesisNote note)
    {
        MarkNoteDirty(note, baselineDirty: false);
        mNeedReSegment = true;
    }

    void OnNotesStructureChanged(VVoice.IVoiceSynthesisNote note)
    {
        mNeedReSegment = true;
    }

    void OnPartPropertiesModified()
    {
        MarkAllDirty(baselineDirty: true);   // part 属性可能改音素预测 → 作废全部基线
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

    void MarkNoteDirty(VVoice.IVoiceSynthesisNote note, bool baselineDirty)
    {
        foreach (var piece in mPieces)
        {
            if (piece.Notes.Contains(note))
            {
                MarkDirty(piece);
                if (baselineDirty)
                    piece.BaselineDirty = true;
            }
        }
    }

    void MarkAllDirty(bool baselineDirty)
    {
        foreach (var piece in mPieces)
        {
            MarkDirty(piece);
            if (baselineDirty)
                piece.BaselineDirty = true;
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

    void OnPieceComplete(Piece piece, LVoice.SynthesisResult result)
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

        // 音素上报 = **基线自然预测缓存**（原始、未压缩），不用真实 pass 的回传：
        //   · 未钉死 note → 宿主 DisplayPhonemes 用它作布局上下文，与 CreateChain 喂引擎的同一份 → 音频==显示；
        //   · 钉死 note → 宿主用其钉死 Phonemes（忽略此项），此处报的自然预测无害；
        //   · 只认可复现的自然预测缓存、与真实 pass 的瞬态帧量化无关 → 关闭重开一致。
        // 延音判定恒 false（见 IsContinuation——老模型无乘客机制），故所有回显走宿主普通内容显示。
        piece.Phonemes = piece.BaselineEcho ?? new PStruct.Map<string, VVoice.SynthesizedSyllable>();

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
        public PStruct.IReadOnlyMap<string, VVoice.SynthesizedSyllable> Phonemes = new PStruct.Map<string, VVoice.SynthesizedSyllable>();

        // 基线自然预测（no-pin 跑一遍得到、按内在数据有效）：喂引擎时未钉死 note 用它，宿主显示也上报它作
        // SynthesizedSyllable（两处同一份原始描述符 → 音频==显示）。默认待建（BaselineDirty=true / null）；
        // 内在脏才作废、渲染脏（钉死/automation）保留 → 编辑期稳定、关闭重开重跑得同一份 → 结果确定。
        public bool BaselineDirty = true;
        public PStruct.Map<string, VVoice.SynthesizedSyllable>? BaselineEcho;
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
        public double[] GetValue(IReadOnlyList<double> times)
        {
            var results = new double[times.Count];
            evaluator.Evaluate(times, results);
            return results;
        }
    }

    sealed class ComposedFinalPitchGetter(VVoice.VoiceSynthesisSnapshot snapshot) : LVoice.IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            var values = new double[times.Count];
            snapshot.Pitch.Evaluator.Evaluate(times, values);
            var deviation = new double[times.Count];
            snapshot.PitchDeviation.Evaluator.Evaluate(times, deviation);
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

    readonly IActionEvent<VVoice.IVoiceSynthesisNote> mNotesBaselineDirty;
    readonly IActionEvent<VVoice.IVoiceSynthesisNote> mNotesRenderDirty;
    readonly List<VVoice.ISynthesisAutomation> mSubscribedAutomations = new();

    readonly List<Piece> mPieces = new();
    bool mNeedReSegment;
    int? mSessionRate;
    bool mDisposed;
}
