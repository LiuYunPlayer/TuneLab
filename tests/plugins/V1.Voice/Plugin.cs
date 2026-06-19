using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Voice;

// V1 voice 测试引擎（会话模型的原生参照实现）：2 个声库；按 note 间隙分块、分块账本托管
// 失效与产物；合成时按每个 note 的音高填一段正弦，并产出按出身 note 归属的扁平 phoneme。
// 用于验证：引擎注册、声库列表、CreateSession、分块状态带（多段着色/进度）、变更标脏增量重合成、
// snapshot.Notes 与 segment.Notes 的索引对齐契约（产物归属回活 note）。

public sealed class TestVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    public void Init()
    {
        mVoiceInfos.Add("v1-alice", new VoiceSourceInfo { Name = "Alice (V1 Test)", Description = "Test voice Alice" });
        mVoiceInfos.Add("v1-bob", new VoiceSourceInfo { Name = "Bob (V1 Test)", Description = "Test voice Bob" });
        mNoteProperties.Add("tension", new SliderConfig { DefaultValue = 0, MinValue = -1, MaxValue = 1 });
        // 条件自动化轨开关（part 级）：勾选才暴露 Growl 轨——验证轨集合 = f(part 参数值)，
        // 取消勾选时 Growl 已画曲线由宿主保留隐藏、重新勾选即原样恢复。
        mPartProperties.Add(("growl_enabled", "Enable Growl"), new CheckBoxConfig { DefaultValue = true });
        // 自定义自动化参数名避开宿主保留名（Volume/VibratoEnvelope 等内置项）。
        mGrowlConfigs.Add(("Growl", "Growl"), new AutomationConfig { DefaultValue = 0, MinValue = 0, MaxValue = 100, Color = "#E5A573" });
    }

    public void Destroy() { }

    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context) => new TestSession(context);

    // 声明（引擎层、纯函数）：条件轨集合 = f(part 参数值)。声明先于会话求值，故会话构造期 Growl 轨已就绪、可订阅。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context)
    {
        // 连续轨 Growl（growl_enabled 勾选才暴露）+ 分段轨 Bend（恒在、DefaultValue=NaN）同在一张有序 map。
        var map = new OrderedMap<PropertyKey, AutomationConfig>();
        if (context.PartProperties.GetBool("growl_enabled", true))
        {
            foreach (var kvp in mGrowlConfigs)
                map.Add(kvp.Key, kvp.Value);
        }
        map.Add(("Bend", "Bend"), mBendConfig);
        return map;
    }

    // 合成参数回显轨（只读）：恒声明一条 energy 回显轨（分段形、DefaultValue=NaN、自带色），合成前 key 即存在、可预声明。
    // 曲线数据经 ISynthesisSession.SynthesizedParameters 按同一 key（energy）承载；宿主作一等只读轨绘制。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context) => mReadbackConfigs;
    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => new() { Properties = mPartProperties };
    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => new() { Properties = mNoteProperties };

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
    readonly OrderedMap<PropertyKey, AutomationConfig> mGrowlConfigs = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mNoteProperties = new();
    // 分段轨（DefaultValue = NaN 表无基线）：验证声明/数据/路由/渲染/编辑/存盘链路；本参照实现的合成暂不消费它。
    static readonly AutomationConfig mBendConfig = new() { DefaultValue = double.NaN, MinValue = -100, MaxValue = 100, Color = "#73C2E5" };
    // 回显轨声明（恒在、只读）：分段形（DefaultValue = NaN），曲线数据经 SynthesizedParameters 的 "energy" key 承载。
    static readonly OrderedMap<PropertyKey, AutomationConfig> mReadbackConfigs = new()
    {
        { ("energy", "Energy"), new AutomationConfig { DefaultValue = double.NaN, MinValue = 0, MaxValue = 100, Color = "#E573B0" } },
    };
}

public sealed class TestSession : ISynthesisSession
{
    public TestSession(ISynthesisContext context)
    {
        mContext = context;

        // 变更接线（数据线程，handler 只做廉价标脏；重活延迟到 Committed 重分块）：
        // note 字段变化 → 标脏所在块 + 待重分块；增删 → 待重分块；
        // 曲线区间变化 → 标脏相交块；时基/part 属性 → 全部标脏。
        mNotesSubscription = TuneLab.Foundation.NotifiableExtensions.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.ItemAdded += OnNotesStructureChanged;
        context.Notes.ItemRemoved += OnNotesStructureChanged;
        context.PartProperties.Modified += MarkAllDirtyAndResegment;
        context.Committed += OnCommitted;
        context.Pitch.RangeModified += OnRangeModified;
        context.PitchDeviation.RangeModified += OnRangeModified;
        // 构造期即订阅自己声明的 Growl 轨：宿主在建会话之前已按引擎声明填好 AutomationConfigs，
        // 故此处 TryGetAutomation 必成（声明已就绪）——参数绘制后的区间失效经此回调送达、触发重渲。
        if (context.TryGetAutomation("Growl", out var growl))
            growl.RangeModified += OnRangeModified;

        mNeedResegment = true;
    }

    public string DefaultLyric => "la";

    // —— 调度：窗内第一个脏块的纯值边界（peek 廉价）——
    public SynthesisRange? GetNextSegment(double startTime, double endTime)
    {
        return FindNextDirtyPiece(startTime, endTime) is { } piece
            ? new SynthesisRange(piece.StartTime, piece.EndTime)
            : null;
    }

    // peek 与 commit 共用同一查找（确定性 + 同调度 tick 无编辑 ⇒ commit 重算得到 peek 报出的同一块）。
    Piece? FindNextDirtyPiece(double startTime, double endTime)
    {
        if (mNeedResegment)
            Resegment();

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

        // 同步前缀（数据线程）拉取快照：notes 即本块全集，曲线开窗按 note 范围。
        var snapshot = mContext.GetSnapshot(
            piece.Notes,
            piece.Notes[0].StartTime.Value,
            piece.Notes[^1].EndTime.Value);

        piece.Dirty = false; // 合成期间到达的新变更会重新标脏，完成后自然重排
        piece.Synthesizing = true;
        piece.Progress = 0;
        StatusChanged?.Invoke();

        // Progress<T> 捕获数据线程上下文：worker 的进度上报 marshal 回来落账再转发宿主。
        var report = new Progress<double>(p =>
        {
            piece.Progress = p;
            StatusChanged?.Invoke();
        });

        try
        {
            var rendered = await Task.Run(() => Render(snapshot, piece.Notes, report, cancellation), CancellationToken.None);
            if (rendered != null && mPieces.Contains(piece))
            {
                // 段握柄：每次完成丢旧建新（一握柄 = 一次渲染）；写入整段后 Commit 把冻结音频交宿主驱动 effect。
                piece.Segment?.Dispose();
                piece.Segment = mContext.CreateAudioSegment((long)(rendered.StartTime * kSampleRate), rendered.Audio.Length, kSampleRate);
                piece.Segment.Write(0, rendered.Audio);
                piece.Segment.Commit();
                piece.Phonemes = rendered.Phonemes;
                piece.EnergyReadback = rendered.EnergyReadback;
            }
        }
        catch (Exception ex)
        {
            piece.Failed = true;
            piece.Error = ex.Message;
        }
        finally
        {
            piece.Synthesizing = false;
            StatusChanged?.Invoke();
        }
    }

    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];

    // 参数回显：各已合成块的 energy 回显段聚成 map（轨 id → 分段曲线），key 与 GetSynthesizedParameterConfigs 对齐。
    // 回显轨显隐由宿主标题栏管控；此处恒返回数据，隐藏时宿主不绘制——无害。
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters
    {
        get
        {
            var segments = new List<IReadOnlyList<Point>>();
            foreach (var piece in mPieces)
            {
                if (piece.EnergyReadback.Count > 0)
                    segments.Add(piece.EnergyReadback);
            }

            var map = new Map<string, SynthesizedParameter>();
            if (segments.Count > 0)
                map.Add("energy", new SynthesizedParameter { Segments = segments });
            return map;
        }
    }

    public IReadOnlyList<SynthesizedPhoneme> Phonemes
    {
        get
        {
            var result = new List<SynthesizedPhoneme>();
            foreach (var piece in mPieces)
            {
                result.AddRange(piece.Phonemes);
            }
            return result;
        }
    }

    public IReadOnlyList<SynthesisStatusSegment> GetStatus()
    {
        var result = new List<SynthesisStatusSegment>(mPieces.Count);
        foreach (var piece in mPieces)
        {
            var status = piece.Failed ? SynthesisSegmentStatus.Failed
                : piece.Synthesizing ? SynthesisSegmentStatus.Synthesizing
                : piece.Dirty || piece.Segment == null ? SynthesisSegmentStatus.Pending
                : SynthesisSegmentStatus.Synthesized;
            result.Add(new SynthesisStatusSegment
            {
                StartTime = piece.StartTime,
                EndTime = piece.EndTime,
                Status = status,
                Message = piece.Failed ? piece.Error : piece.Synthesizing ? "rendering" : null,
                Progress = piece.Synthesizing ? piece.Progress : 0,
            });
        }
        return result;
    }

    public event Action? StatusChanged;

    public void Dispose()
    {
        mNotesSubscription.Dispose();
        mContext.Notes.ItemAdded -= OnNotesStructureChanged;
        mContext.Notes.ItemRemoved -= OnNotesStructureChanged;
        mContext.PartProperties.Modified -= MarkAllDirtyAndResegment;
        mContext.Committed -= OnCommitted;
        mContext.Pitch.RangeModified -= OnRangeModified;
        mContext.PitchDeviation.RangeModified -= OnRangeModified;
        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
        mPieces.Clear();
    }

    // —— 合成（worker 线程，只读冻结快照；产物归属经 segment.Notes 索引对齐回活 note）——
    sealed record RenderResult(float[] Audio, double StartTime, List<SynthesizedPhoneme> Phonemes, List<Point> EnergyReadback);

    static RenderResult? Render(SynthesisSnapshot snapshot, IReadOnlyList<ILiveNote> origins,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var notes = snapshot.Notes;
        if (notes.Count == 0)
        {
            progress?.Report(1);
            return new RenderResult([], 0, [], []);
        }

        // note 可重叠（和弦）：起点恒为首 note（已按 StartTime 升序），但结束须取全体最大——
        // 同起点和弦的数据层序是 EndPos 降，notes[^1] 反而结束最早，不能当块尾。
        double startTime = notes[0].StartTime;
        double endTime = notes.Max(n => n.EndTime);
        int sampleCount = Math.Max(1, (int)((endTime - startTime) * kSampleRate));
        var audio = new float[sampleCount];
        var phonemes = new List<SynthesizedPhoneme>(notes.Count);

        for (int n = 0; n < notes.Count; n++)
        {
            if (cancellation.IsCancellationRequested)
                return null; // 取消是正常调度结局：不抛异常，产物保持上一版

            var note = notes[n];
            double noteStart = note.StartTime;
            double noteEnd = note.EndTime;
            int from = Math.Clamp((int)((noteStart - startTime) * kSampleRate), 0, sampleCount);
            int to = Math.Clamp((int)((noteEnd - startTime) * kSampleRate), 0, sampleCount);

            // 双通道音高消费（参照实现）：finalPitch = resolve(Pitch) + PitchDeviation——
            // Pitch 钉死区用用户曲线、NaN 自由区回退 note 音高，再叠加偏差（vibrato 由此在
            // 未绘制区域同样生效）。控制率 100Hz 采样后逐 sample 线性插值、相位积分调频。
            int controlCount = Math.Max(2, (int)((noteEnd - noteStart) * kControlRate) + 1);
            var controlTimes = new double[controlCount];
            for (int c = 0; c < controlCount; c++)
            {
                controlTimes[c] = noteStart + (noteEnd - noteStart) * c / (controlCount - 1);
            }
            var pitchValues = snapshot.Pitch.Evaluator.Evaluate(controlTimes);
            var deviation = snapshot.PitchDeviation.Evaluator.Evaluate(controlTimes);
            for (int c = 0; c < controlCount; c++)
            {
                pitchValues[c] = (double.IsNaN(pitchValues[c]) ? note.Pitch : pitchValues[c]) + deviation[c];
            }

            // attack/release 线性包络：note 边界处波形从 0 渐入/渐出，消除截断造成的爆音（"啪"声）。
            // 渐入/渐出各取设定时长与半个 note 长度的较小者，短音符也不会重叠（attack==0 时不进渐变分支，无除零）。
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
                audio[i] += (float)(0.2 * envelope * Math.Sin(phase)); // 混音叠加：重叠 note（和弦）各自发声而非互相覆盖
            }

            phonemes.Add(new SynthesizedPhoneme
            {
                Symbol = string.IsNullOrEmpty(note.Lyric) ? "la" : note.Lyric,
                StartTime = noteStart,
                EndTime = noteEnd,
                Note = origins[n],                       // 索引对齐：快照产物归属回活 note
                StretchWeight = noteEnd - noteStart,     // 无音韵学知识：权重=时长，退化均匀缩放
            });
            progress?.Report((double)(n + 1) / notes.Count);
        }

        // 参数回显（energy）：本参照实现产出一条「引擎实际施加的 energy」分段曲线，与音频/音高同一秒时间系，
        // 供宿主作只读回显轨绘制。此处用一条确定性正弦波形（10..90，落在 energy 的 0..100 域内）驱动回显路径。
        var energy = new List<Point>();
        double duration = Math.Max(1e-6, endTime - startTime);
        int energyCount = Math.Max(2, (int)(duration * 20)); // 20Hz 采样
        for (int g = 0; g < energyCount; g++)
        {
            double t = startTime + duration * g / (energyCount - 1);
            double v = 50 + 40 * Math.Sin(2 * Math.PI * (t - startTime) / duration);
            energy.Add(new Point(t, v));
        }

        return new RenderResult(audio, startTime, phonemes, energy);
    }

    // —— 分块（数据线程；按 note 间隙分块，note 集等价的块保留缓存与状态）——
    void Resegment()
    {
        mNeedResegment = false;

        // 按 note 间隙分块；note 可重叠（和弦），故以"组内最大结束"判间隙，而非上一 note 的结束
        //（同起点和弦里上一 note 可能结束更早，用它会把仍在响的长音错误地切出去）。
        var groups = new List<List<ILiveNote>>();
        List<ILiveNote>? current = null;
        double groupMaxEnd = 0;
        foreach (var note in mContext.Notes)
        {
            if (current == null || note.StartTime.Value > groupMaxEnd)
            {
                current = new List<ILiveNote>();
                groups.Add(current);
                groupMaxEnd = note.EndTime.Value;
            }
            else
            {
                groupMaxEnd = Math.Max(groupMaxEnd, note.EndTime.Value);
            }
            current.Add(note);
        }

        var newPieces = new List<Piece>(groups.Count);
        foreach (var notes in groups)
        {
            double pieceEnd = notes.Max(n => n.EndTime.Value); // 块尾 = 组内最大结束（重叠安全）
            var existing = mPieces.FirstOrDefault(piece => piece.Notes.SequenceEqual(notes));
            if (existing != null)
            {
                mPieces.Remove(existing);
                existing.StartTime = notes[0].StartTime.Value;
                existing.EndTime = pieceEnd;
                newPieces.Add(existing);
            }
            else
            {
                newPieces.Add(new Piece
                {
                    Notes = notes,
                    StartTime = notes[0].StartTime.Value,
                    EndTime = pieceEnd,
                    Dirty = true,
                });
            }
        }

        // 未被复用的旧块（其 note 集已不存在）：释放段握柄，宿主丢对应 effect 缓存。
        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
        mPieces.Clear();
        mPieces.AddRange(newPieces);
        StatusChanged?.Invoke();
    }

    void SubscribeNote(ILiveNote note)
    {
        void handler()
        {
            foreach (var piece in mPieces)
            {
                if (piece.Notes.Contains(note))
                {
                    piece.Dirty = true;
                    piece.Failed = false;
                }
            }
            mNeedResegment = true;
        }
        mNoteHandlers[note] = handler;
        note.StartTime.Modified += handler;
        note.EndTime.Modified += handler;
        note.Pitch.Modified += handler;
        note.Lyric.Modified += handler;
        note.Phonemes.Modified += handler;
        note.Properties.Modified += handler;
    }

    void UnsubscribeNote(ILiveNote note)
    {
        if (!mNoteHandlers.Remove(note, out var handler))
            return;

        note.StartTime.Modified -= handler;
        note.EndTime.Modified -= handler;
        note.Pitch.Modified -= handler;
        note.Lyric.Modified -= handler;
        note.Phonemes.Modified -= handler;
        note.Properties.Modified -= handler;
    }

    void OnNotesStructureChanged(ILiveNote note) => mNeedResegment = true;

    void MarkAllDirtyAndResegment()
    {
        foreach (var piece in mPieces)
        {
            piece.Dirty = true;
            piece.Failed = false;
        }
        mNeedResegment = true;
    }

    void OnCommitted()
    {
        if (mNeedResegment)
            Resegment();
    }

    void OnRangeModified(double startTime, double endTime)
    {
        foreach (var piece in mPieces)
        {
            if (piece.EndTime < startTime || piece.StartTime > endTime)
                continue;

            piece.Dirty = true;
            piece.Failed = false;
        }
        StatusChanged?.Invoke();
    }

    sealed class Piece
    {
        public required IReadOnlyList<ILiveNote> Notes;
        public double StartTime;
        public double EndTime;
        public bool Dirty;
        public bool Failed;
        public bool Synthesizing;
        public string? Error;
        public double Progress;
        public IAudioSegment? Segment;
        public IReadOnlyList<SynthesizedPhoneme> Phonemes = [];
        public IReadOnlyList<Point> EnergyReadback = [];
    }

    const int kSampleRate = 44100;
    const int kControlRate = 100;                            // 音高控制率（Hz）
    const int kAttackSamples = (int)(0.008 * kSampleRate);   // 8ms 渐入
    const int kReleaseSamples = (int)(0.012 * kSampleRate);  // 12ms 渐出

    readonly ISynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    readonly Dictionary<ILiveNote, Action> mNoteHandlers = new();
    readonly List<Piece> mPieces = new();
    bool mNeedResegment;
}
