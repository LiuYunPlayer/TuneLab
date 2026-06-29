using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 BooleanConfig（按 UI 控件命名）。构造函数全封，只走静态工厂（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
public sealed class CheckBoxConfig : IValueConfig<bool>
{
    public bool DefaultValue { get; private init; }

    private CheckBoxConfig() { }

    public static CheckBoxConfig Create(bool defaultValue = false) => new() { DefaultValue = defaultValue };

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
