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
    public LVoice.ISynthesisNote? Last => origin.Previous is { } previous ? cache.Wrap(previous) : null;
    public double StartTime => origin.StartTime.Value;
    // 后盖前钳位：尾巴不越过下一 note 起点（无下一 note 即整段保留）。
    public double EndTime => Math.Min(origin.EndTime.Value, origin.Next is { } next ? next.StartTime.Value : double.PositiveInfinity);
    public int Pitch => origin.Pitch.Value;
    public string Lyric => origin.Lyric.Value;
    // 按老声源的 NoteProperties 声明键现取（V1 订阅树外观不可枚举，键集来自声明）。
    public LProp.PropertyObject Properties => propertiesReader(origin);
    public IReadOnlyList<LVoice.SynthesizedPhoneme> Phonemes => LegacyNoteConvert.ToLegacyPinnedPhonemes(
        origin.LeadingPhonemes.Value, origin.BodyPhonemes.Value, origin.BodyOffset.Value, StartTime, EndTime);
}

// 快照包装：按段一次性建链（与 segment.Notes 索引对齐，Origin 留作产物归属的身份 token）。
// 音素时序在 CreateChain 里对整链做一次联合布局（与宿主显示同一套 PhonemeLayout 跨 note 去重叠），
// 老引擎收到的长度位置 == 用户看到的布局——老版本 TuneLab 同样是宿主侧算好长度再传引擎。
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
    public IReadOnlyList<LVoice.SynthesizedPhoneme> Phonemes { get; private set; } = [];

    // 整链联合布局。由 baselineEcho 区分两 pass：
    //   · baselineEcho == null（**基线 pass**）：全部 note 喂空、老引擎自然预测（宿主拿回显建缓存）。
    //   · baselineEcho != null（**真实 pass**）：钉死 note 用用户钉死双列表；未钉死 note 用缓存的自然预测 baselineEcho[i]
    //     （无缓存项则喂空、退回引擎当场预测）。两类都进 PhonemeLayout 联合去重叠、都作为**显式音素**喂引擎——引擎无
    //     当场自预测、整条时间线全显式 → 接缝无补帧 Sil；宿主显示用同一份原始描述符过同一 PhonemeLayout → 音频 == 显示。
    //     且喂引擎只认「落盘钉死数据 + 可复现的自然预测缓存」，与瞬态无关 → 关闭重开结果一致。
    // 锚点 = [note 头, 有效末]，与显示同口径。老版本 TuneLab 同样是宿主算好长度再传引擎。
    public static IReadOnlyList<SnapshotNoteView> CreateChain(
        IReadOnlyList<VVoice.VoiceSynthesisNoteSnapshot> notes, IReadOnlyList<VVoice.IVoiceSynthesisNote> origins,
        IReadOnlyList<VVoice.SynthesizedSyllable?>? baselineEcho = null)
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

        static VVoice.SynthesizedPhoneme Descriptor(VVoice.VoiceSynthesisPhonemeSnapshot p) => new() { Symbol = p.Symbol, Duration = p.Duration, StretchWeight = p.StretchWeight };

        var nodes = new PhonemeLayoutNote[views.Length];
        var emit = new IReadOnlyList<VVoice.SynthesizedPhoneme>?[views.Length];   // 要输出并喂引擎的全序列（引导 ++ 主体）；null = 喂空
        for (int i = 0; i < views.Length; i++)
        {
            IReadOnlyList<VVoice.SynthesizedPhoneme> leading = [], body = [];
            double bodyOffset = 0;

            if (baselineEcho != null && (notes[i].LeadingPhonemes.Count > 0 || notes[i].BodyPhonemes.Count > 0))
            {
                // 钉死 note（仅真实 pass）：用户钉死双列表。几何字段平铺直取，老引擎不需要属性。
                leading = notes[i].LeadingPhonemes.Select(Descriptor).ToArray();
                body = notes[i].BodyPhonemes.Select(Descriptor).ToArray();
                bodyOffset = notes[i].BodyOffset;
                emit[i] = notes[i].Phonemes.Select(Descriptor).ToArray();
            }
            else if (baselineEcho != null && baselineEcho[i] is { } echo && (echo.LeadingPhonemes.Count > 0 || echo.BodyPhonemes.Count > 0))
            {
                // 未钉死 note、有基线缓存（真实 pass）：喂缓存的自然预测（引擎渲染这份而非当场重预测 → 无接缝 Sil）。
                leading = echo.LeadingPhonemes;
                body = echo.BodyPhonemes;
                bodyOffset = echo.BodyOffset;
                emit[i] = echo.Phonemes;
            }
            // else：基线 pass 全部 note，或真实 pass 中无缓存的未钉死 note → 喂空（leading/body 已空、emit[i] 留 null）。

            nodes[i] = new PhonemeLayoutNote { FillStart = views[i].StartTime, FillEnd = views[i].EndTime, LeadingPhonemes = leading, BodyPhonemes = body, BodyOffset = bodyOffset };
        }
        var timings = PhonemeLayout.Resolve(nodes);
        for (int i = 0; i < views.Length; i++)
        {
            if (emit[i] is not { } descriptors)
                continue;   // 喂空
            var list = new LVoice.SynthesizedPhoneme[descriptors.Count];
            for (int k = 0; k < descriptors.Count; k++)
                list[k] = new LVoice.SynthesizedPhoneme { Symbol = descriptors[k].Symbol, StartTime = timings[i][k].Start, EndTime = timings[i][k].End };
            views[i].Phonemes = list;
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
    }

    readonly VVoice.VoiceSynthesisNoteSnapshot mNote;
}

internal static class LegacyNoteConvert
{
    // V1 音素双列表（引导 / 主体 + BodyOffset，非空=整 note 钉死）→ 老接口的绝对秒列表。
    // 直接调 SDK 共享布局 PhonemeLayout（单 note 区间调用），与宿主显示同一套分配数学——全 w=0
    // 等比填满、乘法弹性模型、二级压缩全部一致，且随宿主布局算法演进不漂移。
    // 仅剩 LiveNoteView（Segment 分片输入）在用：老 Segment 只看时间间隙分片、不消费音素精确时序，
    // 单 note 布局足够；合成数据的钉死时序走 SnapshotNoteView.CreateChain 的整链联合布局（含跨 note 去重叠）。
    public static IReadOnlyList<LVoice.SynthesizedPhoneme> ToLegacyPinnedPhonemes(
        IReadOnlyList<VVoice.SynthesizedPhoneme> leading, IReadOnlyList<VVoice.SynthesizedPhoneme> body, double bodyOffset,
        double noteStartTime, double noteEndTime)
    {
        int n = leading.Count + body.Count;
        if (n == 0)
            return [];

        var timings = PhonemeLayout.Resolve([new PhonemeLayoutNote
        {
            FillStart = noteStartTime,
            FillEnd = noteEndTime,
            LeadingPhonemes = leading,
            BodyPhonemes = body,
            BodyOffset = bodyOffset,
        }])[0];

        var result = new List<LVoice.SynthesizedPhoneme>(n);
        for (int k = 0; k < leading.Count; k++)
            result.Add(new LVoice.SynthesizedPhoneme { Symbol = leading[k].Symbol, StartTime = timings[k].Start, EndTime = timings[k].End });
        for (int k = 0; k < body.Count; k++)
            result.Add(new LVoice.SynthesizedPhoneme { Symbol = body[k].Symbol, StartTime = timings[leading.Count + k].Start, EndTime = timings[leading.Count + k].End });
        return result;
    }
}
