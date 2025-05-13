﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Extensions.Formats.DataInfo;

public class VibratoInfo
{
    public double Pos { get; set; }
    public double Dur { get; set; }
    public double Frequency { get; set; }
    public double Phase { get; set; }
    public double Amplitude { get; set; }
    public double Attack { get; set; }
    public double Release { get; set; }
    public Map<string, double> AffectedAutomations { get; set; } = new();
}
