using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Data;

namespace TuneLab.UI;

internal partial class TimelineView
{
    class TimelineViewItem(TimelineView timelineView) : Item
    {
        public TimelineView TimelineView => timelineView;
    }

    class TempoItem(TimelineView timelineView) : TimelineViewItem(timelineView)
    {
        public ITempo Tempo => TempoManager.Tempos[TempoIndex];
        public required ITempoManager TempoManager;
        public required int TempoIndex;

        public double Left => TimelineView.TickAxis.Tick2X(Tempo.Pos);

        public Rect Rect()
        {
            return new Rect(Left, 24, TimelineView.TempoWidth(Tempo), 24);
        }

        public override bool Raycast(Point point)
        {
            return Rect().Contains(point);
        }
    }
}
