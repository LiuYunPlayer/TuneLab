using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.SDK.Format.DataInfo;
using TuneLab.Extensions.Effect;

namespace TuneLab.Data;

internal class Effect : DataObject, IEffect
{
    public IMidiPart Part => mPart;
    public string Type { get; }
    public DataStruct<bool> IsEnabled { get; }
    public DataPropertyObject Properties { get; }
    public IReadOnlyDataObjectMap<string, IAutomation> Automations => mAutomations;
    IDataProperty<bool> IEffect.IsEnabled => IsEnabled;

    public ObjectConfig PropertyConfig => Engine?.PropertyConfig ?? EmptyPropertyConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => Engine?.AutomationConfigs ?? EmptyAutomationConfigs;

    public Effect(MidiPart part, EffectInfo info)
    {
        mPart = part;
        Type = info.Type;
        IsEnabled = new DataStruct<bool>(this);
        Properties = new DataPropertyObject(this);
        mAutomations = new DataObjectMap<string, IAutomation>(this);
        SetInfo(info);
    }

    Automation CreateAutomation(string automationID, AutomationInfo info)
    {
        return new Automation(mPart, info);
    }

    public IAutomation? AddAutomation(string automationID)
    {
        if (mAutomations.TryGetValue(automationID, out var value))
            return value;

        if (!AutomationConfigs.ContainsKey(automationID))
            return null;

        var config = AutomationConfigs[automationID];
        var automation = CreateAutomation(automationID, new() { DefaultValue = config.DefaultValue });
        mAutomations.Add(automationID, automation);
        return automation;
    }

    public double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        if (mAutomations.TryGetValue(automationID, out var automation))
            return automation.GetValues(ticks);

        double defaultValue = AutomationConfigs.TryGetValue(automationID, out var config) ? config.DefaultValue : 0;
        var values = new double[ticks.Count];
        values.Fill(defaultValue);
        return values;
    }

    public EffectInfo GetInfo()
    {
        return new EffectInfo()
        {
            Type = Type,
            IsEnabled = IsEnabled.Value,
            Automations = mAutomations.GetInfo().ToInfo(),
            Properties = Properties.GetInfo(),
        };
    }

    public void SetInfo(EffectInfo info)
    {
        using var _ = MergeNotify();
        IsEnabled.SetInfo(info.IsEnabled);
        Properties.SetInfo(info.Properties);
        mAutomations.SetInfo(info.Automations.Convert(CreateAutomation).ToMap());
    }

    IEffectEngine? Engine => EffectManager.GetInitedEngine(Type);

    static readonly ObjectConfig EmptyPropertyConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };
    static readonly IReadOnlyOrderedMap<string, AutomationConfig> EmptyAutomationConfigs = new OrderedMap<string, AutomationConfig>();

    readonly MidiPart mPart;
    readonly DataObjectMap<string, IAutomation> mAutomations;
}
