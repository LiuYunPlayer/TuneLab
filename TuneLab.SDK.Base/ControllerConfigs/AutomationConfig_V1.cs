namespace TuneLab.SDK.Base.ControllerConfigs;

public class AutomationConfig_V1 : IControllerConfig_V1
{
    public required double DefaultValue { get; set; }
    public required double MinValue { get; set; }
    public required double MaxValue { get; set; }
}
