using System;
using System.Collections.Generic;
using LProp = TuneLab.Base.Properties;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK.Voice;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把 V1 会话侧的 note（活代理/冻结快照）包装成老 ISynthesisNote 喂老引擎。
// 两形态对应老接口的两个消费场景：
//   LiveNoteView    —— Segment() 分片输入（数据线程，读活代理当前值）；
//   SnapshotNoteView —— CreateSynthesisTask 的数据输入（worker 线程，只读冻结快照）。

// 活视图包装：身份按 V1 note 代理缓存（同一代理恒得同一包装，分片 EqualsWith 依赖引用相等）。
internal sealed class LiveNoteViewCache(Func<VVoice.ISynthesisNote, LProp.PropertyObject> propertiesReader)
{
    public LiveNoteView Wrap(VVoice.ISynthesisNote origin)
    {
        if (!mViews.TryGetValue(origin, out var view))
        {
            view = new LiveNoteView(origin, this, propertiesReader);
            mViews.Add(origin, view);
        }
        return view;
    }

    public void Prune(IReadOnlyCollection<VVoice.ISynthesisNote> alive)
    {
        var dead = new List<VVoice.ISynthesisNote>();
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

    readonly Dictionary<VVoice.ISynthesisNote, LiveNoteView> mViews = new(ReferenceEqualityComparer.Instance);
}

internal sealed class LiveNoteView(
    VVoice.ISynthesisNote origin,
    LiveNoteViewCache cache,
    Func<VVoice.ISynthesisNote, LProp.PropertyObject> propertiesReader) : LVoice.ISynthesisNote
{
    public VVoice.ISynthesisNote Origin => origin;

    public LVoice.ISynthesisNote? Next => origin.Next is { } next ? cache.Wrap(next) : null;
    public LVoice.ISynthesisNote? Last => origin.Last is { } last ? cache.Wrap(last) : null;
    public double StartTime => origin.StartPosition.Value.Seconds;
    public double EndTime => origin.EndPosition.Value.Seconds;
    public int Pitch => origin.Pitch.Value;
    public string Lyric => origin.Lyric.Value;
    // 按老声源的 NoteProperties 声明键现取（V1 订阅树外观不可枚举，键集来自声明）。
    public LProp.PropertyObject Properties => propertiesReader(origin);
    public IReadOnlyList<LVoice.SynthesizedPhoneme> Phonemes => LegacyNoteConvert.ToLegacyPinnedPhonemes(origin.Phonemes.Value, StartTime);
}

// 快照包装：按段一次性建链（与 segment.Notes 索引对齐，Origin 留作产物归属的身份 token）。
internal sealed class SnapshotNoteView : LVoice.ISynthesisNote
{
    public VVoice.ISynthesisNote Origin { get; }

    public LVoice.ISynthesisNote? Next { get; private set; }
    public LVoice.ISynthesisNote? Last { get; private set; }
    public double StartTime => mNote.StartPosition.Seconds;
    public double EndTime => mNote.EndPosition.Seconds;
    public int Pitch => mNote.Pitch;
    public string Lyric => mNote.Lyric;
    public LProp.PropertyObject Properties { get; }
    public IReadOnlyList<LVoice.SynthesizedPhoneme> Phonemes { get; }

    public static IReadOnlyList<SnapshotNoteView> CreateChain(
        IReadOnlyList<VVoice.SynthesisNoteSnapshot> notes, IReadOnlyList<VVoice.ISynthesisNote> origins)
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

    SnapshotNoteView(VVoice.SynthesisNoteSnapshot note, VVoice.ISynthesisNote origin)
    {
        mNote = note;
        Origin = origin;
        Properties = Conversion.PropertyConvert.ToLegacy(note.Properties);
        Phonemes = LegacyNoteConvert.ToLegacyPinnedPhonemes(note.Phonemes, note.StartPosition.Seconds);
    }

    readonly VVoice.SynthesisNoteSnapshot mNote;
}

internal static class LegacyNoteConvert
{
    // V1 钉死音素（note 相对秒，列表非空=整 note 钉死）→ 老接口的绝对秒列表（语义一致直转）。
    public static IReadOnlyList<LVoice.SynthesizedPhoneme> ToLegacyPinnedPhonemes(
        IReadOnlyList<VVoice.PhonemeInfo> phonemes, double noteStartTime)
    {
        if (phonemes.Count == 0)
            return [];

        var result = new List<LVoice.SynthesizedPhoneme>(phonemes.Count);
        foreach (var phoneme in phonemes)
        {
            result.Add(new LVoice.SynthesizedPhoneme
            {
                Symbol = phoneme.Symbol,
                StartTime = noteStartTime + phoneme.StartTime,
                EndTime = noteStartTime + phoneme.EndTime,
            });
        }
        return result;
    }
}
