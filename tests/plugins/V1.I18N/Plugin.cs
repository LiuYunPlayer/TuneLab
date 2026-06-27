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

public sealed class I18NVoiceEngine : IVoiceSynthesisEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    public void Init()
    {
        // 静态声库：名字/简介按当前语言本地化。
        mVoiceInfos.Add("i18n-static", new VoiceSourceInfo { Name = L.Tr("Demo voice"), Description = L.Tr("A localized demo voice") });
        // 动态声库：模拟运行时（如云端）按语言产出的内容——名字里带上解析到的语言码，肉眼可证按语言走。
        var lang = TuneLabContext.Global.Language;
        mVoiceInfos.Add("i18n-dynamic", new VoiceSourceInfo { Name = L.Tr("Cloud voice") + " [" + lang + "]", Description = L.Tr("A cloud-fetched voice") });

        // 声明类 config 在引擎层构建（本地化文案按当前语言）："raw" 用未收录词，验证未译时原样显示英文。
        mNoteProperties.Add(("depth", L.Tr("Depth")), new SliderConfig { DefaultValue = 0, MinValue = 0, MaxValue = 1 });
        mNoteProperties.Add(("quality", L.Tr("Quality")), new ComboBoxConfig
        {
            Options = new ComboBoxOption[] { new(0, L.Tr("Low")), new(1, L.Tr("High")) },
            DefaultOption = new ComboBoxOption(0, L.Tr("Low")),
        });
        mNoteProperties.Add(("raw", L.Tr("Uncolored")), new SliderConfig { DefaultValue = 0, MinValue = 0, MaxValue = 1 });
        // 自动化轨名本地化。
        mAutomationConfigs.Add(("breath", L.Tr("Breath")), new AutomationConfig { DefaultValue = 0, MinValue = 0, MaxValue = 100, Color = "#A573E5" });
    }

    public void Destroy() { }
    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => new I18NSession(context);

    // 声明（引擎层、纯函数）：本地化的轨/面板配置。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => new() { Properties = mPartProperties };
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => new() { Properties = mNoteProperties };
    public IReadOnlyList<ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
    readonly OrderedMap<PropertyKey, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mNoteProperties = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyConfigs = new();
}

// 会话取单块最简模式（i18n 与音频无关）：整 part 一块、任何变更全量标脏，合成产出静音 + phoneme。
public sealed class I18NSession : IVoiceSynthesisSession
{
    public I18NSession(IVoiceSynthesisContext context)
    {
        mContext = context;
        context.Notes.WhenAnyItem(n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Pitch.Modified, n => n.Lyric.Modified, n => n.Phonemes.Modified, n => n.Properties.Modified)
            .Subscribe(_ => MarkDirty(), mSubscriptions);
        context.Notes.MembershipModified.Subscribe(MarkDirty, mSubscriptions);
        context.PartProperties.Modified.Subscribe(MarkDirty, mSubscriptions);
        mDirty = true;
    }

    public string DefaultLyric => "la";

    public SynthesisRange? GetNextSegment(double startTime, double endTime)
    {
        if (!mDirty || mSynthesizing || mContext.Notes.Count == 0)
            return null;

        double blockStart = mContext.Notes.First!.StartTime.Value;
        double blockEnd = mContext.Notes.Last!.EndTime.Value;
        return blockEnd < startTime || blockStart > endTime ? null : new SynthesisRange(blockStart, blockEnd);
    }

    public async Task SynthesizeNext(double startTime, double endTime,
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
        NotifyAll();

        var notes = snapshot.Notes;
        double blockStart = notes.Count > 0 ? notes[0].StartTime : 0;
        double blockEnd = notes.Count > 0 ? notes[^1].EndTime : 0;
        int sampleCount = Math.Max(1, (int)((blockEnd - blockStart) * kSampleRate));
        mSegment?.Dispose();
        mSegment = mContext.CreateAudioSegment((long)(blockStart * kSampleRate), sampleCount, kSampleRate);
        mSegment.Commit();   // 静音输出：宿主缓冲零初始化，无需 Write
        mBlockStart = blockStart;
        mBlockEnd = blockEnd;
        var phonemes = new Map<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>>();
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            double noteStart = note.StartTime;
            double noteEnd = note.EndTime;
            phonemes.Add(origins[i], new List<SynthesizedPhoneme>
            {
                new() { Symbol = note.Lyric, Duration = noteEnd - noteStart, StretchWeight = noteEnd - noteStart },
            });
        }
        mPhonemes = phonemes;

        mSynthesizing = false;
        NotifyAll();
        await Task.CompletedTask;
    }


    public SynthesizedPitch SynthesizedPitch => new() { Segments = [] };
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; } = new Map<string, SynthesizedParameter>();
    public IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>> SynthesizedPhonemes => mPhonemes;

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

    public event Action? SynthesizedPhonemesChanged;
    public event Action? SynthesizedParametersChanged;
    public event Action? SynthesizedPitchChanged;
    public event Action? StatusChanged;

    // 测试插件：产物与状态一并通知（本实现产物只有音素，参数/音高恒空）。
    void NotifyAll()
    {
        SynthesizedPhonemesChanged?.Invoke();
        SynthesizedParametersChanged?.Invoke();
        SynthesizedPitchChanged?.Invoke();
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        mSubscriptions.DisposeAll();
        mSegment?.Dispose();
    }

    void MarkDirty()
    {
        mDirty = true;
        NotifyAll();
    }

    const int kSampleRate = 44100;

    readonly IVoiceSynthesisContext mContext;
    readonly DisposableManager mSubscriptions = new();
    bool mDirty;
    bool mSynthesizing;
    IAudioSegment? mSegment;
    double mBlockStart;
    double mBlockEnd;
    IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>> mPhonemes = new Map<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>>();
}
