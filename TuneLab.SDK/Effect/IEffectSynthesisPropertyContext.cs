using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 单个 effect 实例的只读值视图（声明面；*View 后缀 = 声明读值投影，与 voice 的 IVoiceSynthesisPartView
// 同族约定）。承载该实例「用户改过的稀疏值」（未改过的字段不出现，其默认值由引擎自己知道；
// 读不到的 key 取到 Invalid，引擎按自身默认 fallback）。
// 为何是值（PropertyObject）而非活对象：多选要做三态合并（PropertyObjectExtensions.Merge），
// 那是值操作——与 voice 声明面同一判例。壳抗迭代：以后加只读字段（如所属 part 信息）不动方法签名。
public interface IEffectSynthesisView
{
    PropertyObject Properties { get; }

    // 当前**存在曲线数据**的轨的求值器（按 key；查询轴全局秒；连续/分段皆在、含孤儿，分段轨无曲线处求值 NaN）：
    // 只读 map，可枚举可点取。引擎可据曲线当前值条件化 schema。口径与 voice 的「已声明」不同（有意）：
    // effect 的声明集 = f(本 context)，按声明枚举是自引用——未绘制的已声明轨不在 map，其值恒为引擎自知的默认。
    IReadOnlyMap<string, IAutomationEvaluator> Automations { get; }
}

// 效果器声明面（GetPropertyConfig / GetAutomationConfigs / GetSynthesizedParameterConfigs）的求值上下文：
// **多选壳**——多选 part 时对应槽位的各 effect 实例各一个视图（单选 = 1 个、无选中 = 空），
// config 是整个选区的纯函数：三态合并归引擎（context.Effects.Select(e => e.Properties).Merge()），
// 引擎据合并结果裁决多选面板的呈现（与 voice 的 IVoiceSynthesisPartPropertyContext 同构）。
// 生命周期 = 调用级：数据线程一次性同步只读求值，读完即返、不留存视图引用。
public interface IEffectSynthesisPropertyContext
{
    IReadOnlyList<IEffectSynthesisView> Effects { get; }
}
