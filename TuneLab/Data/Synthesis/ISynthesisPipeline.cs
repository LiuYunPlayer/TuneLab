using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Extensions.Effect;

namespace TuneLab.Data.Synthesis;

// 一个 part 的合成管线对宿主（Editor 调度 + MidiPart 产物转发）暴露的统一面：voice / instrument 各有实现。
// 调度（peek/dispatch + 并发槽位）与产物读取（音频段 / 回显 / 状态）领域无关，故抽象在此；
// 领域专属面（如 voice 的 IVoiceSynthesisSession Session、音素回填）留在各自具体类，不进本抽象。
// SynthesizedPitch 是 voice 专属富产物，instrument 实现返回空（保持统一面、宿主绘制端无需分支）。
internal interface ISynthesisPipeline : IDisposable
{
    // 状态 / 产物有更新（已 marshal 到数据线程），宿主 UI 收到直接刷新。
    event Action? StatusChanged;

    bool IsBusy { get; }

    IReadOnlyList<SynthesizedSegment> SynthesizedSegments { get; }
    IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch { get; }
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }
    IReadOnlyMap<string, SynthesizedParameter> GetEffectSynthesizedParameters(IEffect effect);
    IReadOnlyList<SynthesisStatusSegment> GetStatus();

    // —— 调度面（Editor 驱动逐步合成）——
    SynthesisRange? PeekNext(double startTime, double endTime);
    void Dispatch(double startTime, double endTime);

    // —— effect 失效入口（数据线程；由 MidiPart 转发）——
    void OnEffectChainStructureChanged();
    void OnSampleRateChanged();
}
