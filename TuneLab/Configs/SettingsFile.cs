using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Configs;

internal class SettingsFile
{
    public string Language { get; set; } = string.Empty;
    public string BackgroudPath { get; set; } = string.Empty;
    public double ParameterExtend { get; set; } = 5;
    public string KeySamplesPath { get; set; } = string.Empty;
}
