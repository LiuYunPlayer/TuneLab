namespace TuneLab.SDK.Base.ControllerConfigs;

public sealed class SliderConfig_V1 : IControllerConfig_V1
{
    public required double DefaultValue { get; set; }
    public required double MinValue { get; set; }
    public required double MaxValue { get; set; }
    public bool IsInteger { get; set; } = false;
}
