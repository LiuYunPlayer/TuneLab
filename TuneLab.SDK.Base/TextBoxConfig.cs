using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 原 StringConfig（按 UI 控件命名）。
public class TextBoxConfig(string defaultValue = "") : IValueConfig<string>
{
    public string DefaultValue { get; set; } = defaultValue;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
