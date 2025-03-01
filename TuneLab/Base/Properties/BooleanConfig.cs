namespace TuneLab.Base.Properties;

public class BooleanConfig(bool defaultValue = false) : IValueConfig<bool>
{
    public bool DefaultValue { get; set; } = defaultValue;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
