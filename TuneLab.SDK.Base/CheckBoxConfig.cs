using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 原 BooleanConfig（按 UI 控件命名）。
public class CheckBoxConfig(bool defaultValue = false) : IValueConfig<bool>
{
    public bool DefaultValue { get; set; } = defaultValue;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
