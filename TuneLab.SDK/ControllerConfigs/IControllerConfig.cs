using TuneLab.Foundation;

namespace TuneLab.SDK;

// Config 家族族根（marker）：keyed-diff 渲染按具体类型分派，接口本身不强制任何成员——
// 显示标题等字段各具体 config 自带（强制进协议会卡住未来不需要该字段的 config）。
public interface IControllerConfig
{
}

// 自带默认值的「叶子」config：config 家族默认值递归（恢复默认 / 应用 preset / seed 新元素）的基例——
// 实现本接口 = 该 config 自持一个 DefaultValue、直接取用；复合 config（Object/Array/List/ExtensibleObject）
// 刻意不实现，其默认值改由递归拼装子成员得出。故本接口划的是「叶子 vs 复合」，与「有无默认基线」无关。
// 注意 DefaultValue 可能是哨兵值：AutomationConfig 恒实现本接口，但分段轨的 DefaultValue 为 NaN 表「无基线」
// （见 AutomationConfig.IsPiecewise）——做基线物化 / 重置的消费方须按具体 config 解释该值，不得把 NaN 当真默认写回。
public interface IValueConfig : IControllerConfig
{
    PropertyValue DefaultValue { get; }
}

public interface IValueConfig<T> : IValueConfig where T : notnull
{
    new T DefaultValue { get; }
}
