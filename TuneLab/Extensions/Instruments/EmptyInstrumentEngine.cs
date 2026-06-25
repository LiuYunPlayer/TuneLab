using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Instruments;

// 空音源引擎（type = ""）：无音源 part 的回退实现。会话永远报告"窗内无待合成"，
// 产物全空——part 不参与合成调度、UI 无状态带，行为等价于静音。
// 与 EmptyVoiceEngine 同构，差异仅 instrument 接口面（无 DefaultLyric / 音素 / 音高回显）。
internal class EmptyInstrumentEngine : IInstrumentEngine
{
    public IReadOnlyOrderedMap<string, InstrumentSourceInfo> InstrumentSourceInfos => new OrderedMap<string, InstrumentSourceInfo>() { { string.Empty, mInstrumentSourceInfo } };

    public void Init() { }

    public void Destroy() { }

    public IInstrumentSession CreateSession(string instrumentId, IInstrumentContext context)
    {
        return new EmptySession();
    }

    // 声明（引擎层、全空）：无音源 part 无轨 / 无面板。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IInstrumentPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IInstrumentPartPropertyContext context) => mAutomationConfigs;
    public ObjectConfig GetPartPropertyConfig(IInstrumentPartPropertyContext context) => mEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IInstrumentNotePropertyContext context) => mEmptyConfig;

    class EmptySession : IInstrumentSession
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
