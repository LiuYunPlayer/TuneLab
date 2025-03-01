using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;

namespace TuneLab.Data;

internal class AnchorPoint(double pos, double value) : IAnchorPoint
{
    public double Pos { get; } = pos;
    public double Value { get; } = value;

    public IActionEvent SelectionChanged => mSelectionChanged;
    public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }

    public static implicit operator AnchorPoint(Point point)
    {
        return new(point);
    }

    public AnchorPoint(Point point) : this(point.X, point.Y) { }

    readonly ActionEvent mSelectionChanged = new();

    bool mIsSelected = false;
}
