using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK.Voice;
using VConfig = TuneLab.SDK.Base.ControllerConfigs;
using PStruct = TuneLab.Primitives.DataStructures;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 IVoiceEngine 适配成 V1 IVoiceEngine。
// Init：V1 面无参（插件自定位），老引擎需要包目录——适配器构造时持有 enginePath、Init 转发，失败抛异常。
// VoiceInfos 每次重建（引擎在 Init 后才填充，避免缓存到空）。
//
// CreateSession：尚未真化——老 task 模型包成 V1 会话（分片账本/脏判定/音频拼接）随宿主管线
// 迁移收尾后实现；当前返回空会话（part 静音、无状态带），保证 compat 层可加载不抛错。
internal sealed class VoiceEngineAdapter(LVoice.IVoiceEngine legacy, string enginePath) : VVoice.IVoiceEngine
{
    public PStruct.IReadOnlyOrderedMap<string, VVoice.VoiceSourceInfo> VoiceInfos
    {
        get
        {
            var map = new PStruct.OrderedMap<string, VVoice.VoiceSourceInfo>();
            foreach (var kv in legacy.VoiceInfos)
                map.Add(kv.Key, new VVoice.VoiceSourceInfo() { Name = kv.Value.Name, Description = kv.Value.Description });
            return map;
        }
    }

    public void Init()
    {
        if (!legacy.Init(enginePath, out var error))
            throw new InvalidOperationException(error ?? "Legacy voice engine init failed.");
    }

    public void Destroy() => legacy.Destroy();

    public VVoice.ISynthesisSession CreateSession(string voiceId, VVoice.ISynthesisContext context)
    {
        return new PendingSession();
    }

    // 空会话占位：永远报告"窗内无待合成"，产物全空。
    sealed class PendingSession : VVoice.ISynthesisSession
    {
        public string DefaultLyric => "a";
        public PStruct.IReadOnlyOrderedMap<string, VConfig.AutomationConfig> AutomationConfigs => mAutomationConfigs;
        public PStruct.IReadOnlyOrderedMap<string, VConfig.PiecewiseAutomationConfig> PiecewiseAutomationConfigs => mPiecewiseAutomationConfigs;
        public PStruct.IReadOnlyOrderedMap<string, VConfig.IControllerConfig> PartProperties => mProperties;
        public PStruct.IReadOnlyOrderedMap<string, VConfig.IControllerConfig> NoteProperties => mProperties;

        public VVoice.ISynthesisSegment? GetNextSegment(double startTime, double endTime) => null;

        public Task SynthesizeNext(VVoice.ISynthesisSegment segment, VVoice.ISynthesisSnapshot snapshot,
            IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public int SampleRate => 44100;
        public double StartTime => 0;
        public int SampleCount => 0;
        public void ReadAudio(int offset, int count, float[] dst) { }

        public IReadOnlyList<IReadOnlyList<PStruct.Point>> SynthesizedPitch => [];
        public PStruct.IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<PStruct.Point>>> SynthesizedParameters => mSynthesizedParameters;
        public IReadOnlyList<VVoice.SynthesizedPhoneme> Phonemes => [];

        public IReadOnlyList<VVoice.SynthesisStatusSegment> GetStatus() => [];
        public event Action? StatusChanged { add { } remove { } }

        public void Dispose() { }

        static readonly PStruct.OrderedMap<string, VConfig.AutomationConfig> mAutomationConfigs = new();
        static readonly PStruct.OrderedMap<string, VConfig.PiecewiseAutomationConfig> mPiecewiseAutomationConfigs = new();
        static readonly PStruct.OrderedMap<string, VConfig.IControllerConfig> mProperties = new();
        static readonly PStruct.Map<string, IReadOnlyList<IReadOnlyList<PStruct.Point>>> mSynthesizedParameters = new();
    }
}
