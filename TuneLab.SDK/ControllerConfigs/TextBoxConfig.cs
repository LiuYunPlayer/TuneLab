using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 StringConfig（按 UI 控件命名）。
public class TextBoxConfig : IValueConfig<string>
{
    public string? DisplayText { get; init; }
    public string DefaultValue { get; init; } = "";
    // 掩码显示（如 API Key 等敏感字段）。仅影响显示，不改变存值类型，仍是普通 string property。
    public bool IsPassword { get; init; } = false;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
