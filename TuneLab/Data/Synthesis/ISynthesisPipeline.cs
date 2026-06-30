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
    // 状态 / 产物有更新（已 marshal 到数据线程），宿主 UI 收到直接刷新。任意产物或状态变化都会触发——
    // 是"有变化就重绘"的合并信号；要精确响应单一产物的消费者改订下面的分离信号。
    event Action? StatusChanged;
    // —— 分离产物信号（各自仅对应产物变化时触发，不被其它产物 / 进度 tick 带动）——
    // 即便当前无消费者也对外暴露：让未来接线天然连到精确信号，而非默认接到 StatusChanged 浪费性能（多余重建 / 重绘）。
    // 合成音素已回填 note。voice-only 才有意义；instrument 无音素、永不触发。
    event Action? PhonemesChanged;
    // 合成参数回显（readback）曲线更新。voice / instrument 均可触发。
    event Action? ParametersChanged;
    // 合成音高回显更新。voice-only；instrument 无音高回显、永不触发。
    event Action? PitchChanged;

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
