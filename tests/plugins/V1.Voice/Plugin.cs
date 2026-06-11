using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.ControllerConfigs;
using TuneLab.SDK.Voice;

namespace TuneLab.TestPlugins.V1Voice;

// V1 voice 测试引擎（会话模型的原生参照实现）：2 个声库；按 note 间隙分块、分块账本托管
// 失效与产物；合成时按每个 note 的音高填一段正弦，并产出按出身 note 归属的扁平 phoneme。
// 用于验证：引擎注册、声库列表、CreateSession、分块状态带（多段着色/进度）、变更标脏增量重合成、
// snapshot.Notes 与 segment.Notes 的索引对齐契约（产物归属回活 note）。

[VoiceEngine("TLTestVoiceV1")]
public sealed class TestVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public void Init()
    {
        mVoiceInfos.Add("v1-alice", new VoiceSourceInfo { Name = "Alice (V1 Test)", Description = "Test voice Alice" });
        mVoiceInfos.Add("v1-bob", new VoiceSourceInfo { Name = "Bob (V1 Test)", Description = "Test voice Bob" });
    }

    public void Destroy() { }

    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context) => new TestSession(context);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

public sealed class TestSession : ISynthesisSession
{
    public TestSession(ISynthesisContext context)
    {
        mContext = context;
        mNoteProperties.Add("tension", new SliderConfig { DefaultValue = 0, MinValue = -1, MaxValue = 1 });
        // 自定义自动化参数名避开宿主保留名（Volume/VibratoEnvelope 等内置项）。
        mAutomationConfigs.Add("Growl", new AutomationConfig { DisplayText = "Growl", DefaultValue = 0, MinValue = 0, MaxValue = 100, Color = "#E5A573" });

        // 变更接线（数据线程，handler 只做廉价标脏；重活延迟到 BatchEnd 重分块）：
        // note 字段变化 → 标脏所在块 + 待重分块；增删 → 待重分块；
        // 曲线区间变化 → 标脏相交块；时基/part 属性 → 全部标脏。
        mNotesSubscription = TuneLab.Primitives.Event.NotifiableExtension.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.ItemAdded += OnNotesStructureChanged;
        context.Notes.ItemRemoved += OnNotesStructureChanged;
        context.PartProperties.Modified += MarkAllDirtyAndResegment;
        context.TimingModified += MarkAllDirtyAndResegment;
        context.BatchEnd += OnBatchEnd;
        context.Pitch.RangeModified += OnRangeModified;
        if (context.TryGetAutomation("Growl", out var growl))
            growl.RangeModified += OnRangeModified;

        mNeedResegment = true;
    }

    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> PiecewiseAutomationConfigs { get; } = new OrderedMap<string, PiecewiseAutomationConfig>();
    public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    // —— 调度：窗内第一个脏块（peek 廉价，token 只记引用不深拷）——
    public ISynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        if (mNeedResegment)
            Resegment();

        foreach (var piece in mPieces)
        {
            if (!piece.Dirty || piece.Failed || piece.Synthesizing)
                continue;

            if (piece.EndTime < startTime || piece.StartTime > endTime)
                continue;

            return new Segment(piece);
        }
        return null;
    }

    public async Task SynthesizeNext(ISynthesisSegment segment, ISynthesisSnapshot snapshot,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (segment is not Segment token || !mPieces.Contains(token.Piece))
            return;

        var piece = token.Piece;
        piece.Dirty = false; // 合成期间到达的新变更会重新标脏，完成后自然重排
        piece.Synthesizing = true;
        piece.Progress = 0;
        StatusChanged?.Invoke();

        // Progress<T> 捕获数据线程上下文：worker 的进度上报 marshal 回来落账再转发宿主。
        var report = new Progress<double>(p =>
        {
            piece.Progress = p;
            progress?.Report(p);
            StatusChanged?.Invoke();
        });

        try
        {
            var rendered = await Task.Run(() => Render(snapshot, segment.Notes, report, cancellation), CancellationToken.None);
            if (rendered != null && mPieces.Contains(piece))
            {
                piece.Audio = rendered.Audio;
                piece.AudioStart = rendered.StartTime;
                piece.Phonemes = rendered.Phonemes;
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

    // —— 音频产物：分块音频拼成单一时间线 ——
    public int SampleRate => kSampleRate;

    public double StartTime
    {
        get
        {
            double start = double.MaxValue;
            foreach (var piece in mPieces)
            {
                if (piece.Audio != null)
                    start = Math.Min(start, piece.AudioStart);
            }
            return start == double.MaxValue ? 0 : start;
        }
    }

    public int SampleCount
    {
        get
        {
            double start = StartTime;
            double end = start;
            foreach (var piece in mPieces)
            {
                if (piece.Audio is { } audio)
                    end = Math.Max(end, piece.AudioStart + (double)audio.Length / kSampleRate);
            }
            return (int)((end - start) * kSampleRate);
        }
    }

    public void ReadAudio(int offset, int count, float[] dst)
    {
        double sessionStart = StartTime;
        foreach (var piece in mPieces)
        {
            if (piece.Audio is not { } audio)
                continue;

            int audioOffset = (int)((piece.AudioStart - sessionStart) * kSampleRate);
            int from = Math.Max(offset, audioOffset);
            int to = Math.Min(offset + count, audioOffset + audio.Length);
            for (int i = from; i < to; i++)
            {
                dst[i - offset] += audio[i - audioOffset];
            }
        }
    }

    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];
    public IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedParameters { get; } = new Map<string, IReadOnlyList<IReadOnlyList<Point>>>();

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
                : piece.Dirty || piece.Audio == null ? SynthesisSegmentStatus.Pending
                : SynthesisSegmentStatus.Synthesized;
            result.Add(new SynthesisStatusSegment
            {
                StartTime = piece.StartTime,
                EndTime = piece.EndTime,
                Status = status,
                Message = piece.Failed ? piece.Error : piece.Synthesizing ? "rendering" : null,
                Progress = piece.Synthesizing ? piece.Progress : null,
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
        mContext.TimingModified -= MarkAllDirtyAndResegment;
        mContext.BatchEnd -= OnBatchEnd;
        mContext.Pitch.RangeModified -= OnRangeModified;
        mPieces.Clear();
    }

    // —— 合成（worker 线程，只读冻结快照；产物归属经 segment.Notes 索引对齐回活 note）——
    sealed record RenderResult(float[] Audio, double StartTime, List<SynthesizedPhoneme> Phonemes);

    static RenderResult? Render(ISynthesisSnapshot snapshot, IReadOnlyList<ISynthesisNote> origins,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var notes = snapshot.Notes;
        if (notes.Count == 0)
        {
            progress?.Report(1);
            return new RenderResult([], 0, []);
        }

        double startTime = notes[0].StartPosition.Seconds;
        double endTime = notes[^1].EndPosition.Seconds;
        int sampleCount = Math.Max(1, (int)((endTime - startTime) * kSampleRate));
        var audio = new float[sampleCount];
        var phonemes = new List<SynthesizedPhoneme>(notes.Count);

        for (int n = 0; n < notes.Count; n++)
        {
            if (cancellation.IsCancellationRequested)
                return null; // 取消是正常调度结局：不抛异常，产物保持上一版

            var note = notes[n];
            double noteStart = note.StartPosition.Seconds;
            double noteEnd = note.EndPosition.Seconds;
            double freq = 440.0 * Math.Pow(2, (note.Pitch - 69) / 12.0);
            int from = Math.Clamp((int)((noteStart - startTime) * kSampleRate), 0, sampleCount);
            int to = Math.Clamp((int)((noteEnd - startTime) * kSampleRate), 0, sampleCount);
            // attack/release 线性包络：note 边界处波形从 0 渐入/渐出，消除截断造成的爆音（"啪"声）。
            // 渐入/渐出各取设定时长与半个 note 长度的较小者，短音符也不会重叠（attack==0 时不进渐变分支，无除零）。
            int length = to - from;
            int attack = Math.Min(kAttackSamples, length / 2);
            int release = Math.Min(kReleaseSamples, length / 2);
            for (int i = from; i < to; i++)
            {
                int pos = i - from;
                double envelope = pos < attack ? (double)pos / attack
                    : pos >= length - release ? (double)(length - pos) / release
                    : 1.0;
                audio[i] = (float)(0.2 * envelope * Math.Sin(2 * Math.PI * freq * pos / kSampleRate));
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

        return new RenderResult(audio, startTime, phonemes);
    }

    // —— 分块（数据线程；按 note 间隙分块，note 集等价的块保留缓存与状态）——
    void Resegment()
    {
        mNeedResegment = false;

        var groups = new List<List<ISynthesisNote>>();
        List<ISynthesisNote>? current = null;
        ISynthesisNote? previous = null;
        foreach (var note in mContext.Notes)
        {
            if (current == null || previous == null || note.StartPosition.Value.Seconds > previous.EndPosition.Value.Seconds)
            {
                current = new List<ISynthesisNote>();
                groups.Add(current);
            }
            current.Add(note);
            previous = note;
        }

        var newPieces = new List<Piece>(groups.Count);
        foreach (var notes in groups)
        {
            var existing = mPieces.FirstOrDefault(piece => piece.Notes.SequenceEqual(notes));
            if (existing != null)
            {
                mPieces.Remove(existing);
                existing.StartTime = notes[0].StartPosition.Value.Seconds;
                existing.EndTime = notes[^1].EndPosition.Value.Seconds;
                newPieces.Add(existing);
            }
            else
            {
                newPieces.Add(new Piece
                {
                    Notes = notes,
                    StartTime = notes[0].StartPosition.Value.Seconds,
                    EndTime = notes[^1].EndPosition.Value.Seconds,
                    Dirty = true,
                });
            }
        }

        mPieces.Clear();
        mPieces.AddRange(newPieces);
        StatusChanged?.Invoke();
    }

    void SubscribeNote(ISynthesisNote note)
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
        note.StartPosition.Modified += handler;
        note.EndPosition.Modified += handler;
        note.Pitch.Modified += handler;
        note.Lyric.Modified += handler;
        note.Phonemes.Modified += handler;
        note.Properties.Modified += handler;
    }

    void UnsubscribeNote(ISynthesisNote note)
    {
        if (!mNoteHandlers.Remove(note, out var handler))
            return;

        note.StartPosition.Modified -= handler;
        note.EndPosition.Modified -= handler;
        note.Pitch.Modified -= handler;
        note.Lyric.Modified -= handler;
        note.Phonemes.Modified -= handler;
        note.Properties.Modified -= handler;
    }

    void OnNotesStructureChanged(ISynthesisNote note) => mNeedResegment = true;

    void MarkAllDirtyAndResegment()
    {
        foreach (var piece in mPieces)
        {
            piece.Dirty = true;
            piece.Failed = false;
        }
        mNeedResegment = true;
    }

    void OnBatchEnd()
    {
        if (mNeedResegment)
            Resegment();
    }

    void OnRangeModified(double startTick, double endTick)
    {
        double startTime = mContext.Timing.ToSeconds(startTick);
        double endTime = mContext.Timing.ToSeconds(endTick);
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
        public required IReadOnlyList<ISynthesisNote> Notes;
        public double StartTime;
        public double EndTime;
        public bool Dirty;
        public bool Failed;
        public bool Synthesizing;
        public string? Error;
        public double Progress;
        public float[]? Audio;
        public double AudioStart;
        public IReadOnlyList<SynthesizedPhoneme> Phonemes = [];
    }

    sealed class Segment(Piece piece) : ISynthesisSegment
    {
        public Piece Piece => piece;
        public double StartTime => piece.StartTime;
        public double EndTime => piece.EndTime;
        public IReadOnlyList<ISynthesisNote> Notes => piece.Notes;
        public double StartTick => piece.Notes[0].StartPosition.Value.Tick;
        public double EndTick => piece.Notes[^1].EndPosition.Value.Tick;
    }

    const int kSampleRate = 44100;
    const int kAttackSamples = (int)(0.008 * kSampleRate);   // 8ms 渐入
    const int kReleaseSamples = (int)(0.012 * kSampleRate);  // 12ms 渐出

    readonly ISynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    readonly Dictionary<ISynthesisNote, Action> mNoteHandlers = new();
    readonly List<Piece> mPieces = new();
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
    bool mNeedResegment;
}
