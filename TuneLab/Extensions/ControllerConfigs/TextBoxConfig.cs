using TuneLab.Foundation;

namespace TuneLab.Extensions.ControllerConfigs;

public class TextBoxConfig : IControllerConfig
{
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsSingleLine { get; set; } = true;
}
