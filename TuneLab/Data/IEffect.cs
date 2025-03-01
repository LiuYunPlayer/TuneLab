using System.Collections.Generic;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface IEffect : IReadOnlyDataObject<EffectInfo>
{
    IPart Part { get; }
    string Type { get; }
    IDataProperty<bool> IsEnabled { get; }
    DataPropertyObject Properties { get; }
    IReadOnlyDataObjectMap<string, IAutomation> Automations { get; }
    IAutomation? AddAutomation(string automationID);
    double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID);
    double[] GetFinalAutomationValues(IReadOnlyList<double> ticks, string automationID);
}
