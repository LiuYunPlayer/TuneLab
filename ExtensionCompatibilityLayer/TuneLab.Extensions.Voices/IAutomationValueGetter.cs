﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Voices;

public interface IAutomationValueGetter
{
    double[] GetValue(IReadOnlyList<double> times);
}
