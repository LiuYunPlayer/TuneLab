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

[VoiceEngine("TLTestVoiceV1")]
public sealed class TestVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

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
        mNotesSubscription = TuneLab.Foundation.NotifiableExtensions.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.ItemAdded += OnNotesStructureChanged;
        context.Notes.ItemRemoved += OnNotesStructureChanged;
        context.PartProperties.Modified += MarkAllDirtyAndResegment;
        context.BatchEnd += OnBatchEnd;
        context.Pitch.RangeModified += OnRangeModified;
        context.PitchDeviation.RangeModified += OnRangeModified;
        if (context.TryGetAutomation("Growl", out var growl))
            growl.RangeModified += OnRangeModified;

        mNeedResegment = true;
    }

    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs() => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> GetPiecewiseAutomationConfigs() => mPiecewiseAutomationConfigs;
    public ObjectConfig GetPartConfig(IPropertyContext context) => new() { Properties = mPartProperties };
    public ObjectConfig GetNoteConfig(IPropertyContext context) => new() { Properties = mNoteProperties };

    // —— 调度：窗内第一个脏块的纯值边界（peek 廉价）——
    public SynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        return FindNextDirtyPiece(startTime, endTime) is { } piece
            ? new SynthesisSegment(piece.StartTime, piece.EndTime)
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

    public async Task SynthesizeNext(SynthesisSegment segment,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (FindNextDirtyPiece(segment.StartTime, segment.EndTime) is not { } piece)
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
            progress?.Report(p);
            StatusChanged?.Invoke();
        });

        try
        {
            var rendered = await Task.Run(() => Render(snapshot, piece.Notes, report, cancellation), CancellationToken.None);
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

    // —— 音频产物（多块按全局时间轴对齐相加；协议：全局 0 时刻 = 采样点 0）——
    public int SampleRate => kSampleRate;

    public void ReadAudio(long offset, int count, float[] dst)
    {
        foreach (var piece in mPieces)
        {
            if (piece.Audio is not { } audio)
                continue;

            long audioOffset = (long)(piece.AudioStart * kSampleRate);
            long from = Math.Max(offset, audioOffset);
            long to = Math.Min(offset + count, audioOffset + audio.Length);
            for (long i = from; i < to; i++)
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
        mContext.BatchEnd -= OnBatchEnd;
        mContext.Pitch.RangeModified -= OnRangeModified;
        mContext.PitchDeviation.RangeModified -= OnRangeModified;
        mPieces.Clear();
    }

    // —— 合成（worker 线程，只读冻结快照；产物归属经 segment.Notes 索引对齐回活 note）——
    sealed record RenderResult(float[] Audio, double StartTime, List<SynthesizedPhoneme> Phonemes);

    static RenderResult? Render(SynthesisSnapshot snapshot, IReadOnlyList<ISynthesisNote> origins,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var notes = snapshot.Notes;
        if (notes.Count == 0)
        {
            progress?.Report(1);
            return new RenderResult([], 0, []);
        }

        double startTime = notes[0].StartTime;
        double endTime = notes[^1].EndTime;
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
            var pitchValues = snapshot.Pitch.Evaluate(controlTimes);
            var deviation = snapshot.PitchDeviation.Evaluate(controlTimes);
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
                audio[i] = (float)(0.2 * envelope * Math.Sin(phase));
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
            if (current == null || previous == null || note.StartTime.Value > previous.EndTime.Value)
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
                existing.StartTime = notes[0].StartTime.Value;
                existing.EndTime = notes[^1].EndTime.Value;
                newPieces.Add(existing);
            }
            else
            {
                newPieces.Add(new Piece
                {
                    Notes = notes,
                    StartTime = notes[0].StartTime.Value,
                    EndTime = notes[^1].EndTime.Value,
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
        note.StartTime.Modified += handler;
        note.EndTime.Modified += handler;
        note.Pitch.Modified += handler;
        note.Lyric.Modified += handler;
        note.Phonemes.Modified += handler;
        note.Properties.Modified += handler;
    }

    void UnsubscribeNote(ISynthesisNote note)
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

    const int kSampleRate = 44100;
    const int kControlRate = 100;                            // 音高控制率（Hz）
    const int kAttackSamples = (int)(0.008 * kSampleRate);   // 8ms 渐入
    const int kReleaseSamples = (int)(0.012 * kSampleRate);  // 12ms 渐出

    readonly ISynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    readonly Dictionary<ISynthesisNote, Action> mNoteHandlers = new();
    readonly List<Piece> mPieces = new();
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, PiecewiseAutomationConfig> mPiecewiseAutomationConfigs = new();
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
    bool mNeedResegment;
}
