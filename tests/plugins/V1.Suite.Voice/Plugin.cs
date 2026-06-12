using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.ControllerConfigs;
using TuneLab.SDK.Voice;
using TuneLab.TestPlugins.Suite.Common;

namespace TuneLab.TestPlugins.Suite.Voice;

// 一包多插件之 voice：2 个声库（名取自共享 Common）。会话取单块最简模式——整 part 一块、
// 任何变更全量标脏（设计许可的最懒失效策略），合成产出静音 + phoneme（合成保真已由 V1.Voice 覆盖）。
[VoiceEngine("TLSuiteVoice")]
public sealed class SuiteVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

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
        mNotesSubscription = TuneLab.Primitives.Event.NotifiableExtension.WhenAny(context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.Modified += MarkDirty;
        context.PartProperties.Modified += MarkDirty;
        context.TimingModified += MarkDirty;
        context.Pitch.RangeModified += OnRangeModified;
        context.PitchDeviation.RangeModified += OnRangeModified;
        mDirty = true;
    }

    public abstract string DefaultLyric { get; }
    protected abstract IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    protected abstract IReadOnlyOrderedMap<string, IControllerConfig> PartProperties { get; }
    protected abstract IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties { get; }

    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs() => AutomationConfigs;
    public IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> GetPiecewiseAutomationConfigs() => mPiecewiseAutomationConfigs;
    public virtual ObjectConfig GetPartConfig(IPropertyContext context) => new() { Properties = PartProperties };
    public virtual ObjectConfig GetNoteConfig(IPropertyContext context) => new() { Properties = NoteProperties };
    static readonly OrderedMap<string, PiecewiseAutomationConfig> mPiecewiseAutomationConfigs = new();

    public SynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        if (!mDirty || mSynthesizing || mContext.Notes.Count == 0)
            return null;

        double blockStart = mContext.Notes.First!.StartPosition.Value.Seconds;
        double blockEnd = mContext.Notes.Last!.EndPosition.Value.Seconds;
        return blockEnd < startTime || blockStart > endTime ? null : new SynthesisSegment(blockStart, blockEnd);
    }

    public async Task SynthesizeNext(SynthesisSegment segment,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (mContext.Notes.Count == 0)
            return;

        // 同步前缀拉取快照（单块 = 整 part note 全集）。
        var origins = mContext.Notes.ToList();
        var snapshot = mContext.GetSnapshot(
            origins,
            origins[0].StartPosition.Value.Tick,
            origins[^1].EndPosition.Value.Tick);

        mDirty = false;
        mSynthesizing = true;
        StatusChanged?.Invoke();

        try
        {
            var notes = snapshot.Notes;
            double startTime = notes.Count > 0 ? notes[0].StartPosition.Seconds : 0;
            double endTime = notes.Count > 0 ? notes[^1].EndPosition.Seconds : 0;
            mAudio = new float[Math.Max(1, (int)((endTime - startTime) * kSampleRate))];
            mAudioStart = startTime;
            var phonemes = new List<SynthesizedPhoneme>(notes.Count);
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                phonemes.Add(new SynthesizedPhoneme
                {
                    Symbol = note.Lyric,
                    StartTime = note.StartPosition.Seconds,
                    EndTime = note.EndPosition.Seconds,
                    Note = origins[i],   // 索引对齐：产物归属回活 note
                    StretchWeight = note.EndPosition.Seconds - note.StartPosition.Seconds,
                });
            }
            mPhonemes = phonemes;
            mBlockStart = startTime;
            mBlockEnd = endTime;
            progress?.Report(1);
        }
        finally
        {
            mSynthesizing = false;
            StatusChanged?.Invoke();
        }

        await Task.CompletedTask;
    }

    // 音频协议：全局 0 时刻 = 采样点 0。
    public int SampleRate => kSampleRate;

    public void ReadAudio(long offset, int count, float[] dst)
    {
        if (mAudio is not { } audio)
            return;

        long audioOffset = (long)(mAudioStart * kSampleRate);
        long from = Math.Max(offset, audioOffset);
        long to = Math.Min(offset + count, audioOffset + audio.Length);
        for (long i = from; i < to; i++)
        {
            dst[i - offset] += audio[i - audioOffset];
        }
    }

    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];
    public IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedParameters { get; } = new Map<string, IReadOnlyList<IReadOnlyList<Point>>>();
    public IReadOnlyList<SynthesizedPhoneme> Phonemes => mPhonemes;

    public IReadOnlyList<SynthesisStatusSegment> GetStatus()
    {
        if (mAudio == null && !mSynthesizing && !mDirty)
            return [];

        if (mContext.Notes.Count == 0)
            return [];

        double start = mSynthesizing || mAudio == null ? mContext.Notes.First!.StartPosition.Value.Seconds : mBlockStart;
        double end = mSynthesizing || mAudio == null ? mContext.Notes.Last!.EndPosition.Value.Seconds : mBlockEnd;
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
        mContext.Pitch.RangeModified -= OnRangeModified;
        mContext.PitchDeviation.RangeModified -= OnRangeModified;
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

    void OnRangeModified(double startTick, double endTick) => MarkDirty();

    const int kSampleRate = 44100;

    readonly ISynthesisContext mContext;
    readonly IDisposable mNotesSubscription;
    bool mDirty;
    bool mSynthesizing;
    float[]? mAudio;
    double mAudioStart;
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

// 条件属性面板演示：note config = f(context)，随当前值动态变化。覆写 GetNoteConfig 演示三类能力——
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

    public override ObjectConfig GetNoteConfig(IPropertyContext context)
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
