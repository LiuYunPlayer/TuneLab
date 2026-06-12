using System.Collections.Generic;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Property;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK;
using TuneLab.SDK.Format.DataInfo;

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
    IReadOnlyDataObjectMap<string, IAutomation> Automations { get; }
    IAutomation? AddAutomation(string automationID);
    double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID);
}
