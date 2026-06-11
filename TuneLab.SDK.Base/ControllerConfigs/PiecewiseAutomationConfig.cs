namespace TuneLab.SDK.Base.ControllerConfigs;

// 分段型自动化轨配置（分段 + 段间空，无默认基线——段间取值为 NaN，故不实现 IValueConfig）。
public class PiecewiseAutomationConfig : IControllerConfig
{
    public string? DisplayText { get; init; }
    public required double MinValue { get; init; }
    public required double MaxValue { get; init; }
    public required string Color { get; init; }
}
