using Avalonia;
using TuneLab.Data;

namespace TuneLab.UI;

internal partial class TrackScrollView
{
    class TrackScrollViewItem(TrackScrollView trackScrollView) : Item
    {
        public TrackScrollView TrackScrollView => trackScrollView;
    }

    interface IPartItem
    {
        IPart Part { get; }
        int TrackIndex { get; }
    }

    class PartItem(TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView), IPartItem
    {
        public IPart Part { get; set; }
        public int TrackIndex { get; set; }

        public Rect Rect()
        {
            double top = TrackScrollView.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = TrackScrollView.TrackVerticalAxis.GetBottom(TrackIndex);
            double left = TrackScrollView.TickAxis.Tick2X(Part.StartPos());
            double right = TrackScrollView.TickAxis.Tick2X(Part.EndPos());

            return new Rect(left, top, right - left, bottom - top);
        }

        public override bool Raycast(Avalonia.Point point)
        {
            return Rect().Contains(point);
        }
    }

    class PartEndResizeItem(TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView)
    {
        public IPart Part;
        public int TrackIndex;

        public override bool Raycast(Avalonia.Point point)
        {
            double top = TrackScrollView.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = TrackScrollView.TrackVerticalAxis.GetBottom(TrackIndex);
            double x = TrackScrollView.TickAxis.Tick2X(Part.EndPos());
            return point.Y >= top && point.Y <= bottom && point.X > x - 8 && point.X < x + 8;
        }
    }

    class PartNameItem (TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView), IPartItem
    {
        public IPart Part { get; set; }
        public int TrackIndex { get; set; }

        public override bool Raycast(Avalonia.Point point)
        {
            double top = TrackScrollView.TrackVerticalAxis.GetTop(TrackIndex);
            double left = TrackScrollView.TickAxis.Tick2X(Part.StartPos());
            double right = TrackScrollView.TickAxis.Tick2X(Part.EndPos());

            var titleRect = new Rect(left, top, right - left, 16);
            return titleRect.Contains(point);
        }
    }
}
