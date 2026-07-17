using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 挂在 MidiPart 上的一个效果器（链中一环）。Type 不可变（换类型 = 换一个 effect，由列表重建）。
internal interface IEffect : IDataObject<EffectInfo>
{
    IMidiPart Part { get; }
    // 实例稳定标识（不透明、永不复用，本 part 链内唯一）：持久横向引用（颤音影响表）的外键锚点。
    string Id { get; }
    string Type { get; }
    IDataProperty<bool> IsEnabled { get; }
    DataPropertyObject Properties { get; }
    // 来自引擎的参数面板配置；引擎缺失/未 Init 成功时为空配置（优雅降级）。
    ObjectConfig PropertyConfig { get; }
    // 连续轨与分段轨同在此 map（kind 由 AutomationConfig.IsPiecewise 现解析）。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> AutomationConfigs { get; }
    // 合成参数回显轨声明（只读、独立于可编辑轨集合）：曲线数据经宿主聚合各段 processor 的回显按同一批 key 承载。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> SynthesizedParameterConfigs { get; }
    IReadOnlyDataObjectMap<string, IAutomation> Automations { get; }
    IReadOnlyDataObjectMap<string, IPiecewiseAutomation> PiecewiseAutomations { get; }
    IAutomation? AddAutomation(string automationID);
    IPiecewiseAutomation? AddPiecewiseAutomation(string automationID);
    double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID);
}
