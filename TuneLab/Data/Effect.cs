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
    public ObjectConfig PropertyConfig => Engine?.GetPropertyConfig(new EffectPropertyContext(this)) ?? EmptyPropertyConfig;

    // 自动化轨集合随当前参数值涌现（轨集合 = f(当前值)）：live 求值，每次按当前参数算——宿主在参数 commit 后读取即得新集合，
    // 无缓存/无失效时序问题。配置很小、读取不在热路径，开销可忽略。
    // 轨从集合消失不裁剪 mAutomations 的曲线数据——保留隐藏、轨复现即原样恢复。
    // 连续轨与分段轨同在此 map（kind 由 AutomationConfig.IsPiecewise 现解析）；live 求值、随当前参数涌现，孤儿数据保留隐藏。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> AutomationConfigs
        => Engine?.GetAutomationConfigs(new EffectPropertyContext(this)) ?? EmptyAutomationConfigs;

    // 回显轨声明随当前参数值涌现（live、纯函数 of 当前值），镜像 AutomationConfigs；引擎缺失时为空集合。
    // 曲线数据不在数据层，由合成管线聚合各段 processor 的回显按同一批 key 承载。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> SynthesizedParameterConfigs
        => Engine?.GetSynthesizedParameterConfigs(new EffectPropertyContext(this)) ?? EmptyAutomationConfigs;

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

    IEffectSynthesisEngine? Engine => EffectManager.GetInitedEngine(Type);

    // 多选 part「对应槽位」实例组的合并声明面求值（UI 层构造）：slot = 各 part 同槽位的 effect 实例
    //（Type 已对齐、引擎取首实例），context 携全部实例视图、三态合并归引擎——与 voice 的
    // GetPartPropertyConfig(多 part context) 同构。单实例即 N=1 特例。
    public static ObjectConfig GetPropertyConfig(IReadOnlyList<IEffect> slot)
        => slot.Count > 0
            ? ((Effect)slot[0]).Engine?.GetPropertyConfig(new EffectPropertyContext(slot)) ?? EmptyPropertyConfig
            : EmptyPropertyConfig;

    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IReadOnlyList<IEffect> slot)
        => slot.Count > 0
            ? ((Effect)slot[0]).Engine?.GetAutomationConfigs(new EffectPropertyContext(slot)) ?? EmptyAutomationConfigs
            : EmptyAutomationConfigs;

    // 条件面板求值上下文（多选壳）：数据层自身求值恒单选——本 effect 一个视图；
    // 多选 part 的合并面板由 UI 层用各 part 对应槽位的实例构造多元素上下文（见上方静态求值口）。
    sealed class EffectPropertyContext : IEffectSynthesisPropertyContext
    {
        public EffectPropertyContext(Effect effect) => Effects = [new View(effect)];
        public EffectPropertyContext(IReadOnlyList<IEffect> effects) => Effects = effects.Select(IEffectSynthesisView (e) => new View((Effect)e)).ToList();

        public IReadOnlyList<IEffectSynthesisView> Effects { get; }

        sealed class View(Effect effect) : IEffectSynthesisView
        {
            public PropertyObject Properties => effect.Properties.GetInfo();

            // 当前**存在用户内容**的轨的求值器（连续/分段皆在、含孤儿；有内容 = 曲线数据 / 被 vibrato 投影，
            // 与 voice 声明面同口径）。未绘制且无投影的已声明轨不在 map——其值恒为引擎自知的默认，点取缺失即回退
            // 引擎默认。刻意不按「已声明」口径枚举：声明集正是本次求值要产出的东西（AutomationConfigs = f(context)），
            // 按声明枚举是自引用递归（voice 无此问题因其读的是音源缓存的上一次物化声明）。
            // 求值器无状态、每次读现建——声明面是调用级一次性求值，不留存。
            public IReadOnlyMap<string, IAutomationEvaluator> Automations
            {
                get
                {
                    var map = new Map<string, IAutomationEvaluator>();
                    foreach (var kvp in effect.mAutomations)
                        map.Add(kvp.Key, new Evaluator(effect, kvp.Key, piecewise: false));
                    foreach (var kvp in effect.mPiecewiseAutomations)
                    {
                        if (!map.ContainsKey(kvp.Key))
                            map.Add(kvp.Key, new Evaluator(effect, kvp.Key, piecewise: true));
                    }
                    int index = effect.mPart.Effects.IndexOf(effect);
                    foreach (var vibrato in effect.mPart.Vibratos)
                    {
                        foreach (var kvp in vibrato.AffectedEffectAutomations)
                        {
                            if (kvp.Key.EffectIndex == index && !map.ContainsKey(kvp.Key.Id))
                                map.Add(kvp.Key.Id, new Evaluator(effect, kvp.Key.Id, piecewise: false));
                        }
                    }
                    return map;
                }
            }

            // 查询轴全局秒 → 全局 tick → part 相对 tick → 读 effect 当前曲线：连续轨读终值（基线/默认 +
            // vibrato 投影，槽位现场解析）、分段轨读曲线（无曲线处 NaN）。
            sealed class Evaluator(Effect effect, string key, bool piecewise) : IAutomationEvaluator
            {
                public double[] Evaluate(IReadOnlyList<double> times)
                {
                    var part = effect.mPart;
                    double pos = part.Pos.Value;
                    var ticks = new double[times.Count];
                    for (int i = 0; i < times.Count; i++)
                        ticks[i] = part.TempoManager.GetTick(times[i]) - pos;

                    if (!piecewise)
                    {
                        int index = ((IMidiPart)part).Effects.IndexOf(effect);
                        if (index < 0)
                            return effect.GetAutomationValues(ticks, key);
                        return ((IMidiPart)part).GetFinalAutomationValues(ticks, AutomationKey.Effect(index, key));
                    }

                    if (effect.mPiecewiseAutomations.TryGetValue(key, out var automation))
                        return automation.GetValues(ticks);
                    var values = new double[times.Count];
                    values.Fill(double.NaN);
                    return values;
                }
            }
        }
    }

    static readonly ObjectConfig EmptyPropertyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly IReadOnlyOrderedMap<PropertyKey, AutomationConfig> EmptyAutomationConfigs = new OrderedMap<PropertyKey, AutomationConfig>();

    readonly MidiPart mPart;
    readonly DataObjectMap<string, IAutomation> mAutomations;
    readonly DataObjectMap<string, IPiecewiseAutomation> mPiecewiseAutomations;
}
