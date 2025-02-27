﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public interface IAutomationValueGetter_V1
{
    double[] GetValue(IReadOnlyList<double> times);
}
