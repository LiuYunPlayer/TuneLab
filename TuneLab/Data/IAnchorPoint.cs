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
