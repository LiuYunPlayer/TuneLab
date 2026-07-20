using TuneLab.Foundation;

namespace TuneLab.SDK;

// effect 的跨线程冻结快照：Process 同步前缀经 context.GetSnapshot 拉取，worker 只读——
// 承载「worker 可能按自定时刻采样」的函数形数据（自动化冻结求值器，查询轴全局秒）与参数值快照。
// 音频刻意不在此列：Input.Read 的 copy-out 就是音频的物化机制（引擎自控拷取区间，值形而非函数形）。
// 与 voice 的 VoiceSynthesisSnapshot 同判例：worker 决定采样时刻 → 冻函数；前缀可枚举时刻的简单引擎
// 直接在前缀 Evaluate 成数组、不拉快照也完全合法。
public sealed class EffectSynthesisSnapshot
{
    // 该 effect 参数值快照（稀疏：未改过的字段不出现，默认值引擎自知）。
    public required PropertyObject Properties { get; init; }

    // 已声明自动化轨的冻结求值器（按 key；连续轨 = 曲线/默认冻结，分段轨 = 曲线冻结、无曲线处 NaN）。
    // 仅开窗区间内取值有一致性保证（区间外边缘斜率信息不完整）。
    public required IReadOnlyMap<string, SynthesisAutomationSnapshot> Automations { get; init; }
}
