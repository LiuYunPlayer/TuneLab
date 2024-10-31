﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Format.DataInfo;

public abstract class PartInfo_V1
{
    public string Name { get; set; } = string.Empty;
    public double Pos { get; set; } = 0;
    public double Dur { get; set; } = 0;
}
