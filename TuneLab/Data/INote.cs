using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.Utils;
using TuneLab.SDK;
using PhonemeInfo = TuneLab.SDK.PhonemeInfo;
using NoteInfo = TuneLab.SDK.NoteInfo;

namespace TuneLab.Data;

// 宿主业务层 note。不直接实现 SDK 的 ILiveNote——插件经会话级 context 的 note 代理
// 订阅（中间层隔离）；本接口只服务宿主自身（编辑/UI/序列化）。
internal interface INote : IDataObject<NoteInfo>, ISelectable, ILinkedNode<INote>
{
    new INote? Next { get; }
    new INote? Last { get; }
    IMidiPart Part { get; }
    IDataProperty<double> Pos { get; }
    IDataProperty<double> Dur { get; }
    IDataProperty<int> Pitch { get; }
    IDataProperty<string> Lyric { get; }
    IDataProperty<string> Pronunciation { get; }
    DataPropertyObject Properties { get; }
    IDataObjectList<IPhoneme> Phonemes { get; }
    SynthesizedPhoneme[]? SynthesizedPhonemes { get; set; }
    IReadOnlyCollection<string> Pronunciations { get; }

    double StartTime => Part.TempoManager.GetTime(this.GlobalStartPos());
    double EndTime => Part.TempoManager.GetTime(this.GlobalEndPos());

    // 用户钉死音素的显示形（绝对秒时间线，与合成产物同一时间系），供钢琴窗音素带显示/编辑。
    // 与喂给插件的快照（SynthesisNoteProxy.Phonemes）共用 EffectivePinnedPhonemeTimes，
    // 保证显示与合成对同一份钉死数据的解释一致。
    IReadOnlyList<SynthesizedPhoneme> PinnedPhonemes
    {
        get
        {
            var times = EffectivePinnedPhonemeTimes();
            double start = StartTime;
            var list = new SynthesizedPhoneme[times.Count];
            for (int i = 0; i < times.Count; i++)
                list[i] = new SynthesizedPhoneme()
                {
                    Symbol = Phonemes[i].Symbol.Value,
                    StartTime = start + times[i].Start,
                    EndTime = start + times[i].End,
                };
            return list;
        }
    }

    private double PhonemeStartTime => Phonemes.IsEmpty() ? 0 : Phonemes.ConstFirst().StartTime.Value;
    private double PhonemeEndTime => Phonemes.IsEmpty() ? 0 : Phonemes.ConstLast().EndTime.Value;
    // 越界音素的显示压缩比：负向引导音素被前一个 note 占用的空间挤压时按比例缩进。
    // 邻居取 part 内相邻 note（足够远时比例自然退化为 1，行为与旧"段内邻居"基本等价）。
    public double StartPhonemeRatio
    {
        get
        {
            if (Last == null)
                return 1;

            double all = -PhonemeStartTime;
            if (all <= 0)
                return 1;

            return Math.Min(1, (StartTime - Last.StartTime) / all);
        }
    }

    // 正向音素时间线的终点边界（绝对秒）：默认到 note 终点；下一 note 占用时收到其首音素起点
    // （限制在本 note 区间内），与 StartPhonemeRatio 的负向越界压缩对称。
    private double PhonemeEndBoundary
    {
        get
        {
            double end = EndTime;
            if (Next != null)
                end = (Next.Phonemes.IsEmpty() ?
                    (Next.SynthesizedPhonemes == null || Next.SynthesizedPhonemes.IsEmpty() ? Next.StartTime : Next.SynthesizedPhonemes.ConstFirst().StartTime) :
                    Next.PhonemeStartTime + Next.StartTime).Limit(StartTime, EndTime);
            return end;
        }
    }

    // 钉死音素的 effective note 相对时间（秒），与 Phonemes 同序。单一真源：显示与合成快照共用。
    // 正向（note 内）余量按 StretchWeight 重分配——new_dᵢ = dᵢ + Δ×(wᵢ/Σwⱼ)，辅音 w=0、元音 w=1
    // 则 note 伸缩量全进元音，末音素恰好落在正向终点边界；Σw≤0（旧工程缺省 / 未设权重）退化为
    // 均匀缩放（与旧行为一致，零回归）。负向引导音素按 StartPhonemeRatio 压缩。
    // 单调钳制：极端缩短（note 短于辅音总长）时防止边界反相。
    public IReadOnlyList<(double Start, double End)> EffectivePinnedPhonemeTimes()
    {
        int n = Phonemes.Count;
        if (n == 0)
            return Array.Empty<(double, double)>();

        double startRatio = StartPhonemeRatio;
        double available = PhonemeEndBoundary - StartTime;   // 正向预算（秒）
        double nominalSpan = PhonemeEndTime;                 // 正向 nominal 总跨度
        double delta = available - nominalSpan;

        double sumW = 0;
        for (int i = 0; i < n; i++) sumW += Phonemes[i].Weight.Value;
        bool weighted = sumW > 0;
        double uniform = nominalSpan > 0 ? available / nominalSpan : 1;

        // n+1 个边界：b[0]=首音素起点，b[k]=音素 k-1 终点（=音素 k 起点，契约：音素连续）。
        var eff = new double[n + 1];
        double weightBefore = 0;   // 边界 k 之前累计权重 = Σ w[0..k-1]
        for (int k = 0; k <= n; k++)
        {
            double t = k == 0 ? Phonemes[0].StartTime.Value : Phonemes[k - 1].EndTime.Value;
            double mapped = t <= 0
                ? t * startRatio
                : (weighted ? t + delta * (weightBefore / sumW) : t * uniform);
            eff[k] = k == 0 ? mapped : Math.Max(mapped, eff[k - 1]);
            if (k < n) weightBefore += Phonemes[k].Weight.Value;
        }

        var result = new (double, double)[n];
        for (int i = 0; i < n; i++) result[i] = (eff[i], eff[i + 1]);
        return result;
    }

    // EffectivePinnedPhonemeTimes 的逆：把拖拽得到的 effective note 相对时间反解为应写回的
    // nominal 偏移。boundaryIndex 为被拖边界（音素 boundaryIndex-1 终点 / 音素 boundaryIndex 起点，
    // ==Count 表示末音素尾）。正向逆为线性平移、负向逆为按比例还原。
    public double NominalPhonemeTime(int boundaryIndex, double effRel)
    {
        if (effRel <= 0)
        {
            double startRatio = StartPhonemeRatio;
            return startRatio == 0 ? effRel : effRel / startRatio;
        }

        double available = PhonemeEndBoundary - StartTime;
        double nominalSpan = PhonemeEndTime;
        double delta = available - nominalSpan;
        int n = Phonemes.Count;

        double sumW = 0;
        for (int i = 0; i < n; i++) sumW += Phonemes[i].Weight.Value;
        if (sumW > 0)
        {
            double weightBefore = 0;
            for (int i = 0; i < boundaryIndex && i < n; i++) weightBefore += Phonemes[i].Weight.Value;
            return effRel - delta * (weightBefore / sumW);
        }

        double uniform = nominalSpan > 0 ? available / nominalSpan : 1;
        return uniform == 0 ? effRel : effRel / uniform;
    }
}

internal static class INoteExtension
{
    public static double StartPos(this INote note)
    {
        return note.Pos.Value;
    }

    public static double EndPos(this INote note)
    {
        return note.Pos.Value + note.Dur.Value;
    }

    public static double GlobalStartPos(this INote note)
    {
        return note.Part.Pos.Value + note.StartPos();
    }

    public static double GlobalEndPos(this INote note)
    {
        return note.Part.Pos.Value + note.EndPos();
    }

    public static INote SplitAt(this INote note, double pos)
    {
        note.Part.BeginMergeDirty();
        var newNote = note.Part.CreateNote(new NoteInfo() { Pos = pos, Dur = note.EndPos() - pos, Pitch = note.Pitch.Value, Lyric = "-" });
        note.Part.MoveNote(note, () => note.Dur.Set(pos - note.StartPos()));
        note.Part.InsertNote(newNote);
        note.Part.EndMergeDirty();
        note.Part.Notes.DeselectAllItems();
        return newNote;
    }

    public static void LockPhonemes(this INote note)
    {
        if (!note.Phonemes.IsEmpty())
            return;

        if (note.SynthesizedPhonemes == null)
            return;

        if (note.SynthesizedPhonemes.IsEmpty())
            return;

        // 锁定 = 把合成产物（时长 + 伸缩权重）整体固定为用户数据。
        var startTime = note.StartTime;
        foreach (var phoneme in note.SynthesizedPhonemes)
        {
            note.Phonemes.Add(Phoneme.Create(new PhonemeInfo()
            {
                StartTime = phoneme.StartTime - startTime,
                EndTime = phoneme.EndTime - startTime,
                Symbol = phoneme.Symbol,
                Weight = phoneme.StretchWeight,
            }));
        }
    }

    public static string? FinalPronunciation(this INote note)
    {
        if (!string.IsNullOrEmpty(note.Pronunciation.Value))
            return note.Pronunciation.Value;

        return note.Pronunciations.FirstOrDefault();
    }
}
