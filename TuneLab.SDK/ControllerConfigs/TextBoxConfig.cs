using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 StringConfig（按 UI 控件命名）。构造函数全封，只走静态工厂 + With。
public sealed class TextBoxConfig : IValueConfig<string>
{
    public string DefaultValue { get; private init; } = "";

    // 掩码显示（如 API Key 等敏感字段）。仅影响显示，不改变存值类型，仍是普通 string property。
    public bool IsPassword { get; private init; }

    private TextBoxConfig() { }

    public static TextBoxConfig Create(string defaultValue = "") => new() { DefaultValue = defaultValue };

    public TextBoxConfig WithPassword(bool value = true)
        => new() { DefaultValue = DefaultValue, IsPassword = value };

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
