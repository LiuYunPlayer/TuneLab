using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Properties;

public class NumberConfig(double defaultValue = 0, double minValue = double.NegativeInfinity, double maxValue = double.PositiveInfinity, bool isInterger = false) : IValueConfig<double>
{
    public double DefaultValue { get; set; } = defaultValue;
    public double MinValue { get; set; } = minValue;
    public double MaxValue { get; set; } = maxValue;
    public bool IsInterger { get; set; } = isInterger;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
