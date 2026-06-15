using System.Collections.Generic;

namespace TuneLab.SDK;

// 一次重处理相对上次的变化事实集（宿主细粒度告知，引擎据此精确决定内部哪些级需重算）。
// 三类最小事实：上游音频变（区间）、参数变（哪些 key）、自动化变（哪条轨 + 秒区间）。
// 坐标系：所有时间区间一律全局秒，与 input.Audio、input.TryGetAutomation 的查询轴同一时间系。
// 线程：仅可在 IEffectProcessor.Process 的同步前缀（数据线程）读取，与 input 同纪律。
public interface IEffectChange
{
    // 处理器首次被调用：输入全新、无可复用，其余字段可忽略（引擎做全量处理）。
    bool IsInitial { get; }

    // 上游音频变更秒区间；无音频变化时返回 false。当前为整段粒度（命中即整段区间），段内子区间为加性后续。
    bool TryGetAudioChange(out double startTime, out double endTime);

    // 变更的参数 key（对应 PropertyConfig 声明的字段）；宿主按上次/本次参数快照 diff 得出。
    IReadOnlyCollection<string> ChangedProperties { get; }

    // 变更的自动化轨 id（AutomationConfigs 的 key）。
    IReadOnlyCollection<string> ChangedAutomations { get; }

    // 取某条自动化轨的变更秒区间；该轨本次未变时返回 false。
    bool TryGetAutomationChange(string automationId, out double startTime, out double endTime);
}
