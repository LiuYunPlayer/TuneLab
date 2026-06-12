using TuneLab.Primitives.Property;

namespace TuneLab.SDK;

// Config 家族族根（marker）：keyed-diff 渲染按具体类型分派，接口本身不强制任何成员——
// 显示标题等字段各具体 config 自带（强制进协议会卡住未来不需要该字段的 config）。
public interface IControllerConfig
{
}

// 有默认值的 config（preset 重置、按默认值统一处理等通用逻辑按此消费）。
// 是否实现本接口同时在类型上区分"有无默认基线"（如连续型 AutomationConfig 实现、分段型不实现）。
public interface IValueConfig : IControllerConfig
{
    PropertyValue DefaultValue { get; }
}

public interface IValueConfig<T> : IValueConfig where T : notnull
{
    new T DefaultValue { get; }
}
