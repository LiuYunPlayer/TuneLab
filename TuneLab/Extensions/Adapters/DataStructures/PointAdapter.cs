using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base.DataStructures;

namespace TuneLab.Extensions.Adapters.DataStructures;

internal static class PointAdapter
{
    public static Point_V1 ToV1(this Point point)
    {
        return new Point_V1
        {
            X = point.X,
            Y = point.Y,
        };
    }

    public static Point ToDomain(this Point_V1 point)
    {
        return new Point
        {
            X = point.X,
            Y = point.Y,
        };
    }
}
