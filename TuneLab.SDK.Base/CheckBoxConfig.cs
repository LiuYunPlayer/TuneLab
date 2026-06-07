using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 原 BooleanConfig（按 UI 控件命名）。
public class CheckBoxConfig : IValueConfig<bool>
{
    public bool DefaultValue { get; init; } = false;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
