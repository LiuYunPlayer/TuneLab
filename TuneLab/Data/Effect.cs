using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
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

    // 用当前参数稀疏快照求 config（纯函数）：宿主初次渲染取一次，并在参数 commit 时按当前值重算再 keyed-diff 到控件树，
    // 显隐/换控件/选项随值变都是该函数的涌现。
    public ObjectConfig PropertyConfig => Engine?.GetPartPropertyConfig(new EffectPropertyContext(Properties.GetInfo())) ?? EmptyPropertyConfig;
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

    // 条件面板求值上下文：承载该 effect 自身稀疏参数值。
    sealed class EffectPropertyContext(PropertyObject properties) : IEffectPropertyContext
    {
        public PropertyObject Properties => properties;
    }

    static readonly ObjectConfig EmptyPropertyConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };
    static readonly IReadOnlyOrderedMap<string, AutomationConfig> EmptyAutomationConfigs = new OrderedMap<string, AutomationConfig>();

    readonly MidiPart mPart;
    readonly DataObjectMap<string, IAutomation> mAutomations;
}
