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
    public IReadOnlyDataObjectMap<string, IPiecewiseAutomation> PiecewiseAutomations => mPiecewiseAutomations;
    IDataProperty<bool> IEffect.IsEnabled => IsEnabled;

    // 用当前参数稀疏快照求 config（纯函数）：宿主初次渲染取一次，并在参数 commit 时按当前值重算再 keyed-diff 到控件树，
    // 显隐/换控件/选项随值变都是该函数的涌现。
    public ObjectConfig PropertyConfig => Engine?.GetPropertyConfig(new EffectPropertyContext(Properties.GetInfo())) ?? EmptyPropertyConfig;

    // 自动化轨集合随当前参数值涌现（轨集合 = f(当前值)）：live 求值，每次按当前参数算——宿主在参数 commit 后读取即得新集合，
    // 无缓存/无失效时序问题。配置很小、读取不在热路径，开销可忽略。
    // 轨从集合消失不裁剪 mAutomations 的曲线数据——保留隐藏、轨复现即原样恢复。
    // 连续轨与分段轨同在此 map（kind 由 AutomationConfig.IsPiecewise 现解析）；live 求值、随当前参数涌现，孤儿数据保留隐藏。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> AutomationConfigs
        => Engine?.GetAutomationConfigs(new EffectPropertyContext(Properties.GetInfo())) ?? EmptyAutomationConfigs;

    // 回显轨声明随当前参数值涌现（live、纯函数 of 当前值），镜像 AutomationConfigs；引擎缺失时为空集合。
    // 曲线数据不在数据层，由合成管线聚合各段 processor 的回显按同一批 key 承载。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> SynthesizedParameterConfigs
        => Engine?.GetSynthesizedParameterConfigs(new EffectPropertyContext(Properties.GetInfo())) ?? EmptyAutomationConfigs;

    public Effect(MidiPart part, EffectInfo info)
    {
        mPart = part;
        Type = info.Type;
        IsEnabled = new DataStruct<bool>(this);
        Properties = new DataPropertyObject(this);
        mAutomations = new DataObjectMap<string, IAutomation>(this);
        mPiecewiseAutomations = new DataObjectMap<string, IPiecewiseAutomation>(this);
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

        if (!AutomationConfigs.TryGetValue(automationID, out var config) || config.IsPiecewise)
            return null;

        var automation = CreateAutomation(automationID, new() { DefaultValue = config.DefaultValue });
        mAutomations.Add(automationID, automation);
        return automation;
    }

    // 分段轨按需创建（无默认基线，直接建空轨）；轨须在当前声明里才创建（孤儿轨不新建、但已存在的保留）。
    public IPiecewiseAutomation? AddPiecewiseAutomation(string automationID)
    {
        if (mPiecewiseAutomations.TryGetValue(automationID, out var value))
            return value;

        if (!AutomationConfigs.TryGetValue(automationID, out var config) || !config.IsPiecewise)
            return null;

        var automation = new PiecewiseAutomation();
        mPiecewiseAutomations.Add(automationID, automation);
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
            PiecewiseAutomations = mPiecewiseAutomations.PiecewiseAutomationsToInfo(),
            Properties = Properties.GetInfo(),
        };
    }

    public void SetInfo(EffectInfo info)
    {
        using var _ = MergeNotify();
        IsEnabled.SetInfo(info.IsEnabled);
        Properties.SetInfo(info.Properties);
        mAutomations.SetInfo(info.Automations.Convert(CreateAutomation).ToMap());
        mPiecewiseAutomations.SetInfo(info.PiecewiseAutomations.ToPiecewiseAutomations());
    }

    IEffectEngine? Engine => EffectManager.GetInitedEngine(Type);

    // 条件面板求值上下文：承载该 effect 自身稀疏参数值。
    sealed class EffectPropertyContext(PropertyObject properties) : IEffectPropertyContext
    {
        public PropertyObject Properties => properties;
    }

    static readonly ObjectConfig EmptyPropertyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly IReadOnlyOrderedMap<PropertyKey, AutomationConfig> EmptyAutomationConfigs = new OrderedMap<PropertyKey, AutomationConfig>();

    readonly MidiPart mPart;
    readonly DataObjectMap<string, IAutomation> mAutomations;
    readonly DataObjectMap<string, IPiecewiseAutomation> mPiecewiseAutomations;
}
