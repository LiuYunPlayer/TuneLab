using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Data;

internal interface IAnchorPoint : ISelectable
{
    double Pos { get; }
    double Value { get; }
}

internal static class IAnchorPointExtension
{
    public static Point ToPoint(this IAnchorPoint anchorPoint)
    {
        return new(anchorPoint.Pos, anchorPoint.Value);
    }
}
