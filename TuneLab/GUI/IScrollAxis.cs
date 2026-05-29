using System;

namespace TuneLab.GUI;

internal interface IScrollAxis
{
    event Action AxisChanged;
    double ViewLength { get; set; }
    double ViewOffset { get; set; }
    double ContentLength { get; }
}

internal static class IScrollAxisExtension
{
    public static Foundation.DataStructures.RangeF ViewRange(this IScrollAxis axis)
    {
        return new Foundation.DataStructures.RangeF(axis.ViewOffset, Math.Min(axis.ViewOffset + axis.ViewLength, axis.ContentLength));
    }
}
