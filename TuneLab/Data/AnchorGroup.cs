using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Science;

namespace TuneLab.Data;

internal class AnchorGroup : DataList<AnchorPoint>, IAnchorGroup
{
    public double Start => this.First().Pos;
    public double End => this.Last().Pos;

    public AnchorGroup()
    {

    }

    public double[] GetValues(IReadOnlyList<double> ticks)
    {
        return this.Convert(p => p.ToPoint()).MonotonicHermiteInterpolation(ticks);
    }
}
