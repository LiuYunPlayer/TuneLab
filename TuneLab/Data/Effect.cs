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
    public ObjectConfig PropertyConfig => Engine?.GetPropertyConfig(new EffectPropertyContext(Properties.GetInfo())) ?? EmptyPropertyConfig;

    // 自动化轨集合随当前参数值涌现（轨集合 = f(当前值)）。惰性 dirty 缓存：参数 commit 仅置脏（不强制引擎 Init，
    // 避免装载期对全部 effect 的重型引擎触发 Init），下次读取时按当前值重算。缓存还避免合成期 TryGetAutomation
    // 反复 GetInfo + 引擎重算的开销。轨从集合消失不裁剪 mAutomations 的曲线数据——保留隐藏、轨复现即原样恢复。
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs
    {
        get
        {
            if (mAutomationConfigsDirty)
                RecomputeAutomationConfigs();
            return mAutomationConfigs;
        }
    }

    public Effect(MidiPart part, EffectInfo info)
    {
        mPart = part;
        Type = info.Type;
        IsEnabled = new DataStruct<bool>(this);
        Properties = new DataPropertyObject(this);
        mAutomations = new DataObjectMap<string, IAutomation>(this);
        SetInfo(info);
        // 参数 commit 置脏（子先于父触发，故 part 侧 OnEffectModified 读 AutomationConfigs 时已是新值）。
        Properties.Modified.Subscribe(() => mAutomationConfigsDirty = true);
    }

    // 按当前参数稀疏快照重算自动化轨集合，写入缓存、清脏。引擎缺失/未 Init 成功时退化为空集（优雅降级）。
    void RecomputeAutomationConfigs()
    {
        mAutomationConfigs.Clear();
        var configs = Engine?.GetAutomationConfigs(new EffectPropertyContext(Properties.GetInfo()));
        if (configs != null)
        {
            foreach (var kvp in configs)
                mAutomationConfigs.Add(kvp.Key, kvp.Value);
        }
        mAutomationConfigsDirty = false;
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

    readonly MidiPart mPart;
    readonly DataObjectMap<string, IAutomation> mAutomations;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    bool mAutomationConfigsDirty = true;
}
