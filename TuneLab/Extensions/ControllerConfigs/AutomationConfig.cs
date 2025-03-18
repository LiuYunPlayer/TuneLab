namespace TuneLab.Extensions.ControllerConfigs;

public class AutomationConfig : IControllerConfig
{
    public required double DefaultValue { get; set; }
    public required double MinValue { get; set; }
    public required double MaxValue { get; set; }
}
