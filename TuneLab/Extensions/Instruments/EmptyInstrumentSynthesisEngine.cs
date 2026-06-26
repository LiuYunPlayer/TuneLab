using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Instruments;

// 空音源引擎（type = ""）：无音源 part 的回退实现。会话永远报告"窗内无待合成"，
// 产物全空——part 不参与合成调度、UI 无状态带，行为等价于静音。
// 与 EmptyVoiceSynthesisEngine 同构，差异仅 instrument 接口面（无 DefaultLyric / 音素 / 音高回显）。
internal class EmptyInstrumentSynthesisEngine : IInstrumentSynthesisEngine
{
    public IReadOnlyOrderedMap<string, InstrumentSourceInfo> InstrumentSourceInfos => new OrderedMap<string, InstrumentSourceInfo>() { { string.Empty, mInstrumentSourceInfo } };

    public void Init() { }

    public void Destroy() { }

    public IInstrumentSynthesisSession CreateSession(IInstrumentSynthesisContext context)
    {
        return new EmptySession();
    }

    // 声明（引擎层、全空）：无音源 part 无轨 / 无面板。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IInstrumentSynthesisPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IInstrumentSynthesisPartPropertyContext context) => mAutomationConfigs;
    public ObjectConfig GetPartPropertyConfig(IInstrumentSynthesisPartPropertyContext context) => mEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IInstrumentSynthesisNotePropertyContext context) => mEmptyConfig;

    class EmptySession : IInstrumentSynthesisSession
    {
        public SynthesisRange? GetNextSegment(double startTime, double endTime) => null;

        public Task SynthesizeNext(double startTime, double endTime,
            CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mSynthesizedParameters;

        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => [];
        public event Action? SynthesizedParametersChanged { add { } remove { } }
        public event Action? StatusChanged { add { } remove { } }

        public void Dispose() { }

        static readonly Map<string, SynthesizedParameter> mSynthesizedParameters = new();
    }

    static readonly OrderedMap<PropertyKey, AutomationConfig> mAutomationConfigs = new();
    static readonly ObjectConfig mEmptyConfig = new() { Properties = new OrderedMap<PropertyKey, IControllerConfig>() };
    static InstrumentSourceInfo mInstrumentSourceInfo = new() { Name = "Empty Instrument", Description = "" };
}
