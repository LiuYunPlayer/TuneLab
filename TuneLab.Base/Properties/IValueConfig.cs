namespace TuneLab.Base.Properties;

public interface IPropertyConfig
{

}

public interface IValueConfig : IPropertyConfig
{
    PropertyValue DefaultValue { get; }
}

public interface IValueConfig<T> : IValueConfig where T : notnull
{
    new T DefaultValue { get; }
}
