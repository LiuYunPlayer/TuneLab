using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

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
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

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

        mNotesSubscription = TuneLab.Foundation.NotifiableExtensions.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.Modified += MarkDirty;
        context.PartProperties.Modified += MarkDirty;
        mDirty = true;
    }

    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context) => sEmptyConfigs;
    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => new() { Properties = mPartProperties };
    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => new() { Properties = mNoteProperties };

    public SynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        if (!mDirty || mSynthesizing || mContext.Notes.Count == 0)
            return null;

        double blockStart = mContext.Notes.First!.StartTime.Value;
        double blockEnd = mContext.Notes.Last!.EndTime.Value;
        return blockEnd < startTime || blockStart > endTime ? null : new SynthesisSegment(blockStart, blockEnd);
    }

    public async Task SynthesizeNext(SynthesisSegment segment,
        CancellationToken cancellation = default)
    {
        if (mContext.Notes.Count == 0)
            return;

        // 同步前缀拉取快照（单块 = 整 part note 全集）。
        var origins = mContext.Notes.ToList();
        var snapshot = mContext.GetSnapshot(
            origins,
            origins[0].StartTime.Value,
            origins[^1].EndTime.Value);

        mDirty = false;
        mSynthesizing = true;
        StatusChanged?.Invoke();

        var notes = snapshot.Notes;
        double startTime = notes.Count > 0 ? notes[0].StartTime : 0;
        double endTime = notes.Count > 0 ? notes[^1].EndTime : 0;
        int sampleCount = Math.Max(1, (int)((endTime - startTime) * kSampleRate));
        mSegment?.Dispose();
        mSegment = mContext.CreateAudioSegment((long)(startTime * kSampleRate), sampleCount, kSampleRate);
        mSegment.Commit();   // 静音输出：宿主缓冲零初始化，无需 Write
        mBlockStart = startTime;
        mBlockEnd = endTime;
        var phonemes = new List<SynthesizedPhoneme>(notes.Count);
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            double noteStart = note.StartTime;
            double noteEnd = note.EndTime;
            phonemes.Add(new SynthesizedPhoneme
            {
                Symbol = note.Lyric,
                StartTime = noteStart,
                EndTime = noteEnd,
                Note = origins[i],
                StretchWeight = noteEnd - noteStart,
            });
        }
        mPhonemes = phonemes;

        mSynthesizing = false;
        StatusChanged?.Invoke();
        await Task.CompletedTask;
    }


    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; } = new Map<string, SynthesizedParameter>();
    public IReadOnlyList<SynthesizedPhoneme> Phonemes => mPhonemes;

    public IReadOnlyList<SynthesisStatusSegment> GetStatus()
    {
        if (mContext.Notes.Count == 0)
            return [];

        double start = mSynthesizing || mSegment == null ? mContext.Notes.First!.StartTime.Value : mBlockStart;
        double end = mSynthesizing || mSegment == null ? mContext.Notes.Last!.EndTime.Value : mBlockEnd;
        var status = mSynthesizing ? SynthesisSegmentStatus.Synthesizing
            : mDirty || mSegment == null ? SynthesisSegmentStatus.Pending
            : SynthesisSegmentStatus.Synthesized;
        return [new SynthesisStatusSegment { StartTime = start, EndTime = end, Status = status }];
    }

    public event Action? StatusChanged;

    public void Dispose()
    {
        mNotesSubscription.Dispose();
        mContext.Notes.Modified -= MarkDirty;
        mContext.PartProperties.Modified -= MarkDirty;
        mSegment?.Dispose();
    }

    void SubscribeNote(ILiveNote note)
    {
        note.StartTime.Modified += MarkDirty;
        note.EndTime.Modified += MarkDirty;
        note.Pitch.Modified += MarkDirty;
        note.Lyric.Modified += MarkDirty;
        note.Phonemes.Modified += MarkDirty;
        note.Properties.Modified += MarkDirty;
    }

    void UnsubscribeNote(ILiveNote note)
    {
        note.StartTime.Modified -= MarkDirty;
        note.EndTime.Modified -= MarkDirty;
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

    const int kSampleRate = 44100;

    readonly ISynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    static readonly OrderedMap<string, AutomationConfig> sEmptyConfigs = new();
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
    bool mDirty;
    bool mSynthesizing;
    IAudioSegment? mSegment;
    double mBlockStart;
    double mBlockEnd;
    IReadOnlyList<SynthesizedPhoneme> mPhonemes = [];
}
