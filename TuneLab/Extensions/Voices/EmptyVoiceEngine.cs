using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base.ControllerConfigs;

using TuneLab.SDK.Voice;
namespace TuneLab.Extensions.Voices;

// 空声源引擎（type = ""）：无声源 part 的回退实现。会话永远报告"窗内无待合成"，
// 产物全空——part 不参与合成调度、UI 无状态带，行为等价于静音。
[VoiceEngine("")]
internal class EmptyVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => new OrderedMap<string, VoiceSourceInfo>() { { string.Empty, mVoiceSourceInfo } };

    public void Init() { }

    public void Destroy() { }

    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context)
    {
        return new EmptySession();
    }

    class EmptySession : ISynthesisSession
    {
        public string DefaultLyric => "a";
        public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
        public IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> PiecewiseAutomationConfigs => mPiecewiseAutomationConfigs;
        public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
        public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

        public ISynthesisSegment? GetNextSegment(double startTime, double endTime) => null;

        public Task SynthesizeNext(ISynthesisSegment segment, ISynthesisSnapshot snapshot,
            IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public int SampleRate => 44100;
        public double StartTime => 0;
        public int SampleCount => 0;
        public void ReadAudio(int offset, int count, float[] dst) { }

        public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];
        public IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedParameters => mSynthesizedParameters;
        public IReadOnlyList<SynthesizedPhoneme> Phonemes => [];

        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => [];
        public event Action? StatusChanged { add { } remove { } }

        public void Dispose() { }

        static readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
        static readonly OrderedMap<string, PiecewiseAutomationConfig> mPiecewiseAutomationConfigs = new();
        static readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
        static readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
        static readonly Map<string, IReadOnlyList<IReadOnlyList<Point>>> mSynthesizedParameters = new();
    }

    static VoiceSourceInfo mVoiceSourceInfo = new() { Name = "Empty Voice", Description = "" };
}
