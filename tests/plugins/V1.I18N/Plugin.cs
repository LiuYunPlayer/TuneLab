using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.ControllerConfigs;
using TuneLab.SDK.Base.Environment;
using TuneLab.SDK.Voice;

namespace TuneLab.TestPlugins.V1I18N;

// i18n 演示夹具（独立于 tristate/conditional/navigation 等基线夹具，不污染其期望）。
// 验证「插件侧自译」：引擎/声源在能读到 TuneLabContext.Global.Language 时构建本地化文案——
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

    public bool Init(string enginePath, out string? error)
    {
        error = null;
        // 静态声库：名字/简介按当前语言本地化。
        mVoiceInfos.Add("i18n-static", new VoiceSourceInfo { Name = L.Tr("Demo voice"), Description = L.Tr("A localized demo voice") });
        // 动态声库：模拟运行时（如云端）按语言产出的内容——名字里带上解析到的语言码，肉眼可证按语言走。
        var lang = TuneLabContext.Global.Language;
        mVoiceInfos.Add("i18n-dynamic", new VoiceSourceInfo { Name = L.Tr("Cloud voice") + " [" + lang + "]", Description = L.Tr("A cloud-fetched voice") });
        return true;
    }

    public void Destroy() { }
    public IVoiceSource CreateVoiceSource(string id) => new I18NVoiceSource(id);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

public sealed class I18NVoiceSource : IVoiceSource
{
    public I18NVoiceSource(string id)
    {
        mId = id;
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
    }

    public string Name => mId;
    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        => this.SimpleSegment(segment);

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data) => new I18NSynthesisTask(data);

    readonly string mId;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
}

// 合成从简（i18n 与音频无关）：产出静音 + 按 note 的 phoneme。
public sealed class I18NSynthesisTask(ISynthesisData data) : ISynthesisTask
{
    public event Action<SynthesisResult>? Complete;
    public event Action<double>? Progress;
    public event Action<string>? Error;

    public void Start()
    {
        var notes = data.Notes.ToList();
        double startTime = notes.Count > 0 ? notes[0].StartTime : 0;
        double endTime = notes.Count > 0 ? notes[^1].EndTime : 0;
        int sampleCount = Math.Max(1, (int)((endTime - startTime) * SampleRate));
        var phonemes = new Dictionary<ISynthesisNote, SynthesizedPhoneme[]>();
        foreach (var note in notes)
            phonemes[note] = [new SynthesizedPhoneme { Symbol = note.Lyric, StartTime = note.StartTime, EndTime = note.EndTime }];

        Progress?.Invoke(1);
        Complete?.Invoke(new SynthesisResult(startTime, SampleRate, new float[sampleCount], null, phonemes));
    }

    public void Suspend() { }
    public void Resume() { }
    public void Stop() { }
    public void SetDirty(string dirtyType) { }

    const int SampleRate = 44100;
}
