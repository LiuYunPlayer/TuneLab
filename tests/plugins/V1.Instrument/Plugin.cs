using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Instrument;

// V1 instrument 测试引擎（多声部音源的原生参照实现）：一个引擎下挂两个音色（sine / square，演示 InstrumentId 选音色 /
// 容器目录），按 note 间隙分块、分块账本托管失效，合成时对块内**每个 note**（含重叠 / 和弦）按其整数 pitch 叠加发声。
// 用于验证：instrument 注册、音色目录、CreateSession、满末 note（不去重叠）、重叠 note 混音、分块状态带、
// 增量重合成、effect 链对 instrument 输出生效。无歌词 / 音素 / pitch 曲线（instrument v1 纯按 note pitch 发声）。

public sealed class TestInstrumentEngine : IInstrumentSynthesisEngine
{
    public IReadOnlyOrderedMap<string, InstrumentSourceInfo> InstrumentSourceInfos => mInfos;

    public void Init()
    {
        // 两个音色：演示「一个引擎 = 容器，多个音色按 id 选」（容器式发布的退化最小例）。
        mInfos.Add("sine", new InstrumentSourceInfo { Name = "Poly Sine (V1 Test)", Description = "Polyphonic sine synth" });
        mInfos.Add("square", new InstrumentSourceInfo { Name = "Poly Square (V1 Test)", Description = "Polyphonic square synth" });
    }

    public void Destroy() { }

    public IInstrumentSynthesisSession CreateSession(IInstrumentSynthesisContext context) => new TestSession(context, context.InstrumentId);

    // 声明（引擎层、纯函数）：本参照不暴露额外可编辑轨 / 回显轨 / 属性面板（纯按 note pitch 发声）。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IInstrumentSynthesisPartPropertyContext context) => mEmptyConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IInstrumentSynthesisPartPropertyContext context) => mEmptyConfigs;
    public ObjectConfig GetPartPropertyConfig(IInstrumentSynthesisPartPropertyContext context) => mEmptyPanel;
    public ObjectConfig GetNotePropertyConfig(IInstrumentSynthesisNotePropertyContext context) => mEmptyPanel;

    readonly OrderedMap<string, InstrumentSourceInfo> mInfos = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> mEmptyConfigs = new();
    static readonly ObjectConfig mEmptyPanel = new() { Properties = new OrderedMap<PropertyKey, IControllerConfig>() };
}

public sealed class TestSession : IInstrumentSynthesisSession
{
    public TestSession(IInstrumentSynthesisContext context, string instrumentId)
    {
        mContext = context;
        mSquare = instrumentId == "square";

        // 变更接线（数据线程，handler 只做廉价标脏；重活延迟到 Committed 重分块）。instrument 无 pitch 曲线 / 音素。
        context.Notes.WhenAnyItem(n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Pitch.Modified, n => n.Properties.Modified)
            .Subscribe(MarkNoteDirty, mSubscriptions);
        context.Notes.ItemAdded.Subscribe(OnNotesStructureChanged, mSubscriptions);
        context.Notes.ItemRemoved.Subscribe(OnNotesStructureChanged, mSubscriptions);
        context.PartProperties.Modified.Subscribe(MarkAllDirtyAndResegment, mSubscriptions);
        context.Committed.Subscribe(OnCommitted);

        mNeedResegment = true;
    }

    public SynthesisRange? GetNextSegment(double startTime, double endTime)
        => FindNextDirtyPiece(startTime, endTime) is { } piece
            ? new SynthesisRange(piece.StartTime, piece.EndTime)
            : null;

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

    public async Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default)
    {
        if (FindNextDirtyPiece(startTime, endTime) is not { } piece)
            return;

        // 同步前缀（数据线程）拉取快照：notes 即本块全集（满末、可重叠）。automation 开窗按块范围。
        double windowEnd = piece.Notes.Max(n => n.EndTime.Value);
        var snapshot = mContext.GetSnapshot(piece.Notes, piece.Notes[0].StartTime.Value, windowEnd);

        piece.Dirty = false;
        piece.Synthesizing = true;
        piece.Progress = 0;
        mStatusChanged.Invoke();

        var report = new Progress<double>(p => { piece.Progress = p; mStatusChanged.Invoke(); });

        try
        {
            var rendered = await Task.Run(() => Render(snapshot, mSquare, report, cancellation), CancellationToken.None);
            if (rendered != null && mPieces.Contains(piece))
            {
                piece.Segment?.Dispose();
                piece.Segment = mContext.CreateAudioSegment((long)(rendered.StartTime * kSampleRate), rendered.Audio.Length, kSampleRate);
                piece.Segment.Write(0, rendered.Audio);
                piece.Segment.Commit();
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
            mStatusChanged.Invoke();
        }
    }

    // instrument 仅产音频 + 可选参数回显；本参照不声明回显轨，故恒空。
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mEmptyReadback;

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

    public IActionEvent SynthesizedParametersChanged => mSynthesizedParametersChanged;
    public IActionEvent StatusChanged => mStatusChanged;
    readonly ActionEvent mSynthesizedParametersChanged = new();
    readonly ActionEvent mStatusChanged = new();

    public void Dispose()
    {
        mSubscriptions.DisposeAll();
        mContext.Committed.Unsubscribe(OnCommitted);
        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
        mPieces.Clear();
    }

    // —— 合成（worker 线程，只读冻结快照）：块内每个 note（含重叠和弦）按其整数 pitch 叠加一段波形。 ——
    sealed record RenderResult(float[] Audio, double StartTime);

    static RenderResult? Render(InstrumentSynthesisSnapshot snapshot, bool square, IProgress<double>? progress, CancellationToken cancellation)
    {
        var notes = snapshot.Notes;
        if (notes.Count == 0)
        {
            progress?.Report(1);
            return new RenderResult([], 0);
        }

        double startTime = notes[0].StartTime;
        double endTime = notes.Max(n => n.EndTime);   // 满末（不去重叠）：块尾取全体最大
        int sampleCount = Math.Max(1, (int)((endTime - startTime) * kSampleRate));
        var audio = new float[sampleCount];

        for (int n = 0; n < notes.Count; n++)
        {
            if (cancellation.IsCancellationRequested)
                return null;   // 取消是正常调度结局：不抛、产物保持上一版

            var note = notes[n];
            int from = Math.Clamp((int)((note.StartTime - startTime) * kSampleRate), 0, sampleCount);
            int to = Math.Clamp((int)((note.EndTime - startTime) * kSampleRate), 0, sampleCount);   // 满末，重叠尾巴保留
            int length = to - from;
            if (length <= 0)
                continue;

            double freq = 440.0 * Math.Pow(2, (note.Pitch - 69) / 12.0);
            int attack = Math.Min(kAttackSamples, length / 2);
            int release = Math.Min(kReleaseSamples, length / 2);
            double phase = 0;
            double phaseInc = 2 * Math.PI * freq / kSampleRate;
            for (int i = from; i < to; i++)
            {
                int pos = i - from;
                double envelope = pos < attack ? (double)pos / attack
                    : pos >= length - release ? (double)(length - pos) / release
                    : 1.0;
                phase += phaseInc;
                double wave = square ? (Math.Sin(phase) >= 0 ? 1.0 : -1.0) : Math.Sin(phase);
                audio[i] += (float)(0.15 * envelope * wave);   // 混音叠加：重叠 note（和弦）各自发声
            }

            progress?.Report((double)(n + 1) / notes.Count);
        }

        return new RenderResult(audio, startTime);
    }

    // —— 分块（数据线程；按 note 间隙分块，重叠 note 同组——以"组内最大满末"判间隙）——
    void Resegment()
    {
        mNeedResegment = false;

        var groups = new List<List<IInstrumentSynthesisNote>>();
        List<IInstrumentSynthesisNote>? current = null;
        double groupMaxEnd = 0;
        foreach (var note in mContext.Notes)
        {
            if (current == null || note.StartTime.Value > groupMaxEnd)
            {
                current = new List<IInstrumentSynthesisNote>();
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
        foreach (var groupNotes in groups)
        {
            double pieceEnd = groupNotes.Max(n => n.EndTime.Value);
            var existing = mPieces.FirstOrDefault(piece => piece.Notes.SequenceEqual(groupNotes));
            if (existing != null)
            {
                mPieces.Remove(existing);
                existing.StartTime = groupNotes[0].StartTime.Value;
                existing.EndTime = pieceEnd;
                newPieces.Add(existing);
            }
            else
            {
                newPieces.Add(new Piece
                {
                    Notes = groupNotes,
                    StartTime = groupNotes[0].StartTime.Value,
                    EndTime = pieceEnd,
                    Dirty = true,
                });
            }
        }

        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
        mPieces.Clear();
        mPieces.AddRange(newPieces);
        mStatusChanged.Invoke();
    }

    void OnNotesStructureChanged(IInstrumentSynthesisNote note) => mNeedResegment = true;

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

    void MarkNoteDirty(IInstrumentSynthesisNote note)
    {
        foreach (var piece in mPieces)
        {
            if (!piece.Notes.Contains(note))
                continue;
            piece.Dirty = true;
            piece.Failed = false;
        }
        mNeedResegment = true;   // 边界变化可能改分组（重叠关系变）
    }

    sealed class Piece
    {
        public required IReadOnlyList<IInstrumentSynthesisNote> Notes;
        public double StartTime;
        public double EndTime;
        public bool Dirty;
        public bool Failed;
        public bool Synthesizing;
        public string? Error;
        public double Progress;
        public IAudioSegment? Segment;
    }

    const int kSampleRate = 44100;
    const int kAttackSamples = (int)(0.008 * kSampleRate);
    const int kReleaseSamples = (int)(0.012 * kSampleRate);

    static readonly Map<string, SynthesizedParameter> mEmptyReadback = new();

    readonly IInstrumentSynthesisContext mContext;
    readonly bool mSquare;
    readonly DisposableManager mSubscriptions = new();
    readonly List<Piece> mPieces = new();
    bool mNeedResegment;
}
