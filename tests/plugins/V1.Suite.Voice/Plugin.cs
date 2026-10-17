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
//
// 另外它【实现了 IExtensionSettings】而同包的 format 条目没有——用来验证详情窗的设置齿轮是【按能力位归属】的：
// 切到 Suite Voice 那一页才出现齿轮，切到 Suite Format 页则没有；点它应直接定位到本引擎的设置区。
public sealed class SuiteVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings
{
    // 设置 schema（够简单即可，全控件覆盖已由 V1.Settings 承担）：一个开关 + 一个滑杆。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>
        {
            { ("suite_mute", "Silent Output"), CheckBoxConfig.Create(true) },
            { ("suite_gain", "Gain"), SliderConfig.Linear(1, 0, 2) },
        };
        return ObjectConfig.Create(props);
    }

    // 回喂可观测点：确认宿主把持久值送到了【本条目】的桶（而非同包 format 条目）。
    public void ApplySettings(PropertyObject settings)
    {
        TuneLabContext.Global.GetLogger().Info(string.Format(
            "[V1.Suite.Voice] ApplySettings: suite_mute={0}, suite_gain={1}",
            settings.GetBoolean("suite_mute", true), settings.GetDouble("suite_gain", 1)));
    }

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
        foreach (var id in InCatalogOrder(selected, kSpeakers))
            map.Add((id, SpeakerName(id)), AutomationConfig.Create(0, 100).WithColor("#73E5A5").WithDefault(0));
        // 音色 tag：每选一个派生一条强度曲线，**按 tag 的顺序**排——拖动调序后参数面板的曲线顺序随之变，
        // 用来观察 tag 组是有序的。
        foreach (var timbre in SelectedTimbres(context))
            map.Add(("timbre_" + timbre, TimbreName(timbre) + " Amount"), AutomationConfig.Create(0, 1).WithColor("#E5A573").WithDefault(0.5));
        return map;
    }
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context)
        => SourceIdOf(context) == "suite-conditional" ? ConditionalPartConfig(context) : ObjectConfig.Create(mSuitePartProperties);

    static string SourceIdOf(IVoiceSynthesisPartPropertyContext context) => context.Parts.Count > 0 ? context.Parts[0].VoiceId : string.Empty;

    // 条件声库的 part 面板：fromPart 勾选 + 变长键控容器 speakers（ExtensibleObjectConfig）。
    // present 键 = 当前已选 speaker（读 context）；+ 候选 = 全部 speaker（控件隐藏已存在的）；条目仅 presence（空 ObjectConfig）。
    static ObjectConfig ConditionalPartConfig(IVoiceSynthesisPartPropertyContext context)
    {
        var map = new OrderedMap<PropertyKey, IControllerConfig> { { "fromPart", CheckBoxConfig.Create() } };

        var selected = context.Parts.Select(p => p.PartProperties).Merge().GetValue("speakers", PropertyObject.Empty);
        var speakerProps = new OrderedMap<PropertyKey, IControllerConfig>();
        foreach (var id in InCatalogOrder(selected, kSpeakers))
            speakerProps.Add((id, SpeakerName(id)), ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>()));

        map.Add(("speakers", "Mixed Speakers"), ExtensibleObjectConfig.Create(
            speakerProps,
            // 分组候选：加完某组全部说话人后该组从 + 菜单消失。
            [
                new AddableKey("Female", kSpeakers.Where(s => s.id != "bob").Select(SpeakerKey)),
                new AddableKey("Male", kSpeakers.Where(s => s.id == "bob").Select(SpeakerKey)),
            ]));

        // tag 组：音色（分组 + 分隔线 + 顶层叶子）。有序、不重复；选中的音色各派生一条曲线（见 GetAutomationConfigs）。
        map.Add(("timbres", "Timbres"), TagConfig.Create(
            [
                new TagItem("Bright", [new TagItem("resonant", "Resonant"), new TagItem("power", "Power")]),
                new TagItem("Soft", [new TagItem("mellow", "Mellow"), new TagItem("breathy", "Breathy")]),
                TagItem.Separator(),
                new TagItem("raw", "Raw"),
            ]));

        return ObjectConfig.Create(map);
    }

    // 已选音色（多选 part 取首个——演示用，够看出顺序即可）。
    static IEnumerable<string> SelectedTimbres(IVoiceSynthesisPartPropertyContext context)
    {
        if (context.Parts.Count == 0)
            yield break;
        var array = context.Parts[0].PartProperties.GetValue("timbres", PropertyArray.Empty);
        var seen = new HashSet<string>();
        foreach (var value in array)
            if (value.ToString(out string? id) && seen.Add(id))
                yield return id;
    }

    static string TimbreName(string id) => id switch
    {
        "resonant" => "Resonant",
        "power" => "Power",
        "mellow" => "Mellow",
        "breathy" => "Breathy",
        "raw" => "Raw",
        _ => id,
    };

    static readonly (string id, string name)[] kSpeakers = { ("alice", "Alice"), ("bob", "Bob"), ("carol", "Carol") };

    // 键控容器的条目顺序由插件定：数据里的键是无序的（删掉再加会落进空位，先后不可靠），故按候选目录排；
    // 目录外的键（如旧数据里的）排在后面，不丢。
    static IEnumerable<string> InCatalogOrder(PropertyObject present, IEnumerable<(string id, string name)> catalog)
    {
        var known = new HashSet<string>();
        foreach (var (id, _) in catalog)
        {
            known.Add(id);
            if (present.Map.ContainsKey(id))
                yield return id;
        }
        foreach (var key in present.Map.Keys)
            if (!known.Contains(key))
                yield return key;
    }
    static AddableKey SpeakerKey((string id, string name) s) => new((s.id, s.name), ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>()));
    static string SpeakerName(string id)
    {
        foreach (var s in kSpeakers)
            if (s.id == id)
                return s.name;
        return id;
    }
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context)
        => context.Part.VoiceId == "suite-conditional" ? ConditionalNoteConfig(context) : ObjectConfig.Create(mSuiteNoteProperties);
    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    // 条件 note 面板：note config = f(context)，随当前值动态变化——① 显隐/换控件（mode=Advanced 多出字段）；
    // ② 控件参数随值变（pick 选项 = letters 逐字符）；③ 动态数量控件（letters 每个唯一字符派生一个滑条）。
    static ObjectConfig ConditionalNoteConfig(IVoiceSynthesisNotePropertyContext context)
    {
        // 多选：用 helper 合并成单个三态快照（不在乎逐 note 真值，按单选写法处理）。
        var note = context.Notes.Select(n => n.Properties).Merge();
        var map = new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "mode", ComboBoxConfig.Create(["Simple", "Advanced"]) },
            { "letters", TextBoxConfig.Create() },
            // 分组下拉 + 搜索：15 个叶子（≥ 搜索阈值）分在两级分组里，可按组名收窄（如「minor a」）。
            { "key", ComboBoxConfig.Create(
                [
                    new ComboBoxItem("Major", kKeyRoots.Select(r => new ComboBoxItem(r + " maj", r + " major"))),
                    new ComboBoxItem("Minor", [
                        new ComboBoxItem("Natural", kKeyRoots.Take(4).Select(r => new ComboBoxItem(r + " min", r + " minor"))),
                        ComboBoxItem.Separator(),   // 分组内分隔线
                        new ComboBoxItem("Harmonic", kKeyRoots.Take(4).Select(r => new ComboBoxItem(r + " hmin", r + " harmonic minor"))),
                    ]),
                ]).WithDefault("C maj") },
        };

        // ② pick 选项随 letters 变（内容 + 数量）
        var letters = note.GetString("letters", "");
        var options = letters.Length > 0 ? letters.Select(c => c.ToString()).ToList() : ["(empty)"];
        map.Add("pick", ComboBoxConfig.Create(options.Select(o => (ComboBoxItem)o).ToList()));

        // ② 沿链：part 的 fromPart 勾选 → note 多出 partGain 字段（演示 part 值 commit 触发 note 面板重算）
        if (context.Part.PartProperties.GetBoolean("fromPart", false))
            map.Add("partGain", SliderConfig.Linear(0, 0, 100));

        // ① mode=Advanced → 多出字段（显隐 / 换控件）
        if (note.GetString("mode", "Simple") == "Advanced")
        {
            map.Add("gain", SliderConfig.Linear(0, -12, 12));
            map.Add("detail", TextBoxConfig.Create());
        }

        // ③ 每个唯一字符 → 一个滑条（key = 字符；重复字符跳过——key 唯一模型表达不了重复，正是 ④ array 的动机）
        var seen = new HashSet<string>();
        foreach (var ch in letters)
        {
            var key = ch.ToString();
            if (seen.Add(key))
                map.Add(key, SliderConfig.Linear(0.5, 0, 1));
        }

        // ④ 可重复列表 phonemes（PropertyArray + ListConfig）：与 ③ 的 key-unique 滑条对照——这里重复字符不跳过，
        //   "i i an" 三个音素照样三行。presence/seed 语义：absent（从未写）→ 按 letters 逐字符 seed（默认值即该字符）；
        //   present → 按实际元素数返回 TextBox（不再 seed）。面板：+ 弹菜单(Phoneme/Rest) 追加、行悬浮删除、原位编辑。
        IReadOnlyList<IControllerConfig> phonemeElements = note.Map.ContainsKey("phonemes")
            ? Enumerable.Range(0, note.GetValue("phonemes", PropertyArray.Empty).Count)
                .Select(_ => (IControllerConfig)TextBoxConfig.Create()).ToList()
            : letters.Select(c => (IControllerConfig)TextBoxConfig.Create(c.ToString())).ToList();
        map.Add("phonemes", ListConfig.Create(
            phonemeElements,
            // 分组候选（两级）+ 叶子 ≥ 搜索阈值 → + 菜单带搜索框。
            [
                new AddableElement(TextBoxConfig.Create(), "Phoneme"),
                new AddableElement(TextBoxConfig.Create("-"), "Rest"),
                AddableElement.Separator("Sets"),   // 顶层带标签分隔线
                new AddableElement("Vowels", "aeiou".Select(c => new AddableElement(TextBoxConfig.Create(c.ToString()), c.ToString()))),
                new AddableElement("Consonants", [
                    new AddableElement("Plosive", "ktp".Select(c => new AddableElement(TextBoxConfig.Create(c.ToString()), c.ToString()))),
                    AddableElement.Separator(),   // 分组内分隔线
                    new AddableElement("Nasal", "nm".Select(c => new AddableElement(TextBoxConfig.Create(c.ToString()), c.ToString()))),
                ]),
            ]));

        // ④ 定长数组 pair（PropertyArray + ArrayConfig）：固定 2 个滑条、不可增删；
        //   absent → 显示 2 行默认值(0.2/0.8)、编辑任一即物化整段（演示 ArrayController + seed 越界惰性绑定）。
        map.Add("pair", ArrayConfig.Create(
            [
                SliderConfig.Linear(0.2, 0, 1),
                SliderConfig.Linear(0.8, 0, 1),
            ]));

        // ④-E note 级 tag 组 moods（TagConfig）：12 个候选（≥ 10 → + 菜单出搜索框）、默认已选 calm；
        //   含数值型的值（1/2/3 显示为 Level N），供多选下「部分有」的半透明态与按值增删的真机测试。
        map.Add(("moods", "Moods"), TagConfig.Create(
            [
                new TagItem("Gentle", [new TagItem("calm", "Calm"), new TagItem("warm", "Warm"), new TagItem("tender", "Tender"), new TagItem("dreamy", "Dreamy")]),
                new TagItem("Strong", [new TagItem("bold", "Bold"), new TagItem("angry", "Angry"), new TagItem("proud", "Proud"), new TagItem("tense", "Tense")]),
                TagItem.Separator("Intensity"),
                new TagItem(1, "Level 1"), new TagItem(2, "Level 2"), new TagItem(3, "Level 3"),
                "plain",
            ]).WithDefault(["calm"]));

        // ④-D note 级变长键控容器 tags（ExtensibleObjectConfig）：part 面板的 Mixed Speakers 是单选场景，
        //   这里放一个到可多选的 note 面板，供多选下「键并集合并 + 公共键编辑扇出」真机测试。
        var selectedTags = note.GetValue("tags", PropertyObject.Empty);
        var tagProps = new OrderedMap<PropertyKey, IControllerConfig>();
        foreach (var id in InCatalogOrder(selectedTags, kTags))
            tagProps.Add((id, TagName(id)), ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>()));
        map.Add(("tags", "Tags"), ExtensibleObjectConfig.Create(
            tagProps,
            // 分隔线分段：某段的键全加完后，该段的段标题随之隐藏（宿主清理空段）。
            [
                AddableKey.Separator("Warm"), TagKey(kTags[0]),
                AddableKey.Separator("Cool"), TagKey(kTags[1]), TagKey(kTags[2]),
            ]));

        return ObjectConfig.Create(map);
    }

    static readonly string[] kKeyRoots = { "C", "D", "E", "F", "G", "A", "B" };
    static readonly (string id, string name)[] kTags = { ("red", "Red"), ("green", "Green"), ("blue", "Blue") };
    static AddableKey TagKey((string id, string name) t) => new((t.id, t.name), ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>()));
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
    readonly OrderedMap<PropertyKey, AutomationConfig> mSuiteAutomations = new() { { ("Power", "Power"), AutomationConfig.Create(0, 100).WithColor("#73E5A5").WithDefault(0) } };
    readonly OrderedMap<PropertyKey, IControllerConfig> mSuitePartProperties = new();
    // 四类控件各一项 + 多层嵌套 ObjectConfig，供属性面板三态呈现的多选测试 + 深层嵌套导航
    // （vibrato → lfo → range 共 3 层对象；见 tests/PROPERTY-TRISTATE/NAVIGATION-TEST-CASES.md）。
    readonly OrderedMap<PropertyKey, IControllerConfig> mSuiteNoteProperties = new()
    {
        { "tension", SliderConfig.Linear(0, -1, 1) },
        { "accent", CheckBoxConfig.Create() },
        { "label", TextBoxConfig.Create() },
        // 多行 note 属性：给"多选三态"用——两个 note 值不同时框内该显示 (Multiple) 占位而非空白。
        // 最多 3 行（框贴内容长、再多则框内滚动）。
        { "memo", TextBoxConfig.Create().WithMultiline(3) },
        { "style", ComboBoxConfig.Create(["Soft", "Normal", "Strong"]).WithDefault("Normal") },
        // 值/显示分离 + 任意基础类型：界面显示 Low/Mid/High，底层存的是 int 值 0/1/2（默认 Mid=1）。
        { "quality", ComboBoxConfig.Create(new ComboBoxItem[] { new(0, "Low"), new(1, "Mid"), new(2, "High") }).WithDefault(new ComboBoxItem(1, "Mid")) },
        { "vibrato", ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "depth", SliderConfig.Linear(0, 0, 1) },
            { "on", CheckBoxConfig.Create() },
            { "lfo", ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
            {
                { "rate", SliderConfig.Linear(5, 0, 20) },
                { "wave", ComboBoxConfig.Create(["Sine", "Triangle", "Square"]) },
                { "range", ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
                {
                    { "min", SliderConfig.Linear(0, -1, 1) },
                    { "max", SliderConfig.Linear(1, -1, 1) },
                }) },
            }) },
        }) },
    };
}

// 单块最简会话：整 part 一块；订阅 context 全部变更通知 → 全量标脏（懒插件合法策略）。
// 声明（轨/面板）已上移到 SuiteVoiceEngine，会话不再承载，两个声库共用本类。
public sealed class SingleBlockSession : IVoiceSynthesisSession
{
    public SingleBlockSession(IVoiceSynthesisContext context)
    {
        mContext = context;
        context.Notes.WhenAnyItem(n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Pitch.Modified, n => n.Lyric.Modified, n => n.LeadingPhonemes.Modified, n => n.BodyPhonemes.Modified, n => n.BodyOffset.Modified, n => n.Properties.Modified)
            .Subscribe(_ => MarkDirty(), mSubscriptions);
        context.Notes.MembershipModified.Subscribe(MarkDirty, mSubscriptions);
        context.PartProperties.Modified.Subscribe(MarkDirty, mSubscriptions);
        context.Pitch.RangeModified.Subscribe(OnRangeModified);
        context.PitchDeviation.RangeModified.Subscribe(OnRangeModified);
        mDirty = true;
    }

    public string DefaultLyric => "la";
    // 本引擎无延音语义（每个 note 都是内容），如实恒 false——判定与合成行为成对，不做 melisma 就不声称。
    public bool IsContinuation(IVoiceSynthesisNote note) => false;

    public SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime)
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
        var snapshot = mContext.GetSnapshot(origins);

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
            var phonemes = new Map<string, SynthesizedSyllable>();
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double noteStart = note.StartTime;
                double noteEnd = note.EndTime;
                phonemes.Add(note.Id, new SynthesizedSyllable([], new List<SynthesizedPhoneme>   // 键 = 快照 note 运行期 id（零活引用）
                {
                    new() { Symbol = note.Lyric, Duration = noteEnd - noteStart, StretchWeight = noteEnd - noteStart },
                }, 0));   // 无引导、单核主体、元音起手 → BodyOffset 0
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
    public IReadOnlyMap<string, SynthesizedSyllable> SynthesizedPhonemes => mPhonemes;

    public IReadOnlyList<SynthesisStatusSegment> Status => BuildStatus();

    IReadOnlyList<SynthesisStatusSegment> BuildStatus()
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

    public IActionEvent SynthesizedPhonemesChanged => mSynthesizedPhonemesChanged;
    public IActionEvent SynthesizedParametersChanged => mSynthesizedParametersChanged;
    public IActionEvent SynthesizedPitchChanged => mSynthesizedPitchChanged;
    public IActionEvent StatusChanged => mStatusChanged;
    readonly ActionEvent mSynthesizedPhonemesChanged = new();
    readonly ActionEvent mSynthesizedParametersChanged = new();
    readonly ActionEvent mSynthesizedPitchChanged = new();
    readonly ActionEvent mStatusChanged = new();

    // 测试插件：产物与状态一并通知（本实现产物只有音素，参数/音高恒空）。
    void NotifyAll()
    {
        mSynthesizedPhonemesChanged.Invoke();
        mSynthesizedParametersChanged.Invoke();
        mSynthesizedPitchChanged.Invoke();
        mStatusChanged.Invoke();
    }

    public void Dispose()
    {
        mSubscriptions.DisposeAll();
        mContext.Pitch.RangeModified.Unsubscribe(OnRangeModified);
        mContext.PitchDeviation.RangeModified.Unsubscribe(OnRangeModified);
        mSegment?.Dispose();
    }

    void MarkDirty()
    {
        mDirty = true;
        NotifyAll();
    }

    void OnRangeModified(double startTime, double endTime) => MarkDirty();

    const int kSampleRate = 44100;

    readonly IVoiceSynthesisContext mContext;
    readonly DisposableManager mSubscriptions = new();
    bool mDirty;
    bool mSynthesizing;
    IAudioSegment? mSegment;
    double mBlockStart;
    double mBlockEnd;
    IReadOnlyMap<string, SynthesizedSyllable> mPhonemes = new Map<string, SynthesizedSyllable>();
}
