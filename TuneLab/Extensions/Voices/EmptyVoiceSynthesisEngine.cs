using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Extensions.Voices;

// 空声源引擎（type = ""）：无声源 part 的回退实现。会话永远报告"窗内无待合成"，
// 产物全空——part 不参与合成调度、UI 无状态带，行为等价于静音。
internal class EmptyVoiceSynthesisEngine : IVoiceSynthesisEngine
{
    // 名字在此惰性翻译（运行时取值，译器已就绪；切语言强制重启，故无需热更新）——避免静态字段在类加载早于语言设定时取到空翻译。
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => new OrderedMap<string, VoiceSourceInfo>() { { string.Empty, new VoiceSourceInfo { Name = "Empty Voice".Tr(TC.Property), Description = "" } } };

    public void Init() { }

    public void Destroy() { }

    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context)
    {
        return new EmptySession();
    }

    // 声明（引擎层、全空）：无声源 part 无轨/无面板。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => mEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => mEmptyConfig;
    public IReadOnlyList<ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    class EmptySession : IVoiceSynthesisSession
    {
        public string DefaultLyric => "a";

        // 零引擎的延音判定语义 = 编辑器 "-" 录入约定（本引擎是宿主授权自己代管的回退实现，判定语义
        // 与其它引擎同权、自有）：无声源 part（含缺插件的工程回退至此）上，split/merge 手势与钉死
        // melisma 显示在选到真引擎之前成对可用、观感不散架。语义：歌词 "-" ∧ 经不断裂相接链回溯到
        // 内容 note（严格比较——边界同源 tick 换算，相接即精确相等）∧ 本 note 无钉死音素（孤儿 = false）。
        public bool IsContinuation(IVoiceSynthesisNote note)
        {
            if (note.Lyric.Value != "-" || note.LeadingPhonemes.Value.Count > 0 || note.BodyPhonemes.Value.Count > 0)
                return false;
            var cur = note;
            while (true)
            {
                var prev = cur.Last;
                if (prev == null)
                    return false;                          // 链跑出开头、无内容 note → 孤儿
                if (prev.EndTime.Value < cur.StartTime.Value)
                    return false;                          // 空隙断链 → 孤儿
                if (prev.Lyric.Value != "-" || prev.LeadingPhonemes.Value.Count > 0 || prev.BodyPhonemes.Value.Count > 0)
                    return true;                           // 回溯到链头 → 生效延续
                cur = prev;
            }
        }

        public SynthesisRange? GetNextSegment(double startTime, double endTime) => null;

        public Task SynthesizeNext(double startTime, double endTime,
            CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public SynthesizedPitch SynthesizedPitch => new() { Segments = [] };
        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mSynthesizedParameters;
        public IReadOnlyMap<IVoiceSynthesisNote, SynthesizedSyllable> SynthesizedPhonemes => mSynthesizedPhonemes;

        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => [];
        public IActionEvent SynthesizedPhonemesChanged => ActionEvent.Empty;
        public IActionEvent SynthesizedParametersChanged => ActionEvent.Empty;
        public IActionEvent SynthesizedPitchChanged => ActionEvent.Empty;
        public IActionEvent StatusChanged => ActionEvent.Empty;

        public void Dispose() { }

        static readonly Map<string, SynthesizedParameter> mSynthesizedParameters = new();
        static readonly Map<IVoiceSynthesisNote, SynthesizedSyllable> mSynthesizedPhonemes = new();
    }

    static readonly OrderedMap<PropertyKey, AutomationConfig> mAutomationConfigs = new();
    static readonly ObjectConfig mEmptyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
}
