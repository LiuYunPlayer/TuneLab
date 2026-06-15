using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 挂在 MidiPart 上的一个效果器（链中一环）。Type 不可变（换类型 = 换一个 effect，由列表重建）。
internal interface IEffect : IDataObject<EffectInfo>
{
    IMidiPart Part { get; }
    string Type { get; }
    IDataProperty<bool> IsEnabled { get; }
    DataPropertyObject Properties { get; }
    // 来自引擎的参数面板配置；引擎缺失/未 Init 成功时为空配置（优雅降级）。
    ObjectConfig PropertyConfig { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    // 标记轨集合缓存待重算：宿主在该 effect 参数 commit 后、读 AutomationConfigs 前调用，
    // 强制下次读取按当前参数值重算（不依赖内部 Properties.Modified 订阅与本次读取的相对时序）。
    void InvalidateAutomationConfigs();
    IReadOnlyDataObjectMap<string, IAutomation> Automations { get; }
    IAutomation? AddAutomation(string automationID);
    double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID);
}
