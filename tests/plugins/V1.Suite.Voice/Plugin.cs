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
public sealed class SuiteVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    public void Init()
    {
        mVoiceInfos.Add("suite-voice", new VoiceSourceInfo { Name = SuiteCommon.Label("Voice"), Description = "Suite shared-infra voice" });
        // 条件属性面板演示声库（note config 随当前值动态变化，见 tests/PROPERTY-CONDITIONAL-TEST-CASES.md）。
        mVoiceInfos.Add("suite-conditional", new VoiceSourceInfo { Name = SuiteCommon.Label("Conditional"), Description = "Conditional property panel demo" });
    }

    public void Destroy() { }

    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context)
        => voiceId == "suite-conditional" ? new ConditionalSession(context) : new SuiteVoiceSession(context);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

// 单块最简会话基类：整 part 一块；订阅 context 全部变更通知 → 全量标脏（懒插件合法策略）。
public abstract class SingleBlockSession : ISynthesisSession
{
    protected SingleBlockSession(ISynthesisContext context)
    {
        mContext = context;
        mNotesSubscription = TuneLab.Foundation.NotifiableExtensions.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.Modified += MarkDirty;
        context.PartProperties.Modified += MarkDirty;
        context.Pitch.RangeModified += OnRangeModified;
        context.PitchDeviation.RangeModified += OnRangeModified;
        mDirty = true;
    }

    public abstract string DefaultLyric { get; }
    protected abstract IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    protected abstract IReadOnlyOrderedMap<string, IControllerConfig> PartProperties { get; }
    protected abstract IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties { get; }

    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context) => AutomationConfigs;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context) => sEmptyConfigs;
    public virtual ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => new() { Properties = PartProperties };
    public virtual ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => new() { Properties = NoteProperties };

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

        try
        {
            var notes = snapshot.Notes;
            double startTime = notes.Count > 0 ? notes[0].StartTime : 0;
            double endTime = notes.Count > 0 ? notes[^1].EndTime : 0;
            int sampleCount = Math.Max(1, (int)((endTime - startTime) * kSampleRate));
            mSegment?.Dispose();
            mSegment = mContext.CreateAudioSegment((long)(startTime * kSampleRate), sampleCount, kSampleRate);
            mSegment.Commit();   // 静音输出：宿主缓冲零初始化，无需 Write
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
                    Note = origins[i],   // 索引对齐：产物归属回活 note
                    StretchWeight = noteEnd - noteStart,
                });
            }
            mPhonemes = phonemes;
            mBlockStart = startTime;
            mBlockEnd = endTime;
        }
        finally
        {
            mSynthesizing = false;
            StatusChanged?.Invoke();
        }

        await Task.CompletedTask;
    }

    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; } = new Map<string, SynthesizedParameter>();
    public IReadOnlyList<SynthesizedPhoneme> Phonemes => mPhonemes;

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

    public event Action? StatusChanged;

    public void Dispose()
    {
        mNotesSubscription.Dispose();
        mContext.Notes.Modified -= MarkDirty;
        mContext.PartProperties.Modified -= MarkDirty;
        mContext.Pitch.RangeModified -= OnRangeModified;
        mContext.PitchDeviation.RangeModified -= OnRangeModified;
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

    void OnRangeModified(double startTime, double endTime) => MarkDirty();

    const int kSampleRate = 44100;

    static readonly OrderedMap<string, AutomationConfig> sEmptyConfigs = new();
    readonly ISynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    bool mDirty;
    bool mSynthesizing;
    IAudioSegment? mSegment;
    double mBlockStart;
    double mBlockEnd;
    IReadOnlyList<SynthesizedPhoneme> mPhonemes = [];
}

public sealed class SuiteVoiceSession(ISynthesisContext context) : SingleBlockSession(context)
{
    public override string DefaultLyric => "la";
    protected override IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    protected override IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    protected override IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

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

// 条件属性面板演示：note config = f(context)，随当前值动态变化。覆写 GetNotePropertyConfig 演示三类能力——
// ① 显隐/换控件：mode=Advanced 时多出 gain/detail 字段；
// ② 控件参数随值变：pick 下拉的选项 = letters 逐字符（内容 + 数量都随 letters 变）；
// ③ 动态数量控件：letters 每个唯一字符派生一个滑条（key=字符，重复字符只出一个——有序可重复列表属 array 独立话题）。
// 静态 NoteProperties 仍声明 mode/letters 作为兜底（未覆写路径）。
public sealed class ConditionalSession(ISynthesisContext context) : SingleBlockSession(context)
{
    public override string DefaultLyric => "la";
    protected override IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    protected override IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    protected override IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    public override ObjectConfig GetNotePropertyConfig(INotePropertyContext context)
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
