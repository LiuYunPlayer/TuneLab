using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;

namespace TuneLab.Extensions.Voices;

public class AutomationConfig(string name, double defaultValue, double minValue, double maxValue, string color) : NumberConfig(defaultValue, minValue, maxValue, false)
{
    public string Name { get; private set; } = name;
    public string Color { get; private set; } = color;
}
