using System;
using System.Collections.Generic;
using System.Text;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal class Effect : DataObject, IEffect
{
    public IPart Part => mPart;
    public string Type { get; }
    public DataStruct<bool> IsEnabled { get; } = true;
    public DataPropertyObject Properties { get; } = new();
    IDataProperty<bool> IEffect.IsEnabled => IsEnabled;
    public IReadOnlyDataObjectMap<string, IAutomation> Automations => mAutomations;

    public Effect(IPart part, string type) : this(part, new EffectInfo() { Type = type }) { }

    public Effect(IPart part, EffectInfo info)
    {
        mPart = part;
        Type = info.Type;
        IsEnabled = info.IsEnabled;
        Properties.Set(info.Properties);
        ((IDataObject<IReadOnlyMap<string, IAutomation>>)mAutomations).Set(info.Automations.Convert(CreateAutomation).ToMap());
        IsEnabled.Attach(this);
        Properties.Attach(this);
        mAutomations.Attach(this);
    }

    Automation CreateAutomation(string automationID, AutomationInfo info)
    {
        var automation = new Automation(mPart, info);
        return automation;
    }

    public IAutomation? AddAutomation(string automationID)
    {
        if (mAutomations.TryGetValue(automationID, out var value))
            return value;

        if (!IsEffectiveAutomation(automationID))
            return null;

        var config = GetEffectiveAutomationConfig(automationID);
        var automation = CreateAutomation(automationID, new() { DefaultValue = config.DefaultValue });
        mAutomations.Add(automationID, automation);
        return automation;
    }

    public double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        double[] values;
        if (mAutomations.TryGetValue(automationID, out var automation))
        {
            values = automation.GetValues(ticks);
        }
        else
        {
            var defaultValue = GetEffectiveAutomationConfig(automationID).DefaultValue;
            values = new double[ticks.Count];
            values.Fill(defaultValue);
        }

        return values;
    }

    public double[] GetFinalAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        var values = GetAutomationValues(ticks, automationID);

        if (mPart is IMidiPart midiPart)
        {
            var vibratos = midiPart.GetVibratoDeviation(ticks, automationID);
            for (int i = 0; i < ticks.Count; i++)
            {
                values[i] += vibratos[i];
            }
        }

        return values;
    }

    public bool IsEffectiveAutomation(string id)
    {
        return true; //FIXME: 通过config判断
        //return Voice.AutomationConfigs.ContainsKey(id);
    }

    public AutomationConfig GetEffectiveAutomationConfig(string id)
    {
        return new AutomationConfig(id, 0, -1, 1, "#888888"); //FIXME: 通过config返回正确结果
        /*
        if (Voice.AutomationConfigs.ContainsKey(id))
            return Voice.AutomationConfigs[id];
        */
        throw new ArgumentException(string.Format("Automation {0} is not effective!", id));
    }

    public EffectInfo GetInfo()
    {
        return new EffectInfo()
        {
            Type = Type,
            IsEnabled = IsEnabled,
            Automations = mAutomations.GetInfo().ToInfo(),
        };
    }

    readonly IPart mPart;
    readonly DataObjectMap<string, IAutomation> mAutomations = new();
}
