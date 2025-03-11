namespace TuneLab.SDK.Base.ControllerConfigs;

public class TextBoxConfig_V1 : IControllerConfig_V1
{
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsSingleLine { get; set; } = true;
}
