using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.GUI.Components;
using TuneLab.Data;

namespace TuneLab.Views;

internal partial class TrackGrid
{
    class TrackGridItem(TrackGrid trackGrid) : Item
    {
        public TrackGrid TrackGrid => trackGrid;
    }

    class PartItem(TrackGrid trackGrid) : TrackGridItem(trackGrid)
    {
        public IPart Part;
        public int TrackIndex;

        public Rect Rect()
        {
            double top = TrackGrid.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = TrackGrid.TrackVerticalAxis.GetBottom(TrackIndex);
            double left = TrackGrid.TickAxis.Tick2X(Part.StartPos());
            double right = TrackGrid.TickAxis.Tick2X(Part.EndPos());

            return new Rect(left, top, right - left, bottom - top);
        }

        public override bool Raycast(Avalonia.Point point)
        {
            return Rect().Contains(point);
        }
    }

    class PartEndResizeItem(TrackGrid trackGrid) : TrackGridItem(trackGrid)
    {
        public IPart Part;
        public int TrackIndex;

        public override bool Raycast(Avalonia.Point point)
        {
            double top = TrackGrid.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = TrackGrid.TrackVerticalAxis.GetBottom(TrackIndex);
            double x = TrackGrid.TickAxis.Tick2X(Part.EndPos());
            return point.Y >= top && point.Y <= bottom && point.X > x - 8 && point.X < x + 8;
        }
    }

    class PartNameItem (TrackGrid trackGrid) : TrackGridItem(trackGrid)
    {
        public IPart Part;
        public int TrackIndex;

        public override bool Raycast(Avalonia.Point point)
        {
            double top = TrackGrid.TrackVerticalAxis.GetTop(TrackIndex);
            double left = TrackGrid.TickAxis.Tick2X(Part.StartPos());
            double right = TrackGrid.TickAxis.Tick2X(Part.EndPos());

            var titleRect = new Rect(left, top, right - left, 16);
            return titleRect.Contains(point);
        }
    }
}
