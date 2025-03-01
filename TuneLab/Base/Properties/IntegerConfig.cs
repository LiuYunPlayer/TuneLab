namespace TuneLab.Base.Properties;

internal class IntegerConfig : IValueConfig<int>
{
    public int DefaultValue { get; set; }
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
