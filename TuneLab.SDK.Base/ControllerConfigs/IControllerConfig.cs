using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base.ControllerConfigs;

// Config 家族族根（原 IPropertyConfig，按 UI 控件命名体系改名）。
public interface IControllerConfig
{
    // 可本地化的显示标题。null 时调用方回退到该条目的稳定 key（数据键）。
    // 插件侧自译：此处放已按当前语言（TuneLabContext.Global.Language）本地化的串；宿主原样显示、不再查表。
    string? DisplayText { get; }
}

public interface IValueConfig : IControllerConfig
{
    PropertyValue DefaultValue { get; }
}

public interface IValueConfig<T> : IValueConfig where T : notnull
{
    new T DefaultValue { get; }
}
