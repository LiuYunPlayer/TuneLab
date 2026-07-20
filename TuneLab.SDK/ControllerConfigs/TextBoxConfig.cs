using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 StringConfig（按 UI 控件命名）。构造函数全封，只走静态工厂 + With。
// With 走 Clone（与 SliderConfig 同款）：加字段只改声明处，不逐个 With 手抄字段。
public sealed class TextBoxConfig : IValueConfig<string>
{
    public string DefaultValue { get; private set; } = "";

    // 掩码显示（如 API Key 等敏感字段）。仅影响显示，不改变存值类型，仍是普通 string property。
    public bool IsPassword { get; private set; }

    private TextBoxConfig() { }
    TextBoxConfig Clone() => (TextBoxConfig)MemberwiseClone();

    public static TextBoxConfig Create(string defaultValue = "") => new() { DefaultValue = defaultValue };

    public TextBoxConfig WithPassword(bool value = true) { var c = Clone(); c.IsPassword = value; return c; }

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
