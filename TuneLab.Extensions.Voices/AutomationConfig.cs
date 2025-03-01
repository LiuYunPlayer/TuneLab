using TuneLab.Base.Properties;

namespace TuneLab.Extensions.Voices;

public class AutomationConfig(string name, double defaultValue, double minValue, double maxValue, string color) : NumberConfig(defaultValue, minValue, maxValue, false)
{
    public string Name { get; private set; } = name;
    public string Color { get; private set; } = color;
}
