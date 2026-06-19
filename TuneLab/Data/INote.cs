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
    IReadOnlyList<SynthesizedPhoneme> PinnedPhonemes => Phonemes.Convert(GetPhoneme);

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
    public double EndPhonemeRatio
    {
        get
        {
            double all = PhonemeEndTime;
            if (all <= 0)
                return 1;

            double end = EndTime;
            if (Next != null)
                end = (Next.Phonemes.IsEmpty() ?
                    (Next.SynthesizedPhonemes == null || Next.SynthesizedPhonemes.IsEmpty() ? Next.StartTime : Next.SynthesizedPhonemes.ConstFirst().StartTime) :
                    Next.PhonemeStartTime + Next.StartTime).Limit(StartTime, EndTime);

            return (end - StartTime) / all;
        }
    }

    private SynthesizedPhoneme GetPhoneme(IPhoneme phoneme)
    {
        double ConvertTime(double time)
        {
            return StartTime + time * (time <= 0 ? StartPhonemeRatio : EndPhonemeRatio);
        }

        return new SynthesizedPhoneme()
        {
            StartTime = ConvertTime(phoneme.StartTime.Value),
            EndTime = ConvertTime(phoneme.EndTime.Value),
            Symbol = phoneme.Symbol.Value
        };
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
