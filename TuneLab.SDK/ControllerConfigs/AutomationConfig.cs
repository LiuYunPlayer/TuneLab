using TuneLab.Foundation;

namespace TuneLab.SDK;

// 连续型自动化轨配置（处处有值 + 默认基线）。voice 与 effect 共用：声明一条可编辑自动化轨。
// 刻意不继承 SliderConfig：冻结面上解耦优先于 DRY——"automation 是一种 slider"是范畴错误，
// UI 复用滑条控件是宿主侧渲染选择，读各自字段即可；实现 IValueConfig<double> 恰好表达"有默认基线"。
public class AutomationConfig : IValueConfig<double>
{
    public string? DisplayText { get; init; }
    public required double DefaultValue { get; init; }
    public required double MinValue { get; init; }
    public required double MaxValue { get; init; }
    public required string Color { get; init; }
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
