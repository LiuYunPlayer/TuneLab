using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Tests;

// 脚本 voice 测试共享替身（宿主实现面）：与 EffectTestDoubles 同一族语义——
// 记录反向调用触达、可被测试主动触发事件，fake / 真解释器两路对拍同一形状。

internal sealed class VoiceTestNote : IVoiceSynthesisNote
{
    public VoiceTestNote(string id, double start, double end, int pitch, string lyric)
    {
        Id = id;
        StartTime = new NotifiableProperty<double>(start);
        EndTime = new NotifiableProperty<double>(end);
        Pitch = new NotifiableProperty<int>(pitch);
        Lyric = new NotifiableProperty<string>(lyric);
        LeadingPhonemes = new NotifiableProperty<IReadOnlyList<SynthesizedPhoneme>>(Array.Empty<SynthesizedPhoneme>());
        BodyPhonemes = new NotifiableProperty<IReadOnlyList<SynthesizedPhoneme>>(Array.Empty<SynthesizedPhoneme>());
        BodyOffset = new NotifiableProperty<double>(0);
        Properties = new EffectTestProperties();
    }

    public string Id { get; }
    public IReadOnlyNotifiableProperty<double> StartTime { get; }
    public IReadOnlyNotifiableProperty<double> EndTime { get; }
    public IReadOnlyNotifiableProperty<int> Pitch { get; }
    public IReadOnlyNotifiableProperty<string> Lyric { get; }
    public IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> LeadingPhonemes { get; }
    public IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> BodyPhonemes { get; }
    public IReadOnlyNotifiableProperty<double> BodyOffset { get; }
    public IReadOnlyNotifiablePropertyObject Properties { get; }
    public IVoiceSynthesisNote? Next { get; internal set; }
    public IVoiceSynthesisNote? Previous { get; internal set; }
}

// 可通知链表替身：测试经 Add/Remove 驱动结构事件（WhenAny 组合子靠 ItemAdded/ItemRemoved 自动接线）。
internal sealed class VoiceTestNoteList : IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote>
{
    readonly List<IVoiceSynthesisNote> mItems = [];

    // 接口实现要求属性类型精确匹配（return-type covariance 不适用于属性）——公开面声明为接口形态，
    // 内部留具体实例以便测试侧 Invoke。
    readonly ActionEvent<IVoiceSynthesisNote> mItemAdded = new();
    readonly ActionEvent<IVoiceSynthesisNote> mItemRemoved = new();
    readonly ActionEvent mMembershipModified = new();
    public IActionEvent<IVoiceSynthesisNote> ItemAdded => mItemAdded;
    public IActionEvent<IVoiceSynthesisNote> ItemRemoved => mItemRemoved;
    public IActionEvent MembershipModified => mMembershipModified;

    public IEnumerable<IVoiceSynthesisNote> Items => mItems;
    public int Count => mItems.Count;
    public IEnumerator<IVoiceSynthesisNote> GetEnumerator() => mItems.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => mItems.GetEnumerator();
    public IVoiceSynthesisNote? First => mItems.FirstOrDefault();
    public IVoiceSynthesisNote? Last => mItems.LastOrDefault();

    public void Add(VoiceTestNote note)
    {
        note.Next = null;
        var last = Last as VoiceTestNote;
        note.Previous = last;
        if (last != null)
            last.Next = note;
        mItems.Add(note);
        mItemAdded.Invoke(note);
        mMembershipModified.Invoke();
    }
}

internal sealed class VoiceTestContext : IVoiceSynthesisContext
{
    public VoiceTestContext(params VoiceTestNote[] notes)
    {
        foreach (var n in notes)
            Notes.Add(n);
        Automations = new Map<string, ISynthesisAutomation> { ["vibrato_rate"] = VibratoRate };
    }

    public string VoiceId => "sine";
    public VoiceTestNoteList Notes { get; } = new();
    IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> IVoiceSynthesisContext.Notes => Notes;
    public IReadOnlyNotifiablePropertyObject PartProperties { get; } = new EffectTestProperties();
    public EffectTestAutomation PitchAuto { get; } = new();
    public EffectTestAutomation DeviationAuto { get; } = new();
    public EffectTestAutomation VibratoRate { get; } = new();
    ISynthesisAutomation IVoiceSynthesisContext.Pitch => PitchAuto;
    ISynthesisAutomation IVoiceSynthesisContext.PitchDeviation => DeviationAuto;
    public IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }
    public ActionEvent CommittedEvent { get; } = new();
    IActionEvent IVoiceSynthesisContext.Committed => CommittedEvent;
    public readonly List<EffectTestSegment> Segments = [];

    public void TriggerPitchRange(double start, double end)
        => ((ActionEvent<double, double>)PitchAuto.RangeModified).Invoke(start, end);

    VoiceSynthesisSnapshot IVoiceSynthesisContext.GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes)
    {
        var autos = new Map<string, SynthesisAutomationSnapshot>();
        autos["vibrato_rate"] = new SynthesisAutomationSnapshot { Evaluator = VibratoRate };
        return new VoiceSynthesisSnapshot
        {
            Notes = notes.Select(n => new VoiceSynthesisNoteSnapshot
            {
                Id = n.Id,
                StartTime = n.StartTime.Value,
                EndTime = n.EndTime.Value,
                Pitch = n.Pitch.Value,
                Lyric = n.Lyric.Value,
                LeadingPhonemes = Array.Empty<VoiceSynthesisPhonemeSnapshot>(),
                BodyPhonemes = Array.Empty<VoiceSynthesisPhonemeSnapshot>(),
                BodyOffset = n.BodyOffset.Value,
                Properties = PropertyObject.Empty,
            }).ToList(),
            Pitch = new SynthesisAutomationSnapshot { Evaluator = PitchAuto },
            PitchDeviation = new SynthesisAutomationSnapshot { Evaluator = DeviationAuto },
            PartProperties = PropertyObject.Empty,
            Automations = autos,
        };
    }

    IAudioSegment IVoiceSynthesisContext.CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate)
    {
        var s = new EffectTestSegment();
        Segments.Add(s);
        return s;
    }
}
