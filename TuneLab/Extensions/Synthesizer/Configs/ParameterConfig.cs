using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Synthesizer.Configs;

internal class ParameterConfig
{
    public string id = string.Empty;
    public string type = string.Empty;
    public double min_value = double.NegativeInfinity;
    public double max_value = double.PositiveInfinity;
    public object? default_value = null;
    public string[]? options = null;
}
