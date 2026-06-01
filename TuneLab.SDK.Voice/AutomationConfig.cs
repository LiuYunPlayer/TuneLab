using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Voice;

public class AutomationConfig(string name, double defaultValue, double minValue, double maxValue, string color) : SliderConfig(defaultValue, minValue, maxValue, false)
{
    public string Name { get; private set; } = name;
    public string Color { get; private set; } = color;
}
