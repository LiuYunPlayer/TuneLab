using TuneLab.Foundation;

namespace TuneLab.SDK;

// 自动化轨配置（voice 与 effect 共用）。一个类同时表达两种形态，由 DefaultValue 区分：
//   · 连续型：DefaultValue 为实数——处处有值、以此为默认基线（如音量）。
//   · 分段型：DefaultValue 为 NaN——无默认基线、段间取值 NaN（如 pitch 类），渲染/编辑按分段处理。
// NaN 表"无基线"与本 SDK 既有"NaN 表空"求值约定同源；作者在同一张有序 map 里自由穿插两种轨、声明序即呈现序。
// 刻意不继承 SliderConfig：冻结面上解耦优先于 DRY——UI 复用滑条控件是宿主侧渲染选择，读各自字段即可。
public class AutomationConfig : IValueConfig<double>
{
    // 连续轨的默认基线；分段轨置 double.NaN 表"无基线"。
    public required double DefaultValue { get; init; }
    public required double MinValue { get; init; }
    public required double MaxValue { get; init; }
    public required string Color { get; init; }

    // 声明默认基线可随机：UI 默认值面板（滑条）右侧给出随机入口，在量程内重新取值。
    // 与 SliderConfig.Randomizable 同义；整数精度上限同为 double 的 2^53。
    public bool Randomizable { get; init; } = false;

    // 分段（间断、无默认基线）轨 = DefaultValue 为 NaN；连续轨为实数。
    public bool IsPiecewise => double.IsNaN(DefaultValue);

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
