using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Structures;

public struct RangeF
{
    public double min;
    public double max;
    public RangeF(double min, double max)
    {
        this.min = min;
        this.max = max;
    }
}
