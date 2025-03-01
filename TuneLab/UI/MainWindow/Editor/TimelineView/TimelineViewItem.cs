using Avalonia;
using Avalonia.Media;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.Utils;

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

        public override void Render(DrawingContext context)
        {
            if (TimelineView.mState == State.TempoMoving && Tempo == TimelineView.mTempoMovingOperation.Tempo)
            {
                context.FillRectangle(Style.BACK.ToBrush(), Rect());
            }

            context.DrawString(TimelineView.BpmString(Tempo), Rect(), TextBrush, 12, Alignment.Center, Alignment.Center);
        }

        static readonly IBrush TextBrush = new Color(178, 255, 255, 255).ToBrush();
    }

    class TimeSignatureItem(TimelineView timelineView) : TimelineViewItem(timelineView)
    {
        public ITimeSignature TimeSignature => TimeSignatureManager.TimeSignatures[TimeSignatureIndex];
        public required ITimeSignatureManager TimeSignatureManager;
        public required int TimeSignatureIndex;

        public double Left => TimelineView.TickAxis.Tick2X(TimeSignature.Pos);

        public Rect Rect()
        {
            return new Rect(Left, 0, TimelineView.TimeSignatureWidth(TimeSignature), 24);
        }

        public override bool Raycast(Point point)
        {
            return Rect().Contains(point);
        }

        public override void Render(DrawingContext context)
        {
            if (TimelineView.mState == State.TimeSignatureMoving && TimeSignature == TimelineView.mTimeSignatureMovingOperation.TimeSignature)
            {
                context.FillRectangle(Style.BACK.ToBrush(), Rect());
            }

            context.DrawString(TimelineView.MeterString(TimeSignature), Rect(), TextBrush, 12, Alignment.Center, Alignment.Center);
        }

        static readonly IBrush TextBrush = new Color(178, 255, 255, 255).ToBrush();
    }
}
