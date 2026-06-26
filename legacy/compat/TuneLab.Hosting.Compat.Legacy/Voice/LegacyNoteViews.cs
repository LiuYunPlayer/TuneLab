using System;
using System.Collections.Generic;
using TuneLab.SDK;
using LProp = TuneLab.Base.Properties;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把 V1 会话侧的 note（活代理/冻结快照）包装成老 ISynthesisNote 喂老引擎。
// 两形态对应老接口的两个消费场景：
//   LiveNoteView    —— Segment() 分片输入（数据线程，读活代理当前值）；
//   SnapshotNoteView —— CreateSynthesisTask 的数据输入（worker 线程，只读冻结快照）。
//
// 老接口与 V1 面的 note 边界同为全局秒（V1 全秒轴改造后），无需 tick↔秒换算。
//
// 后盖前单声部兜底：V1 数据层/SDK 面直传可重叠 note（和弦），但老引擎硬假定单声部、
// note 首尾相接不重叠。故这两个视图的 EndTime 一律钳到下一 note 的 StartTime
// （EndTime = Min(自身End, Next.StartTime)）——后起的 note 截断前一个的尾巴。
// 同 StartTime 的重叠（真和弦）按数据层序逐个退化：序为 EndPos 降（长者在前），
// 长者的 Next 即同起点的短者，钳后归零，最终只剩排在最后、EndTime 最靠前的那个存活。
// 这是 chord 支持引入前老 Note.EndTime 的原样行为，仅复刻进 compat、不外泄到新 SDK 面。

// 活视图包装：身份按 V1 note 代理缓存（同一代理恒得同一包装，分片 EqualsWith 依赖引用相等）。
internal sealed class LiveNoteViewCache(Func<VVoice.IVoiceSynthesisNote, LProp.PropertyObject> propertiesReader)
{
    public LiveNoteView Wrap(VVoice.IVoiceSynthesisNote origin)
    {
        if (!mViews.TryGetValue(origin, out var view))
        {
            view = new LiveNoteView(origin, this, propertiesReader);
            mViews.Add(origin, view);
        }
        return view;
    }

    public void Prune(IReadOnlyCollection<VVoice.IVoiceSynthesisNote> alive)
    {
        var dead = new List<VVoice.IVoiceSynthesisNote>();
        foreach (var kv in mViews)
        {
            if (!alive.Contains(kv.Key))
                dead.Add(kv.Key);
        }
        foreach (var key in dead)
        {
            mViews.Remove(key);
        }
    }

    readonly Dictionary<VVoice.IVoiceSynthesisNote, LiveNoteView> mViews = new(ReferenceEqualityComparer.Instance);
}

internal sealed class LiveNoteView(
    VVoice.IVoiceSynthesisNote origin,
    LiveNoteViewCache cache,
    Func<VVoice.IVoiceSynthesisNote, LProp.PropertyObject> propertiesReader) : LVoice.ISynthesisNote
{
    public VVoice.IVoiceSynthesisNote Origin => origin;

    public LVoice.ISynthesisNote? Next => origin.Next is { } next ? cache.Wrap(next) : null;
    public LVoice.ISynthesisNote? Last => origin.Last is { } last ? cache.Wrap(last) : null;
    public double StartTime => origin.StartTime.Value;
    // 后盖前钳位：尾巴不越过下一 note 起点（无下一 note 即整段保留）。
    public double EndTime => Math.Min(origin.EndTime.Value, origin.Next is { } next ? next.StartTime.Value : double.PositiveInfinity);
    public int Pitch => origin.Pitch.Value;
    public string Lyric => origin.Lyric.Value;
    // 按老声源的 NoteProperties 声明键现取（V1 订阅树外观不可枚举，键集来自声明）。
    public LProp.PropertyObject Properties => propertiesReader(origin);
    public IReadOnlyList<LVoice.SynthesizedPhoneme> Phonemes => LegacyNoteConvert.ToLegacyPinnedPhonemes(
        origin.Phonemes.Value, StartTime, EndTime);
}

// 快照包装：按段一次性建链（与 segment.Notes 索引对齐，Origin 留作产物归属的身份 token）。
internal sealed class SnapshotNoteView : LVoice.ISynthesisNote
{
    public VVoice.IVoiceSynthesisNote Origin { get; }

    public LVoice.ISynthesisNote? Next { get; private set; }
    public LVoice.ISynthesisNote? Last { get; private set; }
    public double StartTime { get; }
    public double EndTime { get; }
    public int Pitch => mNote.Pitch;
    public string Lyric => mNote.Lyric;
    public LProp.PropertyObject Properties { get; }
    public IReadOnlyList<LVoice.SynthesizedPhoneme> Phonemes { get; }

    public static IReadOnlyList<SnapshotNoteView> CreateChain(
        IReadOnlyList<VVoice.VoiceSynthesisNoteSnapshot> notes, IReadOnlyList<VVoice.IVoiceSynthesisNote> origins)
    {
        var views = new SnapshotNoteView[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            views[i] = new SnapshotNoteView(notes[i], origins[i]);
        }
        for (int i = 0; i + 1 < views.Length; i++)
        {
            views[i].Next = views[i + 1];
            views[i + 1].Last = views[i];
        }
        return views;
    }

    SnapshotNoteView(VVoice.VoiceSynthesisNoteSnapshot note, VVoice.IVoiceSynthesisNote origin)
    {
        mNote = note;
        Origin = origin;
        StartTime = note.StartTime;
        // 后盖前钳位（与 LiveNoteView 同口径）：构造在数据线程同步前缀，读活代理 origin.Next
        // 取全局下一 note 起点冻结成边界——用全局 Next 而非 piece 内链尾，跨 piece 重叠也一致。
        EndTime = Math.Min(note.EndTime, origin.Next is { } next ? next.StartTime.Value : double.PositiveInfinity);
        Properties = Conversion.PropertyConvert.ToLegacy(note.Properties);
        // 时长累积布局；末音素尾用有效末（EndTime，单声部钳位）；核起点恒在音符头。
        Phonemes = LegacyNoteConvert.ToLegacyPinnedPhonemes(note.Phonemes, StartTime, EndTime);
    }

    readonly VVoice.VoiceSynthesisNoteSnapshot mNote;
}

internal static class LegacyNoteConvert
{
    // V1 音素描述符（时长 + 权重 + IsLead，列表非空=整 note 钉死）→ 老接口的绝对秒列表。
    // 新 SDK 音素存「时长 + 权重 + IsLead」、位置由布局派生（去重叠 / 跨 note 压缩归全局布局 PhonemeLayout）；compat 侧按
    // 「本 note 时长累积布局」解析即可——单声部旧引擎按收到的钉死时序处理，跨 note 辅音簇压缩属新 SDK 精修、对老引擎不必要。
    //   · 前置分界线（核起点）= noteStart；IsLead 从分界线往左累积；核 + 后辅音往右、核(w>0)填充到 noteEndTime（有效末口径）。
    public static IReadOnlyList<LVoice.SynthesizedPhoneme> ToLegacyPinnedPhonemes(
        IReadOnlyList<VVoice.VoicePhoneme> phonemes, double noteStartTime, double noteEndTime)
    {
        int n = phonemes.Count;
        if (n == 0)
            return [];

        var pos = new double[n + 1];
        double leadBoundary = noteStartTime;
        int L = 0;
        while (L < n && phonemes[L].IsLead) L++;

        pos[L] = leadBoundary;
        for (int k = L - 1; k >= 0; k--) pos[k] = pos[k + 1] - Math.Max(0, phonemes[k].Duration);

        double rigidAfter = 0, elasticWeight = 0;
        for (int k = L; k < n; k++)
        {
            if (phonemes[k].StretchWeight > 0) elasticWeight += phonemes[k].StretchWeight;
            else rigidAfter += Math.Max(0, phonemes[k].Duration);
        }
        double elasticSpace = Math.Max(0, (noteEndTime - leadBoundary) - rigidAfter);
        double p = leadBoundary;
        for (int k = L; k < n; k++)
        {
            double w = phonemes[k].StretchWeight;
            double len = w > 0 ? (elasticWeight > 0 ? elasticSpace * (w / elasticWeight) : 0) : Math.Max(0, phonemes[k].Duration);
            pos[k] = p;
            p += len;
            pos[k + 1] = p;
        }

        var result = new List<LVoice.SynthesizedPhoneme>(n);
        for (int k = 0; k < n; k++)
            result.Add(new LVoice.SynthesizedPhoneme { Symbol = phonemes[k].Symbol, StartTime = pos[k], EndTime = pos[k + 1] });
        return result;
    }
}
