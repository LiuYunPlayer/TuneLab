using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Voices;

// 空声源引擎（type = ""）：无声源 part 的回退实现。会话永远报告"窗内无待合成"，
// 产物全空——part 不参与合成调度、UI 无状态带，行为等价于静音。
internal class EmptyVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => new OrderedMap<string, VoiceSourceInfo>() { { string.Empty, mVoiceSourceInfo } };

    public void Init() { }

    public void Destroy() { }

    public IVoiceSession CreateSession(string voiceId, IVoiceContext context)
    {
        return new EmptySession();
    }

    // 声明（引擎层、全空）：无声源 part 无轨/无面板。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoicePartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoicePartPropertyContext context) => mAutomationConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoicePartPropertyContext context) => mEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceNotePropertyContext context) => mEmptyConfig;

    class EmptySession : IVoiceSession
    {
        public string DefaultLyric => "a";

        public SynthesisRange? GetNextSegment(double startTime, double endTime) => null;

        public Task SynthesizeNext(double startTime, double endTime,
            CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public SynthesizedPitch SynthesizedPitch => new() { Segments = [] };
        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mSynthesizedParameters;
        public IReadOnlyMap<IVoiceNote, IReadOnlyList<VoicePhoneme>> SynthesizedPhonemes => mSynthesizedPhonemes;

        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => [];
        public event Action? SynthesizedPhonemesChanged { add { } remove { } }
        public event Action? SynthesizedParametersChanged { add { } remove { } }
        public event Action? SynthesizedPitchChanged { add { } remove { } }
        public event Action? StatusChanged { add { } remove { } }

        public void Dispose() { }

        static readonly Map<string, SynthesizedParameter> mSynthesizedParameters = new();
        static readonly Map<IVoiceNote, IReadOnlyList<VoicePhoneme>> mSynthesizedPhonemes = new();
    }

    static readonly OrderedMap<PropertyKey, AutomationConfig> mAutomationConfigs = new();
    static readonly ObjectConfig mEmptyConfig = new() { Properties = new OrderedMap<PropertyKey, IControllerConfig>() };
    static VoiceSourceInfo mVoiceSourceInfo = new() { Name = "Empty Voice", Description = "" };
}
