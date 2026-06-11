using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.ControllerConfigs;
using TuneLab.SDK.Base.Environment;
using TuneLab.SDK.Voice;

namespace TuneLab.TestPlugins.V1I18N;

// i18n 演示夹具（独立于 tristate/conditional/navigation 等基线夹具，不污染其期望）。
// 验证「插件侧自译」：引擎/会话在能读到 TuneLabContext.Global.Language 时构建本地化文案——
//   属性标题（DisplayText）、ComboBox 选项、自动化轨名、声库名/简介。manifest 走 description.json 的 localizations。
//   故意留一个未收录词（"Uncolored"）验证「未译时原样显示」；动态声库名内嵌语言码以肉眼可证「按语言产出」。

// 插件自带的极简翻译方案（Arch A：宿主不参与查表，插件用自己喜欢的任何方式，这里用内置词典演示）。
static class L
{
    static readonly Dictionary<string, Dictionary<string, string>> Dict = new()
    {
        ["zh-CN"] = new()
        {
            ["Depth"] = "深度",
            ["Quality"] = "音质",
            ["Low"] = "低",
            ["High"] = "高",
            ["Breath"] = "气声",
            ["Demo voice"] = "演示声库",
            ["A localized demo voice"] = "一个本地化演示声库",
            ["Cloud voice"] = "云端声库",
            ["A cloud-fetched voice"] = "一个云端拉取的声库",
            // 注意：故意不收录 "Uncolored"，用于验证未译回退原文。
        },
    };

    public static string Tr(string en)
    {
        var lang = TuneLabContext.Global.Language;
        return Dict.TryGetValue(lang, out var m) && m.TryGetValue(en, out var v) ? v : en;
    }
}

[VoiceEngine("TLI18NVoice")]
public sealed class I18NVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public void Init()
    {
        // 静态声库：名字/简介按当前语言本地化。
        mVoiceInfos.Add("i18n-static", new VoiceSourceInfo { Name = L.Tr("Demo voice"), Description = L.Tr("A localized demo voice") });
        // 动态声库：模拟运行时（如云端）按语言产出的内容——名字里带上解析到的语言码，肉眼可证按语言走。
        var lang = TuneLabContext.Global.Language;
        mVoiceInfos.Add("i18n-dynamic", new VoiceSourceInfo { Name = L.Tr("Cloud voice") + " [" + lang + "]", Description = L.Tr("A cloud-fetched voice") });
    }

    public void Destroy() { }
    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context) => new I18NSession(context);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

// 会话取单块最简模式（i18n 与音频无关）：整 part 一块、任何变更全量标脏，合成产出静音 + phoneme。
public sealed class I18NSession : ISynthesisSession
{
    public I18NSession(ISynthesisContext context)
    {
        mContext = context;
        // 属性标题本地化（DisplayText 按当前语言）。"raw" 用未收录词，验证未译时原样显示英文。
        mNoteProperties.Add("depth", new SliderConfig { DisplayText = L.Tr("Depth"), DefaultValue = 0, MinValue = 0, MaxValue = 1 });
        mNoteProperties.Add("quality", new ComboBoxConfig
        {
            DisplayText = L.Tr("Quality"),
            Options = new ComboBoxOption[] { new(0, L.Tr("Low")), new(1, L.Tr("High")) },
            DefaultOption = new ComboBoxOption(0, L.Tr("Low")),
        });
        mNoteProperties.Add("raw", new SliderConfig { DisplayText = L.Tr("Uncolored"), DefaultValue = 0, MinValue = 0, MaxValue = 1 });
        // 自动化轨名本地化。
        mAutomationConfigs.Add("breath", new AutomationConfig { DisplayText = L.Tr("Breath"), DefaultValue = 0, MinValue = 0, MaxValue = 100, Color = "#A573E5" });

        mNotesSubscription = TuneLab.Primitives.Event.NotifiableExtension.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.Modified += MarkDirty;
        context.PartProperties.Modified += MarkDirty;
        context.TimingModified += MarkDirty;
        mDirty = true;
    }

    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> PiecewiseAutomationConfigs { get; } = new OrderedMap<string, PiecewiseAutomationConfig>();
    public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    public ISynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        if (!mDirty || mSynthesizing || mContext.Notes.Count == 0)
            return null;

        var segment = new Segment(mContext.Notes.ToList());
        return segment.EndTime < startTime || segment.StartTime > endTime ? null : segment;
    }

    public async Task SynthesizeNext(ISynthesisSegment segment, ISynthesisSnapshot snapshot,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        mDirty = false;
        mSynthesizing = true;
        StatusChanged?.Invoke();

        var notes = snapshot.Notes;
        double startTime = notes.Count > 0 ? notes[0].StartPosition.Seconds : 0;
        double endTime = notes.Count > 0 ? notes[^1].EndPosition.Seconds : 0;
        mAudio = new float[Math.Max(1, (int)((endTime - startTime) * kSampleRate))];
        mAudioStart = startTime;
        mBlockStart = startTime;
        mBlockEnd = endTime;
        var phonemes = new List<SynthesizedPhoneme>(notes.Count);
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            phonemes.Add(new SynthesizedPhoneme
            {
                Symbol = note.Lyric,
                StartTime = note.StartPosition.Seconds,
                EndTime = note.EndPosition.Seconds,
                Note = segment.Notes[i],
                StretchWeight = note.EndPosition.Seconds - note.StartPosition.Seconds,
            });
        }
        mPhonemes = phonemes;
        progress?.Report(1);

        mSynthesizing = false;
        StatusChanged?.Invoke();
        await Task.CompletedTask;
    }

    public int SampleRate => kSampleRate;
    public double StartTime => mAudioStart;
    public int SampleCount => mAudio?.Length ?? 0;

    public void ReadAudio(int offset, int count, float[] dst)
    {
        if (mAudio is not { } audio)
            return;

        int from = Math.Max(offset, 0);
        int to = Math.Min(offset + count, audio.Length);
        for (int i = from; i < to; i++)
        {
            dst[i - offset] += audio[i];
        }
    }

    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];
    public IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedParameters { get; } = new Map<string, IReadOnlyList<IReadOnlyList<Point>>>();
    public IReadOnlyList<SynthesizedPhoneme> Phonemes => mPhonemes;

    public IReadOnlyList<SynthesisStatusSegment> GetStatus()
    {
        if (mContext.Notes.Count == 0)
            return [];

        double start = mSynthesizing || mAudio == null ? mContext.Notes[0].StartPosition.Value.Seconds : mBlockStart;
        double end = mSynthesizing || mAudio == null ? mContext.Notes[mContext.Notes.Count - 1].EndPosition.Value.Seconds : mBlockEnd;
        var status = mSynthesizing ? SynthesisSegmentStatus.Synthesizing
            : mDirty || mAudio == null ? SynthesisSegmentStatus.Pending
            : SynthesisSegmentStatus.Synthesized;
        return [new SynthesisStatusSegment { StartTime = start, EndTime = end, Status = status }];
    }

    public event Action? StatusChanged;

    public void Dispose()
    {
        mNotesSubscription.Dispose();
        mContext.Notes.Modified -= MarkDirty;
        mContext.PartProperties.Modified -= MarkDirty;
        mContext.TimingModified -= MarkDirty;
    }

    void SubscribeNote(ISynthesisNote note)
    {
        note.StartPosition.Modified += MarkDirty;
        note.EndPosition.Modified += MarkDirty;
        note.Pitch.Modified += MarkDirty;
        note.Lyric.Modified += MarkDirty;
        note.Phonemes.Modified += MarkDirty;
        note.Properties.Modified += MarkDirty;
    }

    void UnsubscribeNote(ISynthesisNote note)
    {
        note.StartPosition.Modified -= MarkDirty;
        note.EndPosition.Modified -= MarkDirty;
        note.Pitch.Modified -= MarkDirty;
        note.Lyric.Modified -= MarkDirty;
        note.Phonemes.Modified -= MarkDirty;
        note.Properties.Modified -= MarkDirty;
    }

    void MarkDirty()
    {
        mDirty = true;
        StatusChanged?.Invoke();
    }

    sealed class Segment(IReadOnlyList<ISynthesisNote> notes) : ISynthesisSegment
    {
        public double StartTime => notes[0].StartPosition.Value.Seconds;
        public double EndTime => notes[^1].EndPosition.Value.Seconds;
        public IReadOnlyList<ISynthesisNote> Notes => notes;
        public double StartTick => notes[0].StartPosition.Value.Tick;
        public double EndTick => notes[^1].EndPosition.Value.Tick;
    }

    const int kSampleRate = 44100;

    readonly ISynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
    bool mDirty;
    bool mSynthesizing;
    float[]? mAudio;
    double mAudioStart;
    double mBlockStart;
    double mBlockEnd;
    IReadOnlyList<SynthesizedPhoneme> mPhonemes = [];
}
