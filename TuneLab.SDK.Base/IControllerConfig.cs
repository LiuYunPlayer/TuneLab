using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// Config 家族族根（§三.12，原 IPropertyConfig 改名）。
public interface IControllerConfig
{

}

public interface IValueConfig : IControllerConfig
{
    PropertyValue DefaultValue { get; }
}

public interface IValueConfig<T> : IValueConfig where T : notnull
{
    new T DefaultValue { get; }
}
