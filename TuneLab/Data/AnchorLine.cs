using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;

namespace TuneLab.Data;

internal class AnchorLine : DataList<Point>, IAnchorLine
{
    public double Start => this.First().X;
    public double End => this.Last().X;

    public AnchorLine()
    {

    }

    public double[] GetValues(IReadOnlyList<double> ticks)
    {
        return this.MonotonicHermiteInterpolation(ticks);
    }
}
