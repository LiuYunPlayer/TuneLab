using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Voice;
using TuneLab.TestPlugins.Suite.Common;

namespace TuneLab.TestPlugins.Suite.Voice;

// 一包多插件之 voice：1 个声库（名取自共享 Common）。合成产出静音 + phoneme（合成保真已由 V1.Voice 覆盖，此处从简）。
[VoiceEngine("TLSuiteVoice")]
public sealed class SuiteVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public bool Init(string enginePath, out string? error)
    {
        error = null;
        mVoiceInfos.Add("suite-voice", new VoiceSourceInfo { Name = SuiteCommon.Label("Voice"), Description = "Suite shared-infra voice" });
        // 条件属性面板演示声库（note config 随当前值动态变化，见 tests/PROPERTY-CONDITIONAL-TEST-CASES.md）。
        mVoiceInfos.Add("suite-conditional", new VoiceSourceInfo { Name = SuiteCommon.Label("Conditional"), Description = "Conditional property panel demo" });
        return true;
    }

    public void Destroy() { }
    public IVoiceSource CreateVoiceSource(string id)
        => id == "suite-conditional" ? new ConditionalVoiceSource(id) : new SuiteVoiceSource(id);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

public sealed class SuiteVoiceSource(string id) : IVoiceSource
{
    public string Name => id;
    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        => this.SimpleSegment(segment);

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data) => new SuiteSynthesisTask(data);

    // 与其它测试 voice 一致地声明属性（避免"空面板像 bug"的误解）。自定义自动化名避开宿主保留名。
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new() { { "Power", new AutomationConfig { DisplayText = "Power", DefaultValue = 0, MinValue = 0, MaxValue = 100, Color = "#73E5A5" } } };
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();

    // 四类控件各一项 + 多层嵌套 ObjectConfig，供属性面板「多值 / 无效」三态呈现的多选测试
    // （含嵌套对象内叶子的三态递归，见 tests/PROPERTY-TRISTATE-TEST-CASES.md）。
    // vibrato → lfo → range 共 3 层对象，验证导航模型在深层嵌套下的逐层导航 / 多选复合递归 / 懒建路径
    // （见 tests/PROPERTY-NAVIGATION-TEST-CASES.md）。
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new()
    {
        { "tension", new SliderConfig { DefaultValue = 0, MinValue = -1, MaxValue = 1 } },
        { "accent", new CheckBoxConfig() },
        { "label", new TextBoxConfig() },
        { "style", new ComboBoxConfig { Options = ["Soft", "Normal", "Strong"], DefaultOption = "Normal" } },
        // 值/显示分离 + 任意基础类型：界面显示 Low/Mid/High，底层存的是 int 值 0/1/2（默认 Mid=1）。
        { "quality", new ComboBoxConfig { Options = new ComboBoxOption[] { new(0, "Low"), new(1, "Mid"), new(2, "High") }, DefaultOption = new ComboBoxOption(1, "Mid") } },
        { "vibrato", new ObjectConfig { Properties = new OrderedMap<string, IControllerConfig>
        {
            { "depth", new SliderConfig { DefaultValue = 0, MinValue = 0, MaxValue = 1 } },
            { "on", new CheckBoxConfig() },
            { "lfo", new ObjectConfig { Properties = new OrderedMap<string, IControllerConfig>
            {
                { "rate", new SliderConfig { DefaultValue = 5, MinValue = 0, MaxValue = 20 } },
                { "wave", new ComboBoxConfig { Options = ["Sine", "Triangle", "Square"] } },
                { "range", new ObjectConfig { Properties = new OrderedMap<string, IControllerConfig>
                {
                    { "min", new SliderConfig { DefaultValue = 0, MinValue = -1, MaxValue = 1 } },
                    { "max", new SliderConfig { DefaultValue = 1, MinValue = -1, MaxValue = 1 } },
                } } },
            } } },
        } } },
    };
}

// 条件属性面板演示：note config = f(context)，随当前值动态变化。覆写 GetNoteConfig 演示三类能力——
// ① 显隐/换控件：mode=Advanced 时多出 gain/detail 字段；
// ② 控件参数随值变：pick 下拉的选项 = letters 逐字符（内容 + 数量都随 letters 变）；
// ③ 动态数量控件：letters 每个唯一字符派生一个滑条（key=字符，重复字符只出一个——有序可重复列表属 array 独立话题）。
// 静态 NoteProperties 仍声明 mode/letters 作为兜底（旧宿主 / 未覆写路径）。
public sealed class ConditionalVoiceSource(string id) : IVoiceSource
{
    public string Name => id;
    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    public ObjectConfig GetNoteConfig(IPropertyContext context)
    {
        var note = context.NoteProperties;
        var map = new OrderedMap<string, IControllerConfig>
        {
            { "mode", new ComboBoxConfig { Options = ["Simple", "Advanced"] } },
            { "letters", new TextBoxConfig() },
        };

        // ② pick 选项随 letters 变（内容 + 数量）
        var letters = note.GetString("letters", "");
        var options = letters.Length > 0 ? letters.Select(c => c.ToString()).ToList() : ["(empty)"];
        // options 是已建好的 typed List<string>，逐元素转成 ComboBoxOption（集合表达式才会自动隐式转）。
        map.Add("pick", new ComboBoxConfig { Options = options.Select(o => (ComboBoxOption)o).ToList() });

        // ② 沿链：part 的 fromPart 勾选 → note 多出 partGain 字段（演示 part 值 commit 触发 note 面板重算）
        if (context.PartProperties.GetBool("fromPart", false))
            map.Add("partGain", new SliderConfig { DefaultValue = 0, MinValue = 0, MaxValue = 100 });

        // ① mode=Advanced → 多出字段（显隐 / 换控件）
        if (note.GetString("mode", "Simple") == "Advanced")
        {
            map.Add("gain", new SliderConfig { DefaultValue = 0, MinValue = -12, MaxValue = 12 });
            map.Add("detail", new TextBoxConfig());
        }

        // ③ 每个唯一字符 → 一个滑条（key = 字符；重复字符跳过，须靠 array 才能表达可重复列表）
        var seen = new HashSet<string>();
        foreach (var ch in letters)
        {
            var key = ch.ToString();
            if (seen.Add(key))
                map.Add(key, new SliderConfig { DefaultValue = 0.5, MinValue = 0, MaxValue = 1 });
        }

        return new ObjectConfig { Properties = map };
    }

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        => this.SimpleSegment(segment);

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data) => new SuiteSynthesisTask(data);

    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    // part 级勾选项，note config 据它沿链多出字段（演示 part→note 传播）。
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new()
    {
        { "fromPart", new CheckBoxConfig() },
    };
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new()
    {
        { "mode", new ComboBoxConfig { Options = ["Simple", "Advanced"] } },
        { "letters", new TextBoxConfig() },
    };
}

public sealed class SuiteSynthesisTask(ISynthesisData data) : ISynthesisTask
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
