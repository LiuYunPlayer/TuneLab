namespace TuneLab.Extensions.ControllerConfigs;

public sealed class SliderConfig : IControllerConfig
{
    public required double DefaultValue { get; set; }
    public required double MinValue { get; set; }
    public required double MaxValue { get; set; }
    public bool IsInteger { get; set; } = false;
}
