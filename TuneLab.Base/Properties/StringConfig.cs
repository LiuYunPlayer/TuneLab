namespace TuneLab.Base.Properties;

public class StringConfig(string defaultValue = "") : IValueConfig<string>
{
    public string DefaultValue { get; set; } = defaultValue;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
