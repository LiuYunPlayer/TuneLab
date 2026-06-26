using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.TestPlugins.Suite.Common;

namespace TuneLab.TestPlugins.Suite.Voice;

// 一包多插件之 voice：2 个声库（名取自共享 Common）。会话取单块最简模式——整 part 一块、
// 任何变更全量标脏（设计许可的最懒失效策略），合成产出静音 + phoneme（合成保真已由 V1.Voice 覆盖）。
public sealed class SuiteVoiceEngine : IVoiceSynthesisEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    public void Init()
    {
        mVoiceInfos.Add("suite-voice", new VoiceSourceInfo { Name = SuiteCommon.Label("Voice"), Description = "Suite shared-infra voice" });
        // 条件属性面板演示声库（note config 随当前值动态变化，见 tests/PROPERTY-CONDITIONAL-TEST-CASES.md）。
        mVoiceInfos.Add("suite-conditional", new VoiceSourceInfo { Name = SuiteCommon.Label("Conditional"), Description = "Conditional property panel demo" });
    }

    public void Destroy() { }

    // 两个声库共用同一最简会话（差异只在声明，已上移到引擎）。
    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => new SingleBlockSession(context);

    // 声明（引擎层、纯函数）：按音源 id 区分两个声库——这正是声明面读 SourceId 的用例。
    // suite-conditional：自动化 = f(已选 speaker)——每个混入的 speaker 派生一条混音曲线（多说话人混音式，演示
    // 「+ speaker → part 属性物化 → 自动化重算 → 曲线按钮自动出现」；wiring 由宿主 OnPartPropertiesModified 既有链承担）。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context)
    {
        if (SourceIdOf(context) != "suite-conditional")
            return mSuiteAutomations;

        var selected = context.Parts.Select(p => p.PartProperties).Merge().GetValue("speakers", PropertyObject.Empty);
        var map = new OrderedMap<PropertyKey, AutomationConfig>();
        foreach (var kvp in selected.Map)
            map.Add((kvp.Key, SpeakerName(kvp.Key)), new AutomationConfig { DefaultValue = 0, MinValue = 0, MaxValue = 100, Color = "#73E5A5" });
        return map;
    }
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context)
        => SourceIdOf(context) == "suite-conditional" ? ConditionalPartConfig(context) : new() { Properties = mSuitePartProperties };

    static string SourceIdOf(IVoiceSynthesisPartPropertyContext context) => context.Parts.Count > 0 ? context.Parts[0].VoiceId : string.Empty;

    // 条件声库的 part 面板：fromPart 勾选 + 变长键控容器 speakers（ExtensibleObjectConfig）。
    // present 键 = 当前已选 speaker（读 context）；+ 候选 = 全部 speaker（控件隐藏已存在的）；条目仅 presence（空 ObjectConfig）。
    static ObjectConfig ConditionalPartConfig(IVoiceSynthesisPartPropertyContext context)
    {
        var map = new OrderedMap<PropertyKey, IControllerConfig> { { "fromPart", new CheckBoxConfig() } };

        var selected = context.Parts.Select(p => p.PartProperties).Merge().GetValue("speakers", PropertyObject.Empty);
        var speakerProps = new OrderedMap<PropertyKey, IControllerConfig>();
        foreach (var kvp in selected.Map)
            speakerProps.Add((kvp.Key, SpeakerName(kvp.Key)), new ObjectConfig { Properties = new OrderedMap<PropertyKey, IControllerConfig>() });

        map.Add(("speakers", "Mixed Speakers"), new ExtensibleObjectConfig
        {
            Properties = speakerProps,
            AddableElements = kSpeakers
                .Select(s => new AddableKey((s.id, s.name), new ObjectConfig { Properties = new OrderedMap<PropertyKey, IControllerConfig>() }))
                .ToList(),
        });

        return new ObjectConfig { Properties = map };
    }

    static readonly (string id, string name)[] kSpeakers = { ("alice", "Alice"), ("bob", "Bob"), ("carol", "Carol") };
    static string SpeakerName(string id)
    {
        foreach (var s in kSpeakers)
            if (s.id == id)
                return s.name;
        return id;
    }
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context)
        => context.Part.VoiceId == "suite-conditional" ? ConditionalNoteConfig(context) : new() { Properties = mSuiteNoteProperties };
    public IReadOnlyList<ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    // 条件 note 面板：note config = f(context)，随当前值动态变化——① 显隐/换控件（mode=Advanced 多出字段）；
    // ② 控件参数随值变（pick 选项 = letters 逐字符）；③ 动态数量控件（letters 每个唯一字符派生一个滑条）。
    static ObjectConfig ConditionalNoteConfig(IVoiceSynthesisNotePropertyContext context)
    {
        // 多选：用 helper 合并成单个三态快照（不在乎逐 note 真值，按单选写法处理）。
        var note = context.Notes.Select(n => n.Properties).Merge();
        var map = new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "mode", new ComboBoxConfig { Options = ["Simple", "Advanced"] } },
            { "letters", new TextBoxConfig() },
        };

        // ② pick 选项随 letters 变（内容 + 数量）
        var letters = note.GetString("letters", "");
        var options = letters.Length > 0 ? letters.Select(c => c.ToString()).ToList() : ["(empty)"];
        map.Add("pick", new ComboBoxConfig { Options = options.Select(o => (ComboBoxOption)o).ToList() });

        // ② 沿链：part 的 fromPart 勾选 → note 多出 partGain 字段（演示 part 值 commit 触发 note 面板重算）
        if (context.Part.PartProperties.GetBool("fromPart", false))
            map.Add("partGain", new SliderConfig { DefaultValue = 0, MinValue = 0, MaxValue = 100 });

        // ① mode=Advanced → 多出字段（显隐 / 换控件）
        if (note.GetString("mode", "Simple") == "Advanced")
        {
            map.Add("gain", new SliderConfig { DefaultValue = 0, MinValue = -12, MaxValue = 12 });
            map.Add("detail", new TextBoxConfig());
        }

        // ③ 每个唯一字符 → 一个滑条（key = 字符；重复字符跳过——key 唯一模型表达不了重复，正是 ④ array 的动机）
        var seen = new HashSet<string>();
        foreach (var ch in letters)
        {
            var key = ch.ToString();
            if (seen.Add(key))
                map.Add(key, new SliderConfig { DefaultValue = 0.5, MinValue = 0, MaxValue = 1 });
        }

        // ④ 可重复列表 phonemes（PropertyArray + ListConfig）：与 ③ 的 key-unique 滑条对照——这里重复字符不跳过，
        //   "i i an" 三个音素照样三行。presence/seed 语义：absent（从未写）→ 按 letters 逐字符 seed（默认值即该字符）；
        //   present → 按实际元素数返回 TextBox（不再 seed）。面板：+ 弹菜单(Phoneme/Rest) 追加、行悬浮删除、原位编辑。
        IReadOnlyList<IControllerConfig> phonemeElements = note.Map.ContainsKey("phonemes")
            ? Enumerable.Range(0, note.GetValue("phonemes", PropertyArray.Empty).Count)
                .Select(_ => (IControllerConfig)new TextBoxConfig()).ToList()
            : letters.Select(c => (IControllerConfig)new TextBoxConfig { DefaultValue = c.ToString() }).ToList();
        map.Add("phonemes", new ListConfig
        {
            Elements = phonemeElements,
            AddableElements =
            [
                new AddableElement(new TextBoxConfig(), "Phoneme"),
                new AddableElement(new TextBoxConfig { DefaultValue = "-" }, "Rest"),
            ],
        });

        // ④ 定长数组 pair（PropertyArray + ArrayConfig）：固定 2 个滑条、不可增删；
        //   absent → 显示 2 行默认值(0.2/0.8)、编辑任一即物化整段（演示 ArrayController + seed 越界惰性绑定）。
        map.Add("pair", new ArrayConfig
        {
            Elements =
            [
                new SliderConfig { DefaultValue = 0.2, MinValue = 0, MaxValue = 1 },
                new SliderConfig { DefaultValue = 0.8, MinValue = 0, MaxValue = 1 },
            ],
        });

        // ④-D note 级变长键控容器 tags（ExtensibleObjectConfig）：part 面板的 Mixed Speakers 是单选场景，
        //   这里放一个到可多选的 note 面板，供多选下「键并集合并 + 公共键编辑扇出」真机测试。
        var selectedTags = note.GetValue("tags", PropertyObject.Empty);
        var tagProps = new OrderedMap<PropertyKey, IControllerConfig>();
        foreach (var kvp in selectedTags.Map)
            tagProps.Add((kvp.Key, TagName(kvp.Key)), new ObjectConfig { Properties = new OrderedMap<PropertyKey, IControllerConfig>() });
        map.Add(("tags", "Tags"), new ExtensibleObjectConfig
        {
            Properties = tagProps,
            AddableElements = kTags
                .Select(t => new AddableKey((t.id, t.name), new ObjectConfig { Properties = new OrderedMap<PropertyKey, IControllerConfig>() }))
                .ToList(),
        });

        return new ObjectConfig { Properties = map };
    }

    static readonly (string id, string name)[] kTags = { ("red", "Red"), ("green", "Green"), ("blue", "Blue") };
    static string TagName(string id)
    {
        foreach (var t in kTags)
            if (t.id == id)
                return t.name;
        return id;
    }

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyConfigs = new();

    // 自定义自动化名避开宿主保留名。
    readonly OrderedMap<PropertyKey, AutomationConfig> mSuiteAutomations = new() { { ("Power", "Power"), new AutomationConfig { DefaultValue = 0, MinValue = 0, MaxValue = 100, Color = "#73E5A5" } } };
    readonly OrderedMap<PropertyKey, IControllerConfig> mSuitePartProperties = new();
    // 四类控件各一项 + 多层嵌套 ObjectConfig，供属性面板三态呈现的多选测试 + 深层嵌套导航
    // （vibrato → lfo → range 共 3 层对象；见 tests/PROPERTY-TRISTATE/NAVIGATION-TEST-CASES.md）。
    readonly OrderedMap<PropertyKey, IControllerConfig> mSuiteNoteProperties = new()
    {
        { "tension", new SliderConfig { DefaultValue = 0, MinValue = -1, MaxValue = 1 } },
        { "accent", new CheckBoxConfig() },
        { "label", new TextBoxConfig() },
        { "style", new ComboBoxConfig { Options = ["Soft", "Normal", "Strong"], DefaultOption = "Normal" } },
        // 值/显示分离 + 任意基础类型：界面显示 Low/Mid/High，底层存的是 int 值 0/1/2（默认 Mid=1）。
        { "quality", new ComboBoxConfig { Options = new ComboBoxOption[] { new(0, "Low"), new(1, "Mid"), new(2, "High") }, DefaultOption = new ComboBoxOption(1, "Mid") } },
        { "vibrato", new ObjectConfig { Properties = new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "depth", new SliderConfig { DefaultValue = 0, MinValue = 0, MaxValue = 1 } },
            { "on", new CheckBoxConfig() },
            { "lfo", new ObjectConfig { Properties = new OrderedMap<PropertyKey, IControllerConfig>
            {
                { "rate", new SliderConfig { DefaultValue = 5, MinValue = 0, MaxValue = 20 } },
                { "wave", new ComboBoxConfig { Options = ["Sine", "Triangle", "Square"] } },
                { "range", new ObjectConfig { Properties = new OrderedMap<PropertyKey, IControllerConfig>
                {
                    { "min", new SliderConfig { DefaultValue = 0, MinValue = -1, MaxValue = 1 } },
                    { "max", new SliderConfig { DefaultValue = 1, MinValue = -1, MaxValue = 1 } },
                } } },
            } } },
        } } },
    };
}

// 单块最简会话：整 part 一块；订阅 context 全部变更通知 → 全量标脏（懒插件合法策略）。
// 声明（轨/面板）已上移到 SuiteVoiceEngine，会话不再承载，两个声库共用本类。
public sealed class SingleBlockSession : IVoiceSynthesisSession
{
    public SingleBlockSession(IVoiceSynthesisContext context)
    {
        mContext = context;
        mNotesSubscription = TuneLab.Foundation.NotifiableExtensions.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.Modified += MarkDirty;
        context.PartProperties.Modified += MarkDirty;
        context.Pitch.RangeModified += OnRangeModified;
        context.PitchDeviation.RangeModified += OnRangeModified;
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

        try
        {
            var notes = snapshot.Notes;
            double blockStart = notes.Count > 0 ? notes[0].StartTime : 0;
            double blockEnd = notes.Count > 0 ? notes[^1].EndTime : 0;
            int sampleCount = Math.Max(1, (int)((blockEnd - blockStart) * kSampleRate));
            mSegment?.Dispose();
            mSegment = mContext.CreateAudioSegment((long)(blockStart * kSampleRate), sampleCount, kSampleRate);
            mSegment.Commit();   // 静音输出：宿主缓冲零初始化，无需 Write
            var phonemes = new Map<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>>();
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double noteStart = note.StartTime;
                double noteEnd = note.EndTime;
                phonemes.Add(origins[i], new List<SynthesizedPhoneme>   // 索引对齐：产物归属回活 note（map 键）
                {
                    new() { Symbol = note.Lyric, Duration = noteEnd - noteStart, StretchWeight = noteEnd - noteStart },
                });
            }
            mPhonemes = phonemes;
            mBlockStart = blockStart;
            mBlockEnd = blockEnd;
        }
        finally
        {
            mSynthesizing = false;
            NotifyAll();
        }

        await Task.CompletedTask;
    }

    public SynthesizedPitch SynthesizedPitch => new() { Segments = [] };
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; } = new Map<string, SynthesizedParameter>();
    public IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>> SynthesizedPhonemes => mPhonemes;

    public IReadOnlyList<SynthesisStatusSegment> GetStatus()
    {
        if (mSegment == null && !mSynthesizing && !mDirty)
            return [];

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
        mNotesSubscription.Dispose();
        mContext.Notes.Modified -= MarkDirty;
        mContext.PartProperties.Modified -= MarkDirty;
        mContext.Pitch.RangeModified -= OnRangeModified;
        mContext.PitchDeviation.RangeModified -= OnRangeModified;
        mSegment?.Dispose();
    }

    void SubscribeNote(IVoiceSynthesisNote note)
    {
        note.StartTime.Modified += MarkDirty;
        note.EndTime.Modified += MarkDirty;
        note.Pitch.Modified += MarkDirty;
        note.Lyric.Modified += MarkDirty;
        note.Phonemes.Modified += MarkDirty;
        note.Properties.Modified += MarkDirty;
    }

    void UnsubscribeNote(IVoiceSynthesisNote note)
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
        NotifyAll();
    }

    void OnRangeModified(double startTime, double endTime) => MarkDirty();

    const int kSampleRate = 44100;

    readonly IVoiceSynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    bool mDirty;
    bool mSynthesizing;
    IAudioSegment? mSegment;
    double mBlockStart;
    double mBlockEnd;
    IReadOnlyMap<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>> mPhonemes = new Map<IVoiceSynthesisNote, IReadOnlyList<SynthesizedPhoneme>>();
}
